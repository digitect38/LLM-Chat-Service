namespace FabCopilot.ChatGateway.Configuration;

public class TtsOptions
{
    public const string SectionName = "Tts";
    public string Provider { get; set; } = "Kokoro";           // v4.84: Kokoro Primary (on-prem)
    public string Voice { get; set; } = "af_heart";            // v4.84: Kokoro default voice
    public float Speed { get; set; } = 1.0f;
    public EdgeTtsSettings EdgeTts { get; set; } = new();
    public XttsSettings Xtts { get; set; } = new();
    public OpenAiCompatSettings Kokoro { get; set; } = new() { BaseUrl = "http://localhost:8401" };
    public OpenAiCompatSettings CosyVoice { get; set; } = new() { BaseUrl = "http://localhost:8402" };
    public FishSpeechSettings FishSpeech { get; set; } = new() { BaseUrl = "http://localhost:8403" };
    public OpenAiCompatSettings Chatterbox { get; set; } = new() { BaseUrl = "http://localhost:8404" };
    public BarkSettings Bark { get; set; } = new() { BaseUrl = "http://localhost:8405" };
    public OpenAiCompatSettings Piper { get; set; } = new() { BaseUrl = "http://localhost:8406" };
    public OpenAiCompatSettings Orpheus { get; set; } = new() { BaseUrl = "http://localhost:8407" };
    public int TimeoutSeconds { get; set; } = 30;
}

public class EdgeTtsSettings
{
    public string BaseUrl { get; set; } = "http://localhost:5050";
}

public class XttsSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8400";
    public string SpeakerWav { get; set; } = "default";
    public int MaxChars { get; set; } = 90;
}

public class OpenAiCompatSettings
{
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "tts-1";
}

public class FishSpeechSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8403";
    public string ReferenceId { get; set; } = "default";
}

public class BarkSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8405";
    public string Speaker { get; set; } = "v2/ko_speaker_0";
}
