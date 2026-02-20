using System.Diagnostics;
using System.Text.Json;
using FabCopilot.Contracts.Constants;
using FabCopilot.Contracts.Messages;
using FabCopilot.McpLogServer.Interfaces;
using FabCopilot.Messaging.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FabCopilot.McpLogServer.Services;

/// <summary>
/// Dispatches MCP tool requests to the appropriate tool implementation.
/// Validates security policy (equipment scope, time range cap), executes the tool,
/// and publishes the result.
/// </summary>
public sealed class McpToolDispatcher
{
    private readonly Dictionary<string, IMcpTool> _tools;
    private readonly IMessageBus _messageBus;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpToolDispatcher> _logger;

    public McpToolDispatcher(
        IEnumerable<IMcpTool> tools,
        IMessageBus messageBus,
        IConfiguration configuration,
        ILogger<McpToolDispatcher> logger)
    {
        _tools = tools.ToDictionary(t => t.ToolName, StringComparer.OrdinalIgnoreCase);
        _messageBus = messageBus;
        _configuration = configuration;
        _logger = logger;

        _logger.LogInformation("McpToolDispatcher initialized with {ToolCount} tools: {ToolNames}",
            _tools.Count, string.Join(", ", _tools.Keys));
    }

    /// <summary>
    /// Subscribes to MCP tool requests, dispatches them, and publishes results.
    /// </summary>
    public async Task RunDispatchLoopAsync(CancellationToken ct)
    {
        const string queueGroup = "mcp-log-workers";

        _logger.LogInformation(
            "McpToolDispatcher subscribing to {Subject} with queue group {QueueGroup}",
            NatsSubjects.McpLogQueryRequest, queueGroup);

        await foreach (var envelope in _messageBus.SubscribeAsync<McpToolRequest>(
                           NatsSubjects.McpLogQueryRequest, queueGroup, ct))
        {
            var request = envelope.Payload;
            if (request is null)
            {
                _logger.LogWarning("Received envelope with null payload, skipping. TraceId={TraceId}", envelope.TraceId);
                continue;
            }

            _ = DispatchAsync(request, ct);
        }
    }

    private async Task DispatchAsync(McpToolRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        McpToolResult toolResult;

        try
        {
            // 1. Resolve the tool
            if (!_tools.TryGetValue(request.ToolName, out var tool))
            {
                _logger.LogWarning(
                    "Unknown MCP tool requested. ToolName={ToolName}, TraceId={TraceId}",
                    request.ToolName, request.TraceId);

                toolResult = new McpToolResult
                {
                    ToolName = request.ToolName,
                    TraceId = request.TraceId,
                    Error = $"Unknown tool: {request.ToolName}"
                };

                await PublishResultAsync(toolResult, request.EquipmentId, ct);
                return;
            }

            // 2. Build security context from configuration and request
            var security = BuildSecurityContext(request);

            // 3. Validate security policy
            ValidateSecurityPolicy(security);

            // 4. Log audit entry
            _logger.LogInformation(
                "Dispatching MCP tool. ToolName={ToolName}, EquipmentId={EquipmentId}, TraceId={TraceId}",
                request.ToolName, request.EquipmentId, request.TraceId);

            // 5. Execute the tool
            var result = await tool.ExecuteAsync(request.Parameters, security, ct);

            sw.Stop();

            toolResult = new McpToolResult
            {
                ToolName = request.ToolName,
                Result = result,
                TraceId = request.TraceId,
                Stats = new McpToolStats
                {
                    TookMs = sw.ElapsedMilliseconds
                }
            };

            _logger.LogInformation(
                "MCP tool execution completed. ToolName={ToolName}, EquipmentId={EquipmentId}, TookMs={TookMs}, TraceId={TraceId}",
                request.ToolName, request.EquipmentId, sw.ElapsedMilliseconds, request.TraceId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "MCP tool execution cancelled. ToolName={ToolName}, TraceId={TraceId}",
                request.ToolName, request.TraceId);
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(ex,
                "MCP tool execution failed. ToolName={ToolName}, EquipmentId={EquipmentId}, TraceId={TraceId}",
                request.ToolName, request.EquipmentId, request.TraceId);

            toolResult = new McpToolResult
            {
                ToolName = request.ToolName,
                TraceId = request.TraceId,
                Error = ex.Message,
                Stats = new McpToolStats { TookMs = sw.ElapsedMilliseconds }
            };
        }

        await PublishResultAsync(toolResult, request.EquipmentId, ct);
    }

    private McpSecurityContext BuildSecurityContext(McpToolRequest request)
    {
        var maxTimeRange = _configuration.GetValue("Mcp:MaxTimeRangeMinutes", 120);
        var maxRecords = _configuration.GetValue("Mcp:MaxRecordsPerQuery", 5000);

        return new McpSecurityContext
        {
            EquipmentId = request.EquipmentId,
            TraceId = request.TraceId,
            MaxTimeRangeMinutes = maxTimeRange,
            MaxRecords = maxRecords
        };
    }

    private void ValidateSecurityPolicy(McpSecurityContext security)
    {
        if (string.IsNullOrWhiteSpace(security.EquipmentId))
        {
            throw new InvalidOperationException(
                "Security policy violation: EquipmentId is required for all MCP tool requests.");
        }

        if (security.MaxTimeRangeMinutes <= 0)
        {
            throw new InvalidOperationException(
                "Security policy violation: MaxTimeRangeMinutes must be positive.");
        }
    }

    private async Task PublishResultAsync(McpToolResult result, string equipmentId, CancellationToken ct)
    {
        try
        {
            await _messageBus.PublishAsync(
                NatsSubjects.McpLogQueryResult,
                MessageEnvelope<McpToolResult>.Create(
                    "mcp.tool.result", result, equipmentId),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish MCP tool result. ToolName={ToolName}, TraceId={TraceId}",
                result.ToolName, result.TraceId);
        }
    }
}
