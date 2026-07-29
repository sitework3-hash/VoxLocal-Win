using System.Threading;
using System.Windows;
using VoxLocal.Win.App;
using VoxLocal.Win.Core;
using VoxLocal.Win.Services;
using VoxLocal.Win.UI;

namespace VoxLocal.Win;

public static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    public static int Main()
    {
        AppPaths.EnsureDirectories();
        _singleInstance = new Mutex(true, "VoxLocal.Windows.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("VoxLocal уже запущен.", "VoxLocal");
            return 0;
        }

        var application = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        application.DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Shared.Error(args.Exception.ToString());
            MessageBox.Show(args.Exception.Message, "VoxLocal", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = new SettingsStore();
        var history = new TranscriptionHistoryStore();
        var models = new ModelManager();
        using var hotkeys = new GlobalHotkeyService(application.Dispatcher, settings);
        using var recorder = new AudioRecorder();
        var transcriber = new WhisperTranscriber();
        using var sherpa = new SherpaTOneTranscriber();
        var refiner = new OllamaRefiner();
        var inserter = new TextInserter(application.Dispatcher);
        using var dictation = new DictationController(
            settings, history, recorder, models, transcriber, sherpa, refiner, inserter, hotkeys);
        var overlay = new OverlayWindow();
        using var tray = new TrayController(dictation, settings, history, models, application.Dispatcher);

        // NAudio delivers level samples from its capture thread. Every visual
        // update must cross back to the WPF dispatcher before touching a Window.
        dictation.StateChanged += (state, message) => application.Dispatcher.BeginInvoke(
            () => overlay.SetState(state, message));
        dictation.LevelChanged += level => application.Dispatcher.BeginInvoke(
            () => overlay.SetLevel(level));
        application.Exit += (_, _) =>
        {
            overlay.Close();
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        };

        try
        {
            hotkeys.Start();
            application.Dispatcher.BeginInvoke(tray.ShowFirstRunIfNeeded);
            if (settings.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian)
            {
                _ = sherpa.WarmUpAsync(settings.Current.SherpaThreads);
            }
            AppLog.Shared.Info("VoxLocal Windows started");
            application.Run();
            return 0;
        }
        catch (Exception error)
        {
            AppLog.Shared.Error(error.ToString());
            MessageBox.Show(error.Message, "VoxLocal", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
