namespace FabCopilot.Contracts.Constants;

public static class NatsSubjects
{
    // Equipment-scoped
    public static string EquipmentChatRequest(string toolId) => $"equipment.{toolId}.chat.request";
    public static string EquipmentChatStream(string toolId) => $"equipment.{toolId}.chat.stream";
    public static string EquipmentAlarmTriggered(string toolId) => $"equipment.{toolId}.alarm.triggered";
    public static string EquipmentLogAnomaly(string toolId) => $"equipment.{toolId}.log.anomaly";

    // Platform
    public const string ChatRequest = "chat.request";
    public static string ChatStream(string conversationId) => $"chat.stream.{conversationId}";
    public const string KnowledgeExtractRequest = "knowledge.extract.request";
    public const string KnowledgeExtractResult = "knowledge.extract.result";
    public const string McpLogQueryRequest = "mcp.log.query.request";
    public const string McpLogQueryResult = "mcp.log.query.result";
    public const string RcaRunRequest = "rca.run.request";
    public const string RcaRunResult = "rca.run.result";
    public const string RagRequest = "rag.request";
    public static string RagResponse(string conversationId) => $"rag.response.{conversationId}";

    // JetStream Streams
    public const string StreamChatRequests = "CHAT_REQUESTS";
    public const string StreamChatStreams = "CHAT_STREAMS";
    public const string StreamAlarms = "ALARMS";
    public const string StreamKnowledge = "KNOWLEDGE";
    public const string StreamMcpLog = "MCP_LOG";
    public const string StreamRca = "RCA";
}
