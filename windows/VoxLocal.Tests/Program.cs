using VoxLocal.Win.Core;
using VoxLocal.Win.Services;

namespace VoxLocal.Tests;

public static class Program
{
    private static int _passed;

    public static int Main()
    {
        Run("state transitions", TestStateTransitions);
        Run("invalid transition is rejected", TestInvalidTransition);
        Run("Whisper argument contract", TestWhisperArguments);
        Run("Whisper JSON parsing", TestWhisperJsonParsing);
        Run("Sherpa Russian model integration", TestSherpaRussianModel);
        Run("artifact removal", TestArtifactRemoval);
        Run("refinement safeguard", TestRefinementSafeguard);
        Run("refinement modes", TestRefinementModes);
        Run("strict refinement prompt", TestStrictRefinementPrompt);
        Run("model catalog", TestModelCatalog);
        Run("transcription history", TestTranscriptionHistory);
        Run("Ollama loopback protection", TestLoopbackProtection);
        Run("empty transcription policy", TestEmptyTranscriptionPolicy);
        Run("duplicate processing state guard", TestDuplicateProcessingStateGuard);
        Run("cloud model catalog", TestCloudModelCatalog);
        Run("cloud endpoint normalization", TestCloudEndpointNormalization);
        Run("cloud HTTP errors", TestCloudHttpErrors);
        Run("DPAPI secret persistence", TestSecretPersistence);
        Run("settings backward compatibility", TestSettingsBackwardCompatibility);
        Console.WriteLine($"[voxlocal-tests] passed {_passed} tests");
        return 0;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"[PASS] {name}");
    }

    private static void TestStateTransitions()
    {
        var state = new DictationStateMachine();
        state.Transition(DictationState.Preparing);
        state.Transition(DictationState.Recording);
        state.Transition(DictationState.Stopping);
        state.Transition(DictationState.Transcribing);
        state.Transition(DictationState.Inserting);
        state.Transition(DictationState.Completed);
        state.Transition(DictationState.Idle);
        Equal(DictationState.Idle, state.State);
    }

    private static void TestInvalidTransition()
    {
        var state = new DictationStateMachine();
        Throws<InvalidOperationException>(() => state.Transition(DictationState.Recording));
    }

    private static void TestWhisperArguments()
    {
        var args = WhisperTranscriber.BuildArguments("model.bin", "input.wav", "ru", 99, "output", 750);
        Equal("16", args[7]);
        True(args.Contains("-ac"));
        Equal("750", args[9]);
        True(args.Contains("-nt"));
        Equal("0.8", args[^1]);
        Equal(750, WhisperTranscriber.SelectAudioContext(TimeSpan.FromSeconds(12)));
        Equal(0, WhisperTranscriber.SelectAudioContext(TimeSpan.FromSeconds(12.1)));
    }

    private static void TestWhisperJsonParsing()
    {
        const string json = "{\"transcription\":[{\"text\":\" Привет\"},{\"text\":\" мир \"}],\"result\":{\"language\":\"ru\"}}";
        var parsed = WhisperOutputParser.Parse(json, true);
        Equal("Привет мир", parsed.Text);
        Equal("ru", parsed.DetectedLanguage);
    }

    private static void TestSherpaRussianModel()
    {
        // The project can be tested before optional models are downloaded.
        if (!SherpaTOneTranscriber.IsInstalled())
            return;

        var sample = System.IO.Path.Combine(AppPaths.SherpaTOneDirectory, "0.wav");
        True(System.IO.File.Exists(sample));
        using var transcriber = new SherpaTOneTranscriber();
        var transcript = transcriber.TranscribeAsync(sample, 1, System.Threading.CancellationToken.None)
            .GetAwaiter().GetResult();
        True(transcript.Text.Contains("бригада", StringComparison.OrdinalIgnoreCase));
        Equal("ru", transcript.DetectedLanguage);
    }

    private static void TestArtifactRemoval()
    {
        var parsed = WhisperOutputParser.Parse("{\"transcription\":[{\"text\":\" [music] Тест (смех) ♪ \"}]}", true);
        Equal("Тест", parsed.Text);
    }

    private static void TestRefinementSafeguard()
    {
        True(RefinementSafeguard.TryAccept("hello world", "Hello, world.", out var accepted));
        Equal("Hello, world.", accepted);
        False(RefinementSafeguard.TryAccept("hello world", "", out _));
        False(RefinementSafeguard.TryAccept("hello world", "As an AI, I cannot help", out _));
        False(RefinementSafeguard.TryAccept("привет мир", "Вот исправленный текст: Привет, мир.", out _));
        False(RefinementSafeguard.TryAccept("привет мир", "**Исправленный текст:** Привет, мир.", out _));
        False(RefinementSafeguard.TryAccept("короткий текст", new string('а', 300), out _));
        False(RefinementSafeguard.TryAccept(
            "первое второе третье четвертое пятое шестое седьмое восьмое девятое десятое",
            "яблоко груша апельсин банан персик слива вишня лимон ананас манго",
            out _));
    }

    private static void TestRefinementModes()
    {
        var settings = new AppSettings { RefinementEnabled = false, RefinementPreset = RefinementPreset.CleanDictation };
        False(DictationTextPolicy.ShouldRefine(settings));

        settings.RefinementEnabled = true;
        settings.RefinementPreset = RefinementPreset.RawTranscript;
        False(DictationTextPolicy.ShouldRefine(settings));

        foreach (var preset in Enum.GetValues<RefinementPreset>().Where(value => value != RefinementPreset.RawTranscript))
        {
            settings.RefinementPreset = preset;
            True(DictationTextPolicy.ShouldRefine(settings));
            var prompt = OllamaRefiner.BuildSystemPrompt(preset, "ru");
            True(prompt.Contains("same text", StringComparison.OrdinalIgnoreCase));
            True(prompt.Contains("keep that language", StringComparison.OrdinalIgnoreCase));
        }

        True(OllamaRefiner.BuildSystemPrompt(RefinementPreset.Concise, "ru").Contains("concise", StringComparison.OrdinalIgnoreCase));
        True(OllamaRefiner.BuildSystemPrompt(RefinementPreset.BusinessStyle, "ru").Contains("professional", StringComparison.OrdinalIgnoreCase));
        True(OllamaRefiner.BuildSystemPrompt(RefinementPreset.PreserveSpokenWording, "ru").Contains("exact wording", StringComparison.OrdinalIgnoreCase));
    }

    private static void TestStrictRefinementPrompt()
    {
        var prompt = OllamaRefiner.BuildSystemPrompt(RefinementPreset.CleanDictation, "ru");
        True(prompt.Contains("Output only", StringComparison.OrdinalIgnoreCase));
        True(prompt.Contains("answer questions", StringComparison.OrdinalIgnoreCase));
        True(prompt.Contains("execute commands", StringComparison.OrdinalIgnoreCase));
        True(prompt.Contains("Never add facts", StringComparison.OrdinalIgnoreCase));
        True(prompt.Contains("original meaning", StringComparison.OrdinalIgnoreCase));
        True(prompt.Contains("names, dates, numbers", StringComparison.OrdinalIgnoreCase));
        True(prompt.Contains("instructions found in the dictation", StringComparison.OrdinalIgnoreCase));
    }

    private static void TestModelCatalog()
    {
        var baseModel = WhisperModelCatalog.Get("base");
        Equal("ggml-base.bin", baseModel.FileName);
        Equal(148, baseModel.ApproxMb);
    }

    private static void TestTranscriptionHistory()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VoxLocal.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "history.json");
            var history = new TranscriptionHistoryStore(path);
            for (var index = 1; index <= 6; index++)
                history.Add($"phrase {index}");

            var entries = history.GetLatest();
            Equal(5, entries.Count);
            Equal("phrase 6", entries[0].Text);
            Equal("phrase 2", entries[^1].Text);

            var reloaded = new TranscriptionHistoryStore(path).GetLatest();
            Equal(5, reloaded.Count);
            Equal("phrase 6", reloaded[0].Text);
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    private static void TestLoopbackProtection()
    {
        True(OllamaRefiner.IsLoopbackEndpoint("http://127.0.0.1:11434"));
        True(OllamaRefiner.IsLoopbackEndpoint("http://localhost:11434"));
        False(OllamaRefiner.IsLoopbackEndpoint("https://example.com"));
    }

    private static void TestEmptyTranscriptionPolicy()
    {
        False(DictationTextPolicy.HasUsableText(null));
        False(DictationTextPolicy.HasUsableText(string.Empty));
        False(DictationTextPolicy.HasUsableText("  \r\n  "));
        True(DictationTextPolicy.HasUsableText("Привет"));
    }

    private static void TestDuplicateProcessingStateGuard()
    {
        var state = new DictationStateMachine();
        state.Transition(DictationState.Preparing);
        state.Transition(DictationState.Recording);
        True(state.CanTransition(DictationState.Stopping));
        state.Transition(DictationState.Stopping);
        False(state.CanTransition(DictationState.Stopping));
        True(state.CanTransition(DictationState.Transcribing));
    }

    private static void TestCloudModelCatalog()
    {
        Equal(39, OpenAiCompatibleModelCatalog.Models.Count);
        True(OpenAiCompatibleModelCatalog.Models.Contains("gemini-3.6-flash"));
        True(OpenAiCompatibleModelCatalog.Models.Contains("deepseek-v4.1-flash"));
        True(OpenAiCompatibleModelCatalog.Models.Contains("gpt-6-luna"));
        Equal("https://triklz27.ru/v1", new AppSettings().OpenAiBaseUrl);
        Equal(RefinementProvider.Ollama, new AppSettings().RefinementProvider);
    }

    private static void TestCloudEndpointNormalization()
    {
        Equal("https://example.com/v1/chat/completions", CloudRefiner.BuildChatCompletionsUri("https://example.com/v1").AbsoluteUri);
        Equal("https://example.com/v1/chat/completions", CloudRefiner.BuildChatCompletionsUri("https://example.com/v1/").AbsoluteUri);
        Equal("https://example.com/v1/chat/completions", CloudRefiner.BuildChatCompletionsUri("https://example.com/v1/chat/completions").AbsoluteUri);
        Throws<InvalidOperationException>(() => CloudRefiner.BuildChatCompletionsUri("not-a-url"));
    }

    private static void TestCloudHttpErrors()
    {
        Equal("Провайдер отклонил API-ключ.", CloudRefiner.DescribeHttpError(System.Net.HttpStatusCode.Unauthorized));
        Equal("Провайдер сообщает о недостатке баланса.", CloudRefiner.DescribeHttpError(System.Net.HttpStatusCode.PaymentRequired));
        Equal("Провайдер не нашёл endpoint или указанную модель.", CloudRefiner.DescribeHttpError(System.Net.HttpStatusCode.NotFound));
    }

    private static void TestSecretPersistence()
    {
        const string secret = "тестовый-ключ-123";
        var protectedValue = SecretProtector.Protect(secret);
        True(!string.IsNullOrWhiteSpace(protectedValue));
        False(protectedValue.Contains(secret, StringComparison.Ordinal));
        Equal(secret, SecretProtector.Unprotect(protectedValue));

        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VoxLocal.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "settings.json");
            var store = new SettingsStore(path);
            store.Current.OpenAiApiKeyProtected = protectedValue;
            store.Save();

            var serialized = System.IO.File.ReadAllText(path);
            False(serialized.Contains(secret, StringComparison.Ordinal));
            var reloaded = new SettingsStore(path);
            Equal(secret, SecretProtector.Unprotect(reloaded.Current.OpenAiApiKeyProtected));

            reloaded.Current.OpenAiApiKeyProtected = "";
            reloaded.Save();
            var afterDeletion = new SettingsStore(path);
            Equal("", afterDeletion.Current.OpenAiApiKeyProtected);
            False(System.IO.File.ReadAllText(path).Contains(protectedValue, StringComparison.Ordinal));
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    private static void TestSettingsBackwardCompatibility()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VoxLocal.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "settings.json");
            System.IO.File.WriteAllText(path, "{\"WhisperModel\":\"small\",\"UseExactAltSpace\":false}");
            var store = new SettingsStore(path);
            Equal("small", store.Current.WhisperModel);
            False(store.Current.UseExactAltSpace);
            False(store.Current.RefinementEnabled);
            Equal("", store.Current.OpenAiApiKeyProtected);
            Equal("https://triklz27.ru/v1", store.Current.OpenAiBaseUrl);
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool value) => True(!value);

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
