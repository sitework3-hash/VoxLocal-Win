using System.Diagnostics;
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
    private readonly CloudRefiner _refiner;
    private readonly TextInserter _inserter;
    private readonly GlobalHotkeyService _hotkeys;
    private CancellationTokenSource? _pipelineCancellation;
    private SherpaTOneTranscriber.LiveSession? _liveTranscription;
    private nint _targetWindow;

    public DictationState State => _machine.State;
    public string StatusMessage { get; private set; } = "Готово";
    public event Action<DictationState, string>? StateChanged;
    public event Action<float>? LevelChanged;
    public event Action<bool>? PreviewAvailabilityChanged;
    public event Action<string>? PreviewChanged;

    public DictationController(
        SettingsStore settings,
        TranscriptionHistoryStore history,
        AudioRecorder recorder,
        ModelManager models,
        WhisperTranscriber transcriber,
        SherpaTOneTranscriber sherpa,
        CloudRefiner refiner,
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
        {
            AppLog.Shared.Info($"Dictation start ignored: state={State}");
            return;
        }
        try
        {
            AppLog.Shared.Info($"Dictation start: engine={_settings.Current.RecognitionEngine}, " +
                               $"hotkeyMode={_settings.Current.HotkeyMode}, " +
                               $"insertionMode={_settings.Current.InsertionMode}, " +
                               $"refinement={_settings.Current.RefinementEnabled}");
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
            AppLog.Shared.Info($"Target window captured: handle=0x{_targetWindow.ToInt64():X}, " +
                               TextInserter.DescribeCapturedWindow(_targetWindow));
            _pipelineCancellation = new CancellationTokenSource();
            var livePreviewAvailable = _settings.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian;
            PreviewAvailabilityChanged?.Invoke(livePreviewAvailable);
            PreviewChanged?.Invoke("");
            if (livePreviewAvailable)
            {
                _liveTranscription = _sherpa.StartLiveSession(
                    _settings.Current.SherpaThreads,
                    text => PreviewChanged?.Invoke(text),
                    _pipelineCancellation.Token);
                _recorder.Pcm16DataAvailable += _liveTranscription.AcceptPcm16;
                AppLog.Shared.Info("Live transcription started");
            }
            _recorder.Start();
            AppLog.Shared.Info("Audio recording started");
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
        {
            AppLog.Shared.Info($"Dictation stop ignored: state={State}");
            return;
        }
        var pipelineTimer = Stopwatch.StartNew();
        AppLog.Shared.Info("Dictation pipeline started");
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
            AppLog.Shared.Info($"Audio recording stopped: durationMs={recording.Duration.TotalMilliseconds:F0}, " +
                               $"fileBytes={new FileInfo(recording.FilePath).Length}");
            cancellationToken.ThrowIfCancellationRequested();

            Transition(DictationState.Transcribing, "Завершаю распознавание…");
            var transcriptionTimer = Stopwatch.StartNew();
            WhisperTranscript transcript;
            if (_liveTranscription is not null)
            {
                _recorder.Pcm16DataAvailable -= _liveTranscription.AcceptPcm16;
                transcript = await _liveTranscription.CompleteAsync();
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

            transcriptionTimer.Stop();
            AppLog.Shared.Info($"Transcription completed: elapsedMs={transcriptionTimer.ElapsedMilliseconds}, " +
                               $"textLength={transcript.Text.Length}, detectedLanguage={transcript.DetectedLanguage ?? "unknown"}");

            var text = transcript.Text;
            var refinementFallback = false;
            if (!DictationTextPolicy.HasUsableText(text))
            {
                AppLog.Shared.Info("Transcription produced empty text; refinement and insertion skipped");
                Transition(DictationState.Completed, "Ничего не распознано");
                await ResetAfterDelayAsync();
                return;
            }
            if (DictationTextPolicy.ShouldRefine(_settings.Current))
            {
                Transition(DictationState.Refining, "Уточняю текст…");
                var refinementTimer = Stopwatch.StartNew();
                try
                {
                    text = await _refiner.RefineAsync(
                        text, transcript.DetectedLanguage, _settings.Current, cancellationToken);
                    refinementTimer.Stop();
                    AppLog.Shared.Info($"Refinement completed: elapsedMs={refinementTimer.ElapsedMilliseconds}, " +
                                       $"inputLength={transcript.Text.Length}, outputLength={text.Length}, " +
                                       $"preset={_settings.Current.RefinementPreset}");
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    refinementTimer.Stop();
                    refinementFallback = true;
                    AppLog.Shared.Info($"Refinement skipped: elapsedMs={refinementTimer.ElapsedMilliseconds}, " +
                                       $"reason={error.Message}");
                }
            }

            _history.Add(text);
            AppLog.Shared.Info($"Transcript stored in history: textLength={text.Length}");

            Transition(DictationState.Inserting, "Вставляю…");
            var insertionTimer = Stopwatch.StartNew();
            var outcome = await _inserter.InsertAsync(
                text, _targetWindow, _settings.Current.InsertionMode, cancellationToken);
            insertionTimer.Stop();
            AppLog.Shared.Info($"Insertion completed: elapsedMs={insertionTimer.ElapsedMilliseconds}, outcome={outcome}, " +
                               $"textLength={text.Length}");
            var message = outcome switch
            {
                InsertionOutcome.Pasted when refinementFallback => "Текст вставлен — обработка недоступна, использован исходный текст",
                InsertionOutcome.Pasted => "Текст отправлен — буфер вернётся через 5 с",
                InsertionOutcome.SecureField when refinementFallback => "Поле защищено — скопирован исходный текст",
                InsertionOutcome.SecureField => "Поле защищено — текст скопирован",
                _ when refinementFallback => "Исходный текст скопирован — вставьте Ctrl+V",
                _ => "Текст скопирован — вставьте Ctrl+V"
            };
            Transition(DictationState.Completed, message);
            pipelineTimer.Stop();
            AppLog.Shared.Info($"Dictation pipeline completed: elapsedMs={pipelineTimer.ElapsedMilliseconds}, outcome={outcome}");
            await ResetAfterDelayAsync();
        }
        catch (OperationCanceledException)
        {
            pipelineTimer.Stop();
            AppLog.Shared.Info($"Dictation pipeline cancelled: elapsedMs={pipelineTimer.ElapsedMilliseconds}");
            if (State is not DictationState.Cancelled and not DictationState.Idle)
                Transition(DictationState.Cancelled, "Отменено");
            await ResetAfterDelayAsync();
        }
        catch (Exception error)
        {
            pipelineTimer.Stop();
            AppLog.Shared.Error($"Dictation pipeline failed: elapsedMs={pipelineTimer.ElapsedMilliseconds}, {error}");
            Fail(error, logError: false);
        }
        finally
        {
            _hotkeys.CaptureEscape = false;
            StopLiveTranscription(cancel: false);
            if (audioPath is not null)
            {
                try
                {
                    File.Delete(audioPath);
                    AppLog.Shared.Info("Temporary audio deleted");
                }
                catch (Exception error)
                {
                    AppLog.Shared.Info($"Temporary audio deletion skipped: {error.Message}");
                }
            }
        }
    }

    public void Cancel()
    {
        if (!_machine.IsCancellable)
            return;
        _pipelineCancellation?.Cancel();
        StopLiveTranscription(cancel: true);
        _recorder.Cancel();
        _hotkeys.CaptureEscape = false;
        if (State != DictationState.Cancelled && _machine.CanTransition(DictationState.Cancelled))
            Transition(DictationState.Cancelled, "Отменено");
        _ = ResetAfterDelayAsync();
    }

    private void Fail(Exception error, bool logError = true)
    {
        if (logError)
            AppLog.Shared.Error(error.ToString());
        StopLiveTranscription(cancel: true);
        _recorder.Cancel();
        _hotkeys.CaptureEscape = false;
        if (_machine.CanTransition(DictationState.Error))
            Transition(DictationState.Error, error.Message);
        _ = ResetAfterDelayAsync();
    }

    private void StopLiveTranscription(bool cancel)
    {
        var session = _liveTranscription;
        if (session is null)
            return;

        _recorder.Pcm16DataAvailable -= session.AcceptPcm16;
        if (cancel)
            session.Cancel();
        session.Dispose();
        _liveTranscription = null;
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
        StopLiveTranscription(cancel: true);
        _pipelineCancellation?.Dispose();
        _recorder.Dispose();
    }
}
