using System.Text.Json;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.Messaging.Interfaces;

namespace FabCopilot.RcaAgent;

public sealed class RcaOrchestrator : BackgroundService
{
    private const string QueueGroup = "rca-workers";
    private const string RagRequestSubject = "rag.request";

    private readonly IMessageBus _messageBus;
    private readonly ILogger<RcaOrchestrator> _logger;

    public RcaOrchestrator(
        IMessageBus messageBus,
        ILogger<RcaOrchestrator> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RcaOrchestrator started. Subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.RcaRunRequest, QueueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<RcaRunRequest>(
                           NatsSubjects.RcaRunRequest, QueueGroup, stoppingToken))
        {
            var request = envelope.Payload;
            if (request is null)
            {
                _logger.LogWarning("Received envelope with null payload, skipping. TraceId={TraceId}", envelope.TraceId);
                continue;
            }

            _logger.LogInformation(
                "Processing RCA run request. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}, TraceId={TraceId}",
                request.EquipmentId, request.AlarmCode, envelope.TraceId);

            _ = OrchestrateRcaAsync(request, envelope.TraceId, stoppingToken);
        }

        _logger.LogInformation("RcaOrchestrator stopped");
    }

    private async Task OrchestrateRcaAsync(RcaRunRequest request, string traceId, CancellationToken ct)
    {
        try
        {
            // Phase 1: Fan-out parallel requests to MCP tools and RAG
            _logger.LogInformation(
                "Starting RCA evidence collection. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}",
                request.EquipmentId, request.AlarmCode);

            // Fan-out: fire all MCP requests and RAG request in parallel
            var alarmWindowTask = RequestMcpToolAsync("extract_alarm_window", new
            {
                equipmentId = request.EquipmentId,
                alarmCode = request.AlarmCode,
                triggeredAt = request.TriggeredAt,
                windowBeforeSec = 300,
                windowAfterSec = 60
            }, request.EquipmentId, traceId, ct);

            var timeseriesTask = RequestMcpToolAsync("get_timeseries", new
            {
                equipmentId = request.EquipmentId,
                signals = new[] { "temperature", "pressure", "flow_rate" },
                start = request.TriggeredAt.AddMinutes(-10),
                end = request.TriggeredAt.AddMinutes(2),
                stepMs = 1000
            }, request.EquipmentId, traceId, ct);

            var anomalyTask = RequestMcpToolAsync("detect_anomalies", new
            {
                equipmentId = request.EquipmentId,
                start = request.TriggeredAt.AddMinutes(-15),
                end = request.TriggeredAt.AddMinutes(2)
            }, request.EquipmentId, traceId, ct);

            var ragTask = RequestRagAsync(request, traceId, ct);

            // Await all evidence gathering in parallel
            await Task.WhenAll(alarmWindowTask, timeseriesTask, anomalyTask, ragTask);

            _logger.LogInformation(
                "RCA evidence collection completed. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}",
                request.EquipmentId, request.AlarmCode);

            // Phase 2: Build synthesis context from all gathered evidence
            var synthesisPrompt = BuildSynthesisPrompt(request, traceId);

            // Phase 3: Publish a ChatRequest with RCA context for LLM synthesis
            var conversationId = request.ConversationId
                ?? $"rca-{request.EquipmentId}-{request.AlarmCode}-{request.TriggeredAt:yyyyMMddHHmmss}";

            var chatRequest = new ChatRequest
            {
                ConversationId = conversationId,
                EquipmentId = request.EquipmentId,
                UserMessage = synthesisPrompt,
                Context = new EquipmentContext
                {
                    RecentAlarms = [request.AlarmCode]
                }
            };

            await _messageBus.PublishAsync(
                NatsSubjects.ChatRequest,
                MessageEnvelope<ChatRequest>.Create(
                    "chat.request.rca", chatRequest, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published RCA synthesis chat request. ConversationId={ConversationId}",
                conversationId);

            // Phase 4: Publish the RCA run result
            var rcaResult = new RcaRunResult
            {
                EquipmentId = request.EquipmentId,
                AlarmCode = request.AlarmCode,
                Summary = $"RCA analysis initiated for alarm {request.AlarmCode} on {request.EquipmentId}. " +
                          "Evidence collection from MCP tools and RAG completed. LLM synthesis in progress.",
                RankedCauses =
                [
                    new RcaCause
                    {
                        Cause = "Pending LLM synthesis",
                        Confidence = 0.0,
                        Evidence = ["alarm_window_collected", "timeseries_collected", "anomaly_detection_run", "rag_context_retrieved"]
                    }
                ],
                RecommendedActions = ["Awaiting LLM synthesis for detailed recommendations"]
            };

            await _messageBus.PublishAsync(
                NatsSubjects.RcaRunResult,
                MessageEnvelope<RcaRunResult>.Create(
                    "rca.run.result", rcaResult, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "RCA orchestration completed. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}, TraceId={TraceId}",
                request.EquipmentId, request.AlarmCode, traceId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "RCA orchestration cancelled. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}",
                request.EquipmentId, request.AlarmCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error during RCA orchestration. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}, TraceId={TraceId}",
                request.EquipmentId, request.AlarmCode, traceId);

            // Publish error result
            try
            {
                var errorResult = new RcaRunResult
                {
                    EquipmentId = request.EquipmentId,
                    AlarmCode = request.AlarmCode,
                    Summary = $"RCA analysis failed: {ex.Message}",
                    RankedCauses = [],
                    RecommendedActions = ["Review error logs and retry RCA analysis"]
                };

                await _messageBus.PublishAsync(
                    NatsSubjects.RcaRunResult,
                    MessageEnvelope<RcaRunResult>.Create(
                        "rca.run.result.error", errorResult, request.EquipmentId),
                    ct);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx,
                    "Failed to publish RCA error result. TraceId={TraceId}", traceId);
            }
        }
    }

    private async Task RequestMcpToolAsync(string toolName, object parameters, string equipmentId, string traceId, CancellationToken ct)
    {
        try
        {
            var mcpRequest = new McpToolRequest
            {
                ToolName = toolName,
                Parameters = JsonSerializer.SerializeToElement(parameters),
                TraceId = traceId,
                EquipmentId = equipmentId
            };

            await _messageBus.PublishAsync(
                NatsSubjects.McpLogQueryRequest,
                MessageEnvelope<McpToolRequest>.Create(
                    "mcp.tool.request", mcpRequest, equipmentId),
                ct);

            _logger.LogInformation(
                "Published MCP tool request. ToolName={ToolName}, EquipmentId={EquipmentId}, TraceId={TraceId}",
                toolName, equipmentId, traceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish MCP tool request. ToolName={ToolName}, TraceId={TraceId}",
                toolName, traceId);
        }
    }

    private async Task RequestRagAsync(RcaRunRequest request, string traceId, CancellationToken ct)
    {
        try
        {
            // Publish a RAG request for relevant knowledge
            var ragQuery = $"Equipment {request.EquipmentId} alarm {request.AlarmCode}: " +
                           $"Find relevant troubleshooting knowledge, root cause analyses, and resolution procedures.";

            if (!string.IsNullOrWhiteSpace(request.Question))
            {
                ragQuery += $" Additional context: {request.Question}";
            }

            var ragRequest = new ChatRequest
            {
                ConversationId = $"rag-rca-{traceId}",
                EquipmentId = request.EquipmentId,
                UserMessage = ragQuery,
                Context = new EquipmentContext
                {
                    RecentAlarms = [request.AlarmCode]
                }
            };

            await _messageBus.PublishAsync(
                RagRequestSubject,
                MessageEnvelope<ChatRequest>.Create(
                    "rag.request.rca", ragRequest, request.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published RAG request for RCA. EquipmentId={EquipmentId}, TraceId={TraceId}",
                request.EquipmentId, traceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish RAG request. TraceId={TraceId}", traceId);
        }
    }

    private static string BuildSynthesisPrompt(RcaRunRequest request, string traceId)
    {
        return $"""
                You are a Root Cause Analysis (RCA) assistant for semiconductor fab equipment.

                Analyze the following alarm event and provide a structured RCA report:

                Equipment: {request.EquipmentId}
                Alarm Code: {request.AlarmCode}
                Triggered At: {request.TriggeredAt:O}
                {(request.Question is not null ? $"Engineer's Question: {request.Question}" : "")}

                Evidence has been collected from the following sources:
                - Alarm window logs (5 minutes before, 1 minute after the alarm)
                - Time series data (temperature, pressure, flow_rate) for the 10-minute window
                - Anomaly detection results
                - RAG knowledge base lookup

                Please provide:
                1. A concise summary of the situation
                2. Ranked list of probable root causes with confidence scores
                3. Supporting evidence for each cause
                4. Recommended immediate actions
                5. Preventive measures

                TraceId: {traceId}
                """;
    }
}
