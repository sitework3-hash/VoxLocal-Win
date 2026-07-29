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
        Run("model catalog", TestModelCatalog);
        Run("transcription history", TestTranscriptionHistory);
        Run("Ollama loopback protection", TestLoopbackProtection);
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
        False(RefinementSafeguard.TryAccept("hello world", "As an AI, I cannot help", out _));
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
