using FabCopilot.McpLogServer.Services;

namespace FabCopilot.McpLogServer;

public sealed class McpLogWorker : BackgroundService
{
    private readonly McpToolDispatcher _dispatcher;
    private readonly ILogger<McpLogWorker> _logger;

    public McpLogWorker(
        McpToolDispatcher dispatcher,
        ILogger<McpLogWorker> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("McpLogWorker started");

        try
        {
            await _dispatcher.RunDispatchLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("McpLogWorker stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "McpLogWorker encountered an unexpected error");
            throw;
        }

        _logger.LogInformation("McpLogWorker stopped");
    }
}
