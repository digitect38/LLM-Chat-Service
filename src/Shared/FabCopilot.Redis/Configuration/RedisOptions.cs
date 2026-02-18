namespace FabCopilot.Redis.Configuration;

public class RedisOptions
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; set; } = "localhost:6379";
    public int DefaultDatabase { get; set; } = 0;
    public string KeyPrefix { get; set; } = "fabcopilot:";
}
