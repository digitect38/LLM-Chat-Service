using FabCopilot.Contracts.Configuration;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Interfaces;
using FabCopilot.Contracts.Models;
using FabCopilot.KnowledgeService;
using FabCopilot.KnowledgeService.Services;
using FabCopilot.Messaging.Extensions;
using FabCopilot.Redis.Extensions;
using FabCopilot.Llm.Extensions;
using FabCopilot.VectorStore;
using FabCopilot.VectorStore.Extensions;
using FabCopilot.Observability.Extensions;
using FabCopilot.RagService.Services.Evaluation;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddFabObservability(builder.Configuration);
builder.Services.AddFabMessaging(builder.Configuration);
builder.Services.AddFabRedis(builder.Configuration);
builder.Services.AddFabLlm(builder.Configuration);
builder.Services.AddFabVectorStore(builder.Configuration);
builder.Services.AddFabTelemetry(builder.Configuration);
builder.Services.Configure<GraphOptions>(builder.Configuration.GetSection(GraphOptions.SectionName));
builder.Services.AddSingleton<IKnowledgeGraphStore, FabCopilot.Redis.RedisKnowledgeGraphStore>();
builder.Services.AddSingleton<KnowledgeManager>();
builder.Services.AddSingleton<RagEvaluationService>();
builder.Services.AddHostedService<KnowledgeWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/knowledge", async (CreateKnowledgeDraftRequest body, KnowledgeManager manager, CancellationToken ct) =>
{
    var draft = await manager.CreateDraftAsync(
        body.Type, body.Equipment, body.Symptom, body.RootCause, body.Solution, ct);
    return Results.Ok(draft);
});

app.MapPut("/api/knowledge/{id}/status", async (string id, UpdateStatusRequest body, KnowledgeManager manager, CancellationToken ct) =>
{
    var success = await manager.UpdateStatusAsync(id, body.Status, body.ApprovedBy, ct);
    if (!success) return Results.NotFound();
    return Results.Ok(new { id, status = body.Status.ToString() });
});

app.MapGet("/api/knowledge/pending", async (KnowledgeManager manager, CancellationToken ct) =>
{
    var items = await manager.ListPendingReviewAsync(ct);
    return Results.Ok(items);
});

app.MapPost("/api/knowledge/{id}/index", async (string id, KnowledgeManager manager, CancellationToken ct) =>
{
    var success = await manager.IndexApprovedAsync(id, ct);
    if (!success) return Results.NotFound();
    return Results.Ok(new { id, indexed = true });
});

// ─── Graph Management API ─────────────────────────────────────────

app.MapGet("/api/graph/entities", async (string? type, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    var entities = await graphStore.ListEntitiesAsync(type, ct);
    return Results.Ok(entities);
});

app.MapGet("/api/graph/entities/{name}", async (string name, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    var entity = await graphStore.GetEntityAsync(name, ct);
    if (entity is null) return Results.NotFound();
    return Results.Ok(entity);
});

app.MapGet("/api/graph/entities/{name}/related", async (string name, int? depth, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    var maxDepth = depth ?? 2;
    var entities = await graphStore.GetRelatedEntitiesAsync(name, maxDepth, ct);
    var relations = await graphStore.GetRelatedRelationsAsync(name, maxDepth, ct);
    return Results.Ok(new { entities, relations });
});

app.MapPost("/api/graph/entities", async (GraphEntity entity, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(entity.Name))
        return Results.BadRequest(new { error = "Entity name is required" });

    if (string.IsNullOrWhiteSpace(entity.Id))
        entity.Id = Guid.NewGuid().ToString();

    await graphStore.UpsertEntityAsync(entity, ct);
    return Results.Created($"/api/graph/entities/{entity.Name}", entity);
});

app.MapDelete("/api/graph/entities/{name}", async (string name, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    await graphStore.DeleteEntityAsync(name, ct);
    return Results.NoContent();
});

app.MapPost("/api/graph/relations", async (GraphRelation relation, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(relation.SourceId) || string.IsNullOrWhiteSpace(relation.TargetId))
        return Results.BadRequest(new { error = "SourceId and TargetId are required" });

    await graphStore.UpsertRelationAsync(relation, ct);
    return Results.Created($"/api/graph/relations", relation);
});

app.MapGet("/api/graph/search", async (string? query, IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(query))
        return Results.BadRequest(new { error = "Query parameter is required" });

    var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    var context = await graphStore.BuildGraphContextAsync(query, keywords, ct);
    var entities = new List<GraphEntity>();
    var relations = new List<GraphRelation>();

    foreach (var keyword in keywords)
    {
        var relatedEntities = await graphStore.GetRelatedEntitiesAsync(keyword, 2, ct);
        entities.AddRange(relatedEntities);
        var relatedRelations = await graphStore.GetRelatedRelationsAsync(keyword, 2, ct);
        relations.AddRange(relatedRelations);
    }

    // Deduplicate
    var uniqueEntities = entities
        .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();

    var uniqueRelations = relations
        .GroupBy(r => $"{r.SourceId}:{r.RelationType}:{r.TargetId}", StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();

    return Results.Ok(new { entities = uniqueEntities, relations = uniqueRelations, context });
});

app.MapGet("/api/graph/stats", async (IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    var stats = await graphStore.GetStatsAsync(ct);
    return Results.Ok(stats);
});

app.MapPost("/api/graph/rebuild-keyword-index", async (IKnowledgeGraphStore graphStore, CancellationToken ct) =>
{
    await graphStore.RebuildKeywordIndexAsync(ct);
    return Results.Ok(new { status = "rebuilt" });
});

// ─── RAG Evaluation API ─────────────────────────────────────────

EvaluationReport? _latestReport = null;

