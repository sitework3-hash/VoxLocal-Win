using VoxLocal.Win.Core;
using VoxLocal.Win.Services;

namespace VoxLocal.Win.App;

public sealed class DictationController : IDisposable
{
    private readonly DictationStateMachine _machine = new();
    private readonly SettingsStore _settings;
    private readonly TranscriptionHistoryStore _history;
    private readonly AudioRecorder _recorder;
    private readonly ModelManager _models;
    private readonly WhisperTranscriber _transcriber;
    private readonly SherpaTOneTranscriber _sherpa;
    private readonly OllamaRefiner _refiner;
    private readonly TextInserter _inserter;
    private readonly GlobalHotkeyService _hotkeys;
    private CancellationTokenSource? _pipelineCancellation;
    private nint _targetWindow;

    public DictationState State => _machine.State;
    public string StatusMessage { get; private set; } = "Готово";
    public event Action<DictationState, string>? StateChanged;
    public event Action<float>? LevelChanged;

    public DictationController(
        SettingsStore settings,
        TranscriptionHistoryStore history,
        AudioRecorder recorder,
        ModelManager models,
        WhisperTranscriber transcriber,
        SherpaTOneTranscriber sherpa,
        OllamaRefiner refiner,
        TextInserter inserter,
        GlobalHotkeyService hotkeys)
    {
        _settings = settings;
        _history = history;
        _recorder = recorder;
        _models = models;
        _transcriber = transcriber;
        _sherpa = sherpa;
        _refiner = refiner;
        _inserter = inserter;
        _hotkeys = hotkeys;
        _recorder.LevelChanged += level => LevelChanged?.Invoke(level);
        _hotkeys.MainKeyDown += OnMainKeyDown;
        _hotkeys.MainKeyUp += OnMainKeyUp;
        _hotkeys.EscapePressed += Cancel;
    }

    private void OnMainKeyDown()
    {
        if (_settings.Current.HotkeyMode == HotkeyMode.PressAndHold)
            Start();
        else if (State == DictationState.Idle)
            Start();
        else if (State == DictationState.Recording)
            _ = StopAndProcessAsync();
    }

    private void OnMainKeyUp()
    {
        if (_settings.Current.HotkeyMode == HotkeyMode.PressAndHold &&
            State == DictationState.Recording)
            _ = StopAndProcessAsync();
    }

    public void Start()
    {
        if (State != DictationState.Idle)
            return;
        try
        {
            Transition(DictationState.Preparing, "Подготовка…");
            if (_settings.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian)
            {
                if (!SherpaTOneTranscriber.IsInstalled())
                    throw new FileNotFoundException(
                        "Русская быстрая модель Sherpa-ONNX не установлена. Выполните scripts/windows/download_sherpa_model.ps1.");
            }
            else if (!_models.IsInstalled(_settings.Current.WhisperModel))
            {
                throw new FileNotFoundException(
                    $"Модель {_settings.Current.WhisperModel} не установлена. Откройте настройки VoxLocal.");
            }

            _targetWindow = TextInserter.CaptureForegroundWindow();
            _pipelineCancellation = new CancellationTokenSource();
            _recorder.Start();
            _hotkeys.CaptureEscape = true;
            Transition(DictationState.Recording, "Слушаю…");
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    public async Task StopAndProcessAsync()
    {
        if (State != DictationState.Recording)
            return;
        Transition(DictationState.Stopping, "Завершаю запись…");
        var cancellationToken = _pipelineCancellation?.Token ?? CancellationToken.None;
        string? audioPath = null;
        try
        {
            // Let the capture device flush its final 20 ms buffers. Users can
            // release Alt+Space immediately after the final word.
            await Task.Delay(180, cancellationToken);
            var recording = await _recorder.StopAsync();
            audioPath = recording.FilePath;
            cancellationToken.ThrowIfCancellationRequested();

            Transition(DictationState.Transcribing, "Распознаю…");
            WhisperTranscript transcript;
            if (_settings.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian)
            {
                transcript = await _sherpa.TranscribeAsync(
                    recording.FilePath,
                    _settings.Current.SherpaThreads,
                    cancellationToken);
            }
            else
            {
                var modelPath = _models.PathFor(_settings.Current.WhisperModel);
                transcript = await _transcriber.TranscribeAsync(
                    recording.FilePath,
                    recording.Duration,
                    modelPath,
                    _settings.Current.WhisperLanguageCode,
                    _settings.Current.WhisperThreads,
                    _settings.Current.RemoveArtifacts,
                    cancellationToken);
            }

            var text = transcript.Text;
            if (_settings.Current.RefinementEnabled &&
                _settings.Current.RefinementPreset != RefinementPreset.RawTranscript)
            {
                Transition(DictationState.Refining, "Уточняю текст…");
                try
                {
                    text = await _refiner.RefineAsync(
                        text, transcript.DetectedLanguage, _settings.Current, cancellationToken);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    AppLog.Shared.Info($"Refinement skipped: {error.Message}");
                }
            }

            _history.Add(text);

            Transition(DictationState.Inserting, "Вставляю…");
            var outcome = await _inserter.InsertAsync(
                text, _targetWindow, _settings.Current.InsertionMode, cancellationToken);
            var message = outcome switch
            {
                InsertionOutcome.Pasted => "Текст отправлен — буфер вернётся через 5 с",
                InsertionOutcome.SecureField => "Поле защищено — текст скопирован",
                _ => "Текст скопирован — вставьте Ctrl+V"
            };
            Transition(DictationState.Completed, message);
            await ResetAfterDelayAsync();
        }
        catch (OperationCanceledException)
        {
            if (State is not DictationState.Cancelled and not DictationState.Idle)
                Transition(DictationState.Cancelled, "Отменено");
            await ResetAfterDelayAsync();
        }
        catch (Exception error)
        {
            Fail(error);
        }
        finally
        {
            _hotkeys.CaptureEscape = false;
            if (audioPath is not null)
            {
                try { File.Delete(audioPath); } catch { }
            }
        }
    }

    public void Cancel()
    {
        if (!_machine.IsCancellable)
            return;
        _pipelineCancellation?.Cancel();
        _recorder.Cancel();
        _hotkeys.CaptureEscape = false;
        if (State != DictationState.Cancelled && _machine.CanTransition(DictationState.Cancelled))
            Transition(DictationState.Cancelled, "Отменено");
        _ = ResetAfterDelayAsync();
    }

    private void Fail(Exception error)
    {
        AppLog.Shared.Error(error.ToString());
        _recorder.Cancel();
        _hotkeys.CaptureEscape = false;
        if (_machine.CanTransition(DictationState.Error))
            Transition(DictationState.Error, error.Message);
        _ = ResetAfterDelayAsync();
    }

    private async Task ResetAfterDelayAsync()
    {
        await Task.Delay(1200);
        if (_machine.CanTransition(DictationState.Idle))
            Transition(DictationState.Idle, "Готово");
    }

    private void Transition(DictationState next, string message)
    {
        _machine.Transition(next);
        StatusMessage = message;
        StateChanged?.Invoke(next, message);
        AppLog.Shared.Info($"State: {next} ({message})");
    }

    public void Dispose()
    {
        _pipelineCancellation?.Cancel();
        _pipelineCancellation?.Dispose();
        _recorder.Dispose();
    }
}
