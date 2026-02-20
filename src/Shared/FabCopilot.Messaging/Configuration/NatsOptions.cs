namespace FabCopilot.Messaging.Configuration;

public class NatsOptions
{
    public const string SectionName = "Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    public int MaxReconnectRetries { get; set; } = -1;

    public int ReconnectWaitMs { get; set; } = 2000;
}
