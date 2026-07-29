using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using VoxLocal.Win.Core;
using VoxLocal.Win.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;

namespace VoxLocal.Win.UI;

public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly ModelManager _models;
    private readonly ComboBox _model;
    private readonly ComboBox _language;
    private readonly ComboBox _mode;
    private readonly ComboBox _engine;
    private readonly CheckBox _exactAltSpace;
    private readonly CheckBox _clipboardOnly;
    private readonly CheckBox _refinement;
    private readonly TextBox _ollamaModel;
    private readonly ProgressBar _downloadProgress;
    private readonly TextBlock _modelStatus;

    public SettingsWindow(SettingsStore store, ModelManager models)
    {
        _store = store;
        _models = models;
        Title = "VoxLocal — настройки";
        Width = 520;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "VoxLocal для Windows",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _exactAltSpace = AddCheck(
            root,
            "Использовать Alt + Space (аналог Option + Space)",
            store.Current.UseExactAltSpace);
        root.Children.Add(new TextBlock
        {
            Text = "Если выключено: Ctrl + Alt + Space. Alt + Space заменяет системное меню окна.",
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 12)
        });

        _mode = AddCombo(root, "Режим горячей клавиши", Enum.GetValues<HotkeyMode>().Cast<object>());
        _mode.SelectedItem = store.Current.HotkeyMode;
        _engine = AddCombo(root, "Движок распознавания",
            ["Whisper — точнее", "Sherpa-ONNX — быстрее (русский)"]);
        _engine.SelectedIndex = store.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian ? 1 : 0;
        _model = AddCombo(root, "Модель Whisper", WhisperModelCatalog.Models.Select(model => model.Name).Cast<object>());
        _model.SelectedItem = store.Current.WhisperModel;
        _modelStatus = new TextBlock { Margin = new Thickness(0, 4, 0, 4) };
        root.Children.Add(_modelStatus);
        var download = new Button
        {
            Content = "Скачать выбранную модель",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        download.Click += DownloadModel;
        root.Children.Add(download);
        _downloadProgress = new ProgressBar
        {
            Height = 6,
            Minimum = 0,
            Maximum = 1,
            Margin = new Thickness(0, 8, 0, 12),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_downloadProgress);

        _language = AddCombo(root, "Язык речи", Enum.GetValues<SpokenLanguage>().Cast<object>());
        _language.SelectedItem = store.Current.SpokenLanguage;
        _clipboardOnly = AddCheck(
            root, "Только копировать текст в буфер", store.Current.InsertionMode == InsertionMode.ClipboardOnly);
        _refinement = AddCheck(root, "Refinement через локальную Ollama", store.Current.RefinementEnabled);
        _ollamaModel = AddText(root, "Модель Ollama", store.Current.OllamaModel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var microphone = new Button { Content = "Доступ к микрофону", Margin = new Thickness(0, 0, 8, 0) };
        microphone.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone")
        {
            UseShellExecute = true
        });
        var save = new Button { Content = "Сохранить", IsDefault = true, Padding = new Thickness(18, 7, 18, 7) };
        save.Click += (_, _) => SaveAndClose();
        buttons.Children.Add(microphone);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        Content = root;
        UpdateModelStatus();
    }

    private async void DownloadModel(object sender, RoutedEventArgs args)
    {
        var name = _model.SelectedItem?.ToString() ?? "base";
        var info = WhisperModelCatalog.Get(name);
        if (MessageBox.Show(
                $"Скачать модель {name} (~{info.ApproxMb} МБ)?",
                "VoxLocal", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            _downloadProgress.Visibility = Visibility.Visible;
            var progress = new Progress<double>(value => _downloadProgress.Value = value);
            await _models.DownloadAsync(name, progress);
            UpdateModelStatus();
            MessageBox.Show("Модель установлена.", "VoxLocal");
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _downloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateModelStatus()
    {
        var name = _model.SelectedItem?.ToString() ?? "base";
        _modelStatus.Text = _models.IsInstalled(name) ? "✓ Модель установлена" : "Модель не установлена";
    }

    private void SaveAndClose()
    {
        _store.Current.UseExactAltSpace = _exactAltSpace.IsChecked == true;
        _store.Current.HotkeyMode = (HotkeyMode)(_mode.SelectedItem ?? HotkeyMode.PressAndHold);
        _store.Current.RecognitionEngine = _engine.SelectedIndex == 1
            ? RecognitionEngine.SherpaTOneRussian
            : RecognitionEngine.Whisper;
        _store.Current.WhisperModel = _model.SelectedItem?.ToString() ?? "base";
        _store.Current.SpokenLanguage = (SpokenLanguage)(_language.SelectedItem ?? SpokenLanguage.Auto);
        _store.Current.InsertionMode = _clipboardOnly.IsChecked == true
            ? InsertionMode.ClipboardOnly
            : InsertionMode.Automatic;
        _store.Current.RefinementEnabled = _refinement.IsChecked == true;
        _store.Current.OllamaModel = _ollamaModel.Text.Trim();
        _store.Save();
        Close();
    }

    private static ComboBox AddCombo(StackPanel root, string title, IEnumerable<object> items)
    {
        root.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 7, 0, 3) });
        var combo = new ComboBox { ItemsSource = items, MinHeight = 28 };
        root.Children.Add(combo);
        return combo;
    }

    private static CheckBox AddCheck(StackPanel root, string title, bool value)
    {
        var check = new CheckBox
        {
            Content = title,
            IsChecked = value,
            Margin = new Thickness(0, 8, 0, 4)
        };
        root.Children.Add(check);
        return check;
    }

    private static TextBox AddText(StackPanel root, string title, string value)
    {
        root.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 7, 0, 3) });
        var text = new TextBox { Text = value, MinHeight = 28 };
        root.Children.Add(text);
        return text;
    }
}