app.MapPost("/api/evaluation/run", (EvaluationRunRequest body, RagEvaluationService evaluationService) =>
{
    if (string.IsNullOrWhiteSpace(body.KnowledgeDocsPath))
        return Results.BadRequest(new { error = "knowledgeDocsPath is required" });

    if (!Directory.Exists(body.KnowledgeDocsPath))
        return Results.BadRequest(new { error = $"Directory not found: {body.KnowledgeDocsPath}" });

    var groundTruthPath = body.GroundTruthPath;
    if (string.IsNullOrWhiteSpace(groundTruthPath))
    {
        groundTruthPath = Path.Combine(AppContext.BaseDirectory, "rag-evaluation-groundtruth.json");
        if (!File.Exists(groundTruthPath))
            return Results.BadRequest(new { error = "Ground truth file not found. Provide groundTruthPath parameter." });
    }

    var dataset = RagEvaluationService.LoadGroundTruth(groundTruthPath);
    var k = body.K > 0 ? body.K : 10;
    var report = evaluationService.EvaluateBm25(dataset, body.KnowledgeDocsPath, k);
    _latestReport = report;

    return Results.Ok(new
    {
        report,
        summary = RagEvaluationService.FormatSummary(report)
    });
});

app.MapGet("/api/evaluation/report", () =>
{
    if (_latestReport is null)
        return Results.NotFound(new { error = "No evaluation report available. Run POST /api/evaluation/run first." });

    return Results.Ok(new
    {
        report = _latestReport,
        summary = RagEvaluationService.FormatSummary(_latestReport)
    });
});

// ─── Equipment Registry API ─────────────────────────────────────

app.MapGet("/api/equipment", async (string? fab, string? type, EquipmentStatus? status, IEquipmentRegistry registry, CancellationToken ct) =>
{
    var items = await registry.ListAsync(fab, type, status, ct);
    return Results.Ok(items);
});

app.MapGet("/api/equipment/{id}", async (string id, IEquipmentRegistry registry, CancellationToken ct) =>
{
    var equipment = await registry.GetAsync(id, ct);
    if (equipment is null) return Results.NotFound();
    return Results.Ok(equipment);
});

app.MapPost("/api/equipment", async (EquipmentRegistration body, IEquipmentRegistry registry, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.EquipmentId))
        return Results.BadRequest(new { error = "equipmentId is required" });

    await registry.RegisterAsync(body, ct);
    return Results.Created($"/api/equipment/{body.EquipmentId}", body);
});

app.MapPut("/api/equipment/{id}/status", async (string id, UpdateEquipmentStatusRequest body, IEquipmentRegistry registry, CancellationToken ct) =>
{
    var existing = await registry.GetAsync(id, ct);
    if (existing is null) return Results.NotFound();

    await registry.UpdateStatusAsync(id, body.Status, ct);
    return Results.Ok(new { equipmentId = id, status = body.Status.ToString() });
});

app.MapDelete("/api/equipment/{id}", async (string id, IEquipmentRegistry registry, CancellationToken ct) =>
{
    var existing = await registry.GetAsync(id, ct);
    if (existing is null) return Results.NotFound();

    await registry.RemoveAsync(id, ct);
    return Results.NoContent();
});

// ─── Dual-Index Management API ───────────────────────────────────

app.MapGet("/api/dual-index/status", (DualIndexManager dualIndex) =>
{
    return Results.Ok(new
    {
        activeCollection = dualIndex.ActiveCollection,
        standbyCollection = dualIndex.StandbyCollection,
        activeModelId = dualIndex.ActiveModelId,
        standbyModelId = dualIndex.StandbyModelId
    });
});

app.MapPost("/api/dual-index/prepare", async (PrepareStandbyRequest body, DualIndexManager dualIndex, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.ModelId))
        return Results.BadRequest(new { error = "modelId is required" });

    if (body.VectorSize <= 0)
        return Results.BadRequest(new { error = "vectorSize must be positive" });

    await dualIndex.PrepareStandbyAsync(body.ModelId, body.VectorSize, ct);
    return Results.Ok(new
    {
        standbyCollection = dualIndex.StandbyCollection,
        standbyModelId = dualIndex.StandbyModelId
    });
});

app.MapPost("/api/dual-index/promote", (DualIndexManager dualIndex) =>
{
    var result = dualIndex.PromoteStandby();
    if (!result.Success)
        return Results.BadRequest(new { error = result.Message });

    return Results.Ok(new
    {
        activeCollection = dualIndex.ActiveCollection,
        previousModelId = result.PreviousModelId,
        previousCollection = result.PreviousCollection,
        message = result.Message
    });
});

app.MapPost("/api/dual-index/rollback", (RollbackRequest body, DualIndexManager dualIndex) =>
{
    if (string.IsNullOrWhiteSpace(body.PreviousModelId))
        return Results.BadRequest(new { error = "previousModelId is required" });

    var success = dualIndex.Rollback(body.PreviousModelId);
    return Results.Ok(new
    {
        success,
        activeCollection = dualIndex.ActiveCollection,
        activeModelId = dualIndex.ActiveModelId
    });
});

app.Run();

// Minimal request DTOs for the API endpoints
public sealed record CreateKnowledgeDraftRequest(
    string Type,
    string? Equipment,
    string? Symptom,
    string? RootCause,
    string? Solution);

public sealed record UpdateStatusRequest(
    KnowledgeStatus Status,
    string? ApprovedBy);

public sealed record EvaluationRunRequest(
    string KnowledgeDocsPath,
    string? GroundTruthPath = null,
    int K = 10);

public sealed record UpdateEquipmentStatusRequest(
    EquipmentStatus Status);

public sealed record PrepareStandbyRequest(
    string ModelId,
    int VectorSize);

public sealed record RollbackRequest(
    string PreviousModelId);
