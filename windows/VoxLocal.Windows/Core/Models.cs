using System.Text.Json.Serialization;

namespace VoxLocal.Win.Core;

public enum DictationState
{
    Idle,
    Preparing,
    Recording,
    Stopping,
    Transcribing,
    Refining,
    Inserting,
    Completed,
    Cancelled,
    Error
}

public sealed class DictationStateMachine
{
    private static readonly IReadOnlyDictionary<DictationState, HashSet<DictationState>> Allowed =
        new Dictionary<DictationState, HashSet<DictationState>>
        {
            [DictationState.Idle] = [DictationState.Preparing],
            [DictationState.Preparing] = [DictationState.Recording, DictationState.Cancelled, DictationState.Error],
            [DictationState.Recording] = [DictationState.Stopping, DictationState.Cancelled, DictationState.Error],
            [DictationState.Stopping] = [DictationState.Transcribing, DictationState.Cancelled, DictationState.Error],
            [DictationState.Transcribing] = [DictationState.Refining, DictationState.Inserting, DictationState.Cancelled, DictationState.Error],
            [DictationState.Refining] = [DictationState.Inserting, DictationState.Cancelled, DictationState.Error],
            [DictationState.Inserting] = [DictationState.Completed, DictationState.Cancelled, DictationState.Error],
            [DictationState.Completed] = [DictationState.Idle],
            [DictationState.Cancelled] = [DictationState.Idle],
            [DictationState.Error] = [DictationState.Idle]
        };

    public DictationState State { get; private set; } = DictationState.Idle;
    public bool IsActive => State != DictationState.Idle;
    public bool IsCancellable => State is >= DictationState.Preparing and <= DictationState.Inserting;

    public bool CanTransition(DictationState next) =>
        Allowed.TryGetValue(State, out var states) && states.Contains(next);

    public void Transition(DictationState next)
    {
        if (!CanTransition(next))
            throw new InvalidOperationException($"Invalid dictation transition {State} -> {next}");
        State = next;
    }
}

public enum HotkeyMode
{
    PressAndHold,
    Toggle
}

public enum SpokenLanguage
{
    Auto,
    Russian,
    English
}

public enum InsertionMode
{
    Automatic,
    ClipboardOnly
}

public enum RecognitionEngine
{
    Whisper,
    SherpaTOneRussian
}

public enum RefinementPreset
{
    RawTranscript,
    CleanDictation,
    Concise,
    BusinessStyle,
    PreserveSpokenWording
}

public enum RefinementProvider
{
    Ollama,
    OpenAiCompatible
}

public static class OpenAiCompatibleModelCatalog
{
    public static IReadOnlyList<string> Models { get; } =
    [
        "claude-fable-5", "claude-haiku-4-5", "claude-opus-4-6", "claude-opus-4-7", "claude-opus-4-8", "claude-opus-5",
        "claude-sonnet-4-6", "claude-sonnet-5", "composer-2.5-fast", "deepseek-v4-flash", "deepseek-v4-pro", "deepseek-v4.1-flash",
        "gemini-3.1-pro", "gemini-3.6-flash", "gemini-3.7-flash", "gemini-3.8-flash", "glm-5-turbo", "glm-5.2", "glm-5.3", "glm-5.3-flash",
        "gpt-5.5", "gpt-5.6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-6-astra", "gpt-6-luna", "gpt-6-sol",
        "grok-4.5", "grok-4.6", "grok-4.7", "hy4-preview", "kimi-k2.7-code", "kimi-k3", "mimo-v2.5", "mimo-v2.5-pro",
        "minimax-m3", "muse-spark-1.3", "qwen3.8-flash", "qwen3.8-max"
    ];
}

public sealed class AppSettings
{
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.PressAndHold;
    public bool UseExactAltSpace { get; set; } = true;
    // The local Russian Sherpa model is the low-latency default. Whisper is
    // still available in Settings when transcription quality is preferred.
    public RecognitionEngine RecognitionEngine { get; set; } = RecognitionEngine.SherpaTOneRussian;
    // `base` is the smallest model that reliably handles short Russian
    // dictation. Users can explicitly select `tiny` when speed is preferred.
    public string WhisperModel { get; set; } = "base";
    public SpokenLanguage SpokenLanguage { get; set; } = SpokenLanguage.Russian;
    // Dictation happens after the key is released, so use the available CPU
    // capacity by default instead of deliberately leaving half of it idle.
    public int WhisperThreads { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 8);
    public int SherpaThreads { get; set; } = 1;
    public bool RemoveArtifacts { get; set; } = true;
    public InsertionMode InsertionMode { get; set; } = InsertionMode.Automatic;
    public bool RefinementEnabled { get; set; }
    public RefinementProvider RefinementProvider { get; set; } = RefinementProvider.Ollama;
    public RefinementPreset RefinementPreset { get; set; } = RefinementPreset.CleanDictation;
    public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string OllamaModel { get; set; } = "";
    public string OpenAiBaseUrl { get; set; } = "https://triklz27.ru/v1";
    public string OpenAiModel { get; set; } = "gemini-3.6-flash";
    public string OpenAiApiKeyProtected { get; set; } = "";
    public double RefinementTimeoutSeconds { get; set; } = 20;

    [JsonIgnore]
    public string WhisperLanguageCode => SpokenLanguage switch
    {
        SpokenLanguage.Russian => "ru",
        SpokenLanguage.English => "en",
        _ => "auto"
    };
}

public sealed record WhisperTranscript(string Text, string? DetectedLanguage);

public static class DictationTextPolicy
{
    public static bool HasUsableText(string? text) => !string.IsNullOrWhiteSpace(text);
}

public sealed record WhisperModelInfo(string Name, int ApproxMb, bool Multilingual)
{
    public string FileName => $"ggml-{Name}.bin";
    public Uri DownloadUri => new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}");
}

public static class WhisperModelCatalog
{
    public static IReadOnlyList<WhisperModelInfo> Models { get; } =
    [
        new("tiny", 78, true),
        new("tiny.en", 78, false),
        new("base", 148, true),
        new("base.en", 148, false),
        new("small", 488, true),
        new("small.en", 488, false),
        new("medium", 1530, true),
        new("large-v3", 3100, true),
        new("large-v3-turbo", 1620, true)
    ];

    public static WhisperModelInfo Get(string name) =>
        Models.FirstOrDefault(model => model.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown Whisper model: {name}", nameof(name));
}
