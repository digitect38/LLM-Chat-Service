using System.Collections.Concurrent;
using FabCopilot.RagService.Configuration;
using FabCopilot.RagService.Interfaces;
using FabCopilot.RagService.Services.Bm25;
using FabCopilot.RagService.Services.ImageOcr;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using Microsoft.Extensions.Options;

namespace FabCopilot.RagService.Services;

public sealed class FileWatcherIngestorService : BackgroundService
{
    private const string EquipmentId = "shared";
    private const int MaxRetries = 3;

    private readonly DocumentIngestor _ingestor;
    private readonly FileTextExtractor _extractor;
    private readonly IVectorStore _vectorStore;
    private readonly IBm25Index? _bm25Index;
    private readonly IRagCache? _ragCache;
    private readonly IImageOcrExtractor? _ocrExtractor;
    private readonly QdrantOptions _qdrantOptions;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<FileWatcherIngestorService> _logger;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTimers = new();

    private FileSystemWatcher? _watcher;
    private string _watchFolder = null!;

    public FileWatcherIngestorService(
        DocumentIngestor ingestor,
        FileTextExtractor extractor,
        IVectorStore vectorStore,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<FileWatcherIngestorService> logger,
        IBm25Index? bm25Index = null,
        IRagCache? ragCache = null,
        IImageOcrExtractor? ocrExtractor = null)
    {
        _ingestor = ingestor;
        _extractor = extractor;
        _vectorStore = vectorStore;
        _bm25Index = bm25Index;
        _ragCache = ragCache;
        _ocrExtractor = ocrExtractor;
        _qdrantOptions = qdrantOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_ragOptions.WatchFolder))
        {
            _logger.LogInformation("FileWatcher disabled: WatchFolder is not configured");
            return;
        }

        _watchFolder = Path.GetFullPath(_ragOptions.WatchFolder);

        if (!Directory.Exists(_watchFolder))
        {
            Directory.CreateDirectory(_watchFolder);
            _logger.LogInformation("Created watch folder: {WatchFolder}", _watchFolder);
        }

        _logger.LogInformation("FileWatcher starting. WatchFolder={WatchFolder}, DebounceMs={DebounceMs}, ScanOnStartup={ScanOnStartup}",
            _watchFolder, _ragOptions.DebounceMs, _ragOptions.ScanOnStartup);

        // Ensure collection exists
        await _vectorStore.EnsureCollectionAsync(
            _qdrantOptions.DefaultCollection, _qdrantOptions.VectorSize, stoppingToken);

        // Scan existing files on startup (BM25 index is rebuilt during ingestion)
        if (_ragOptions.ScanOnStartup)
        {
            _bm25Index?.Clear();
            await ScanExistingFilesAsync(stoppingToken);
            _logger.LogInformation("BM25 index rebuilt with {DocCount} chunks",
                _bm25Index?.DocumentCount ?? 0);
        }

        // Start watching
        _watcher = new FileSystemWatcher(_watchFolder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += (_, e) => OnFileChanged(e.FullPath, stoppingToken);
        _watcher.Changed += (_, e) => OnFileChanged(e.FullPath, stoppingToken);
        _watcher.Deleted += (_, e) => OnFileDeleted(e.FullPath, stoppingToken);
        _watcher.Renamed += (_, e) => OnFileRenamed(e.OldFullPath, e.FullPath, stoppingToken);
        _watcher.Error += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error");

        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("FileWatcher active and monitoring {WatchFolder}", _watchFolder);

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();

        foreach (var cts in _debounceTimers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _debounceTimers.Clear();
        base.Dispose();
    }

    private async Task ScanExistingFilesAsync(CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(_watchFolder, "*.*", SearchOption.AllDirectories)
            .Where(FileTextExtractor.IsSupported)
            .ToList();

        _logger.LogInformation("Scanning {FileCount} existing files in {WatchFolder}", files.Count, _watchFolder);

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await IngestFileAsync(file, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest existing file {FilePath}", file);
            }
        }

        _logger.LogInformation("Startup scan complete");
    }

    private void OnFileChanged(string fullPath, CancellationToken stoppingToken)
    {
        if (!FileTextExtractor.IsSupported(fullPath)) return;
        DebounceAndExecute(fullPath, () => IngestFileWithRetryAsync(fullPath, stoppingToken));
    }

    private void OnFileDeleted(string fullPath, CancellationToken stoppingToken)
    {
        if (!FileTextExtractor.IsSupported(fullPath)) return;
        DebounceAndExecute(fullPath, () => DeleteDocumentAsync(fullPath, stoppingToken));
    }

    private void OnFileRenamed(string oldPath, string newPath, CancellationToken stoppingToken)
    {
        // Delete old document
        if (FileTextExtractor.IsSupported(oldPath))
        {
            DebounceAndExecute(oldPath, () => DeleteDocumentAsync(oldPath, stoppingToken));
        }

        // Ingest new document
        if (FileTextExtractor.IsSupported(newPath))
        {
            DebounceAndExecute(newPath, () => IngestFileWithRetryAsync(newPath, stoppingToken));
        }
    }

    private void DebounceAndExecute(string filePath, Func<Task> action)
    {
        // Cancel any pending debounce for this file
        if (_debounceTimers.TryRemove(filePath, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _debounceTimers[filePath] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_ragOptions.DebounceMs, cts.Token);

                _debounceTimers.TryRemove(filePath, out _);

                await action();
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled — a newer event superseded this one
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file event for {FilePath}", filePath);
            }
        });
    }

    private async Task IngestFileWithRetryAsync(string fullPath, CancellationToken ct)
    {
        var delays = new[] { 500, 1000, 2000 };

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await IngestFileAsync(fullPath, ct);
                return;
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                _logger.LogWarning("File locked, retry {Attempt}/{MaxRetries} for {FilePath}",
                    attempt + 1, MaxRetries, fullPath);
                await Task.Delay(delays[attempt], ct);
            }
        }
    }

    private async Task IngestFileAsync(string fullPath, CancellationToken ct)
    {
        var documentId = GetDocumentId(fullPath, _watchFolder);
        var collection = _qdrantOptions.DefaultCollection;

        _logger.LogInformation("Ingesting file {FilePath} as document {DocumentId}", fullPath, documentId);

        // Delete existing chunks for this document (handles re-ingestion with different chunk count)
        await _vectorStore.DeleteByDocumentIdAsync(collection, documentId, ct);

        // Build metadata
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "file-watcher",
            ["file_path"] = documentId,
            ["file_name"] = Path.GetFileName(fullPath),
            ["file_extension"] = Path.GetExtension(fullPath).ToLowerInvariant(),
            ["ingested_at"] = DateTimeOffset.UtcNow.ToString("o")
        };

        // PDF files: page-by-page ingestion with page numbers
        if (FileTextExtractor.IsPdf(fullPath))
        {
            var pages = _extractor.ExtractPdfPagesWithTables(fullPath);
            await _ingestor.IngestPdfPagesAsync(documentId, pages, EquipmentId, metadata, ct);
        }
        else if (FileTextExtractor.IsImage(fullPath))
        {
            // Image files: extract text via OCR service (optional)
            if (_ocrExtractor is not null)
            {
                var ocrText = await _ocrExtractor.ExtractTextAsync(fullPath, ct);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    metadata["content_type"] = "image_ocr";
                    metadata["original_format"] = Path.GetExtension(fullPath).ToLowerInvariant();
                    await _ingestor.IngestTextAsync(documentId, ocrText, EquipmentId, metadata, ct);
                }
                else
                {
                    _logger.LogInformation("OCR returned empty text for image {FilePath}, skipping", fullPath);
                }
            }
            else
            {
                _logger.LogDebug("No OCR extractor configured, skipping image {FilePath}", fullPath);
            }
        }
        else
        {
            var text = _extractor.ExtractText(fullPath);
            await _ingestor.IngestTextAsync(documentId, text, EquipmentId, metadata, ct);
        }

        // Invalidate RAG cache when documents change
        if (_ragCache is not null)
            await _ragCache.InvalidateAllAsync(ct);

        _logger.LogInformation("File ingested successfully: {DocumentId}", documentId);
    }

    private async Task DeleteDocumentAsync(string fullPath, CancellationToken ct)
    {
        var documentId = GetDocumentId(fullPath, _watchFolder);
        var collection = _qdrantOptions.DefaultCollection;

        _logger.LogInformation("Deleting document {DocumentId} for removed file {FilePath}", documentId, fullPath);

        await _vectorStore.DeleteByDocumentIdAsync(collection, documentId, ct);

        // Remove from BM25 index (all chunks for this document)
        _bm25Index?.RemoveByPrefix(documentId);

        // Invalidate RAG cache when documents change
        if (_ragCache is not null)
            await _ragCache.InvalidateAllAsync(ct);

        _logger.LogInformation("Document deleted: {DocumentId}", documentId);
    }

    internal static string GetDocumentId(string filePath, string watchFolder)
    {
        var fullPath = Path.GetFullPath(filePath);
        var basePath = Path.GetFullPath(watchFolder);

        // Ensure base path ends with separator for correct relative path computation
        if (!basePath.EndsWith(Path.DirectorySeparatorChar))
            basePath += Path.DirectorySeparatorChar;

        var relativePath = Path.GetRelativePath(basePath, fullPath);

        // Normalize: forward slashes, lowercase
        return relativePath
            .Replace('\\', '/')
            .ToLowerInvariant();
    }
}
