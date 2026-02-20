using System.Text.Json;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Messages;
using FabCopilot.Contracts.Models;
using FabCopilot.Messaging.Interfaces;

namespace FabCopilot.AlarmCopilot;

public sealed class AlarmCopilotWorker : BackgroundService
{
    private const string QueueGroup = "alarm-copilot-workers";
    private const string AlarmSubscriptionPattern = "equipment.*.alarm.triggered";

    private readonly IMessageBus _messageBus;
    private readonly ILogger<AlarmCopilotWorker> _logger;

    public AlarmCopilotWorker(
        IMessageBus messageBus,
        ILogger<AlarmCopilotWorker> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AlarmCopilotWorker started. Subscribing to {Subject} with queue group {QueueGroup}",
            AlarmSubscriptionPattern, QueueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<AlarmTriggered>(
                           AlarmSubscriptionPattern, QueueGroup, stoppingToken))
        {
            var alarm = envelope.Payload;
            if (alarm is null)
            {
                _logger.LogWarning("Received envelope with null payload, skipping. TraceId={TraceId}", envelope.TraceId);
                continue;
            }

            _logger.LogInformation(
                "Alarm triggered. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}, Severity={Severity}, TraceId={TraceId}",
                alarm.EquipmentId, alarm.AlarmCode, alarm.Severity, envelope.TraceId);

            _ = ProcessAlarmAsync(alarm, envelope.TraceId, stoppingToken);
        }

        _logger.LogInformation("AlarmCopilotWorker stopped");
    }

    private async Task ProcessAlarmAsync(AlarmTriggered alarm, string traceId, CancellationToken ct)
    {
        try
        {
            // Step 1: Request alarm window extraction via MCP
            _logger.LogInformation(
                "Requesting alarm window extraction. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}",
                alarm.EquipmentId, alarm.AlarmCode);

            var mcpParameters = JsonSerializer.SerializeToElement(new
            {
                equipmentId = alarm.EquipmentId,
                alarmCode = alarm.AlarmCode,
                triggeredAt = alarm.TriggeredAt,
                windowBeforeSec = 300,
                windowAfterSec = 60
            });

            var mcpRequest = new McpToolRequest
            {
                ToolName = "extract_alarm_window",
                Parameters = mcpParameters,
                TraceId = traceId,
                EquipmentId = alarm.EquipmentId
            };

            await _messageBus.PublishAsync(
                NatsSubjects.McpLogQueryRequest,
                MessageEnvelope<McpToolRequest>.Create(
                    "mcp.tool.request", mcpRequest, alarm.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published MCP tool request for alarm window. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}",
                alarm.EquipmentId, alarm.AlarmCode);

            // Step 2: Publish a ChatRequest with alarm analysis context
            // so the LlmService can generate a diagnostic response
            var conversationId = $"alarm-{alarm.EquipmentId}-{alarm.AlarmCode}-{alarm.TriggeredAt:yyyyMMddHHmmss}";

            var chatRequest = new ChatRequest
            {
                ConversationId = conversationId,
                EquipmentId = alarm.EquipmentId,
                UserMessage = BuildAlarmAnalysisPrompt(alarm),
                Context = new EquipmentContext
                {
                    Module = alarm.Module,
                    RecentAlarms = [alarm.AlarmCode]
                }
            };

            await _messageBus.PublishAsync(
                NatsSubjects.ChatRequest,
                MessageEnvelope<ChatRequest>.Create(
                    "chat.request.alarm", chatRequest, alarm.EquipmentId),
                ct);

            _logger.LogInformation(
                "Published chat request for alarm analysis. ConversationId={ConversationId}, EquipmentId={EquipmentId}",
                conversationId, alarm.EquipmentId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Alarm processing cancelled. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}",
                alarm.EquipmentId, alarm.AlarmCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing alarm. EquipmentId={EquipmentId}, AlarmCode={AlarmCode}, TraceId={TraceId}",
                alarm.EquipmentId, alarm.AlarmCode, traceId);
        }
    }

    private static string BuildAlarmAnalysisPrompt(AlarmTriggered alarm)
    {
        var prompt = $"An alarm has been triggered on equipment {alarm.EquipmentId}.\n" +
                     $"Alarm Code: {alarm.AlarmCode}\n" +
                     $"Severity: {alarm.Severity}\n" +
                     $"Triggered At: {alarm.TriggeredAt:O}\n";

        if (!string.IsNullOrWhiteSpace(alarm.Module))
            prompt += $"Module: {alarm.Module}\n";

        if (!string.IsNullOrWhiteSpace(alarm.Description))
            prompt += $"Description: {alarm.Description}\n";

        prompt += "\nPlease analyze this alarm and provide:\n" +
                  "1. Likely root causes\n" +
                  "2. Recommended immediate actions\n" +
                  "3. Relevant troubleshooting steps\n" +
                  "4. Any known resolutions for this alarm code";

        return prompt;
    }
}
