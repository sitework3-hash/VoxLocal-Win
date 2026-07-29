using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using VoxLocal.Win.App;
using VoxLocal.Win.Core;
using VoxLocal.Win.Services;

namespace VoxLocal.Win.UI;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly DictationController _dictation;
    private readonly SettingsStore _settings;
    private readonly ModelManager _models;
    private readonly Dispatcher _dispatcher;
    private readonly Action<DictationState, string> _stateHandler;
    private SettingsWindow? _settingsWindow;

    public TrayController(
        DictationController dictation,
        SettingsStore settings,
        ModelManager models,
        Dispatcher dispatcher)
    {
        _dictation = dictation;
        _settings = settings;
        _models = models;
        _dispatcher = dispatcher;
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "VoxLocal — готово",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowSettings();
        _stateHandler = (state, message) =>
        {
            if (_dispatcher.CheckAccess())
                OnStateChanged(state, message);
            else
                _dispatcher.BeginInvoke(() => OnStateChanged(state, message));
        };
        _dictation.StateChanged += _stateHandler;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Начать диктовку", null, (_, _) => _dictation.Start());
        menu.Items.Add("Остановить и распознать", null, async (_, _) => await _dictation.StopAndProcessAsync());
        menu.Items.Add("Отменить (Esc)", null, (_, _) => _dictation.Cancel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        menu.Items.Add("Папка моделей", null, (_, _) => OpenFolder(AppPaths.ModelsDirectory));
        menu.Items.Add("Журнал", null, (_, _) => OpenFolder(AppPaths.LogsDirectory));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void OnStateChanged(DictationState state, string message)
    {
        _icon.Text = $"VoxLocal — {message}"[..Math.Min(63, $"VoxLocal — {message}".Length)];
        if (state == DictationState.Error)
        {
            _icon.BalloonTipTitle = "VoxLocal";
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = ToolTipIcon.Error;
            _icon.ShowBalloonTip(5000);
        }
    }

    public void ShowFirstRunIfNeeded()
    {
        var activeEngineInstalled = _settings.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian
            ? SherpaTOneTranscriber.IsInstalled()
            : _models.IsInstalled(_settings.Current.WhisperModel);
        if (activeEngineInstalled)
            return;
        _icon.BalloonTipTitle = "VoxLocal готов";
        _icon.BalloonTipText = "Скачайте модель Whisper в настройках, затем удерживайте Alt + Space.";
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(8000);
        ShowSettings();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _models);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    public void Dispose()
    {
        _dictation.StateChanged -= _stateHandler;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
