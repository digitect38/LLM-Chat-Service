using FabCopilot.Contracts.Enums;
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
