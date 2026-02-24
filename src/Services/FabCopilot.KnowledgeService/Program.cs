using FabCopilot.Contracts.Configuration;
using FabCopilot.Contracts.Enums;
using FabCopilot.Contracts.Interfaces;
using FabCopilot.Contracts.Models;
using FabCopilot.KnowledgeService;
using FabCopilot.KnowledgeService.Services;
using FabCopilot.Messaging.Extensions;
using FabCopilot.Redis.Extensions;
using FabCopilot.Llm.Extensions;
using FabCopilot.VectorStore.Extensions;
using FabCopilot.Observability.Extensions;

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
