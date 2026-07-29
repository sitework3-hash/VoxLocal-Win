using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoxLocal.Win.Core;
using VoxLocal.Win.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;

namespace VoxLocal.Win.UI;

public sealed class SettingsWindow : Window
{
    private static readonly Brush AppBackground = new SolidColorBrush(Color.FromRgb(246, 248, 252));
    private static readonly Brush PanelBackground = Brushes.White;
    private static readonly Brush BorderColor = new SolidColorBrush(Color.FromRgb(220, 226, 236));
    private static readonly Brush AccentColor = new SolidColorBrush(Color.FromRgb(31, 104, 217));
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
        Width = 560;
        MinWidth = 500;
        MinHeight = 480;
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 16);
        Height = Math.Min(680, MaxHeight);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        Background = AppBackground;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(26, 20, 26, 18)
        };
        var headerContent = new StackPanel();
        headerContent.Children.Add(new TextBlock
        {
            Text = "VoxLocal для Windows",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 34, 54))
        });
        headerContent.Children.Add(new TextBlock
        {
            Text = "Локальная диктовка без отправки речи в облако",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 103, 121)),
            Margin = new Thickness(0, 5, 0, 0)
        });
        header.Child = headerContent;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel { Margin = new Thickness(26, 18, 26, 18) };
        scroll.Content = content;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var hotkey = AddSection(content, "Горячая клавиша");
        _exactAltSpace = AddCheck(
            hotkey,
            "Использовать Alt + Space (аналог Option + Space)",
            store.Current.UseExactAltSpace);
        hotkey.Children.Add(new TextBlock
        {
            Text = "Если выключить, будет использоваться Ctrl + Alt + Space. Alt + Space заменяет системное меню окна.",
            Foreground = new SolidColorBrush(Color.FromRgb(91, 103, 121)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, -1, 0, 8)
        });
        _mode = AddCombo(hotkey, "Режим", Enum.GetValues<HotkeyMode>().Cast<object>());
        _mode.SelectedItem = store.Current.HotkeyMode;

        var recognition = AddSection(content, "Распознавание речи");
        _engine = AddCombo(recognition, "Движок", ["Whisper — точнее", "Sherpa-ONNX — быстрее (русский)"]);
        _engine.SelectedIndex = store.Current.RecognitionEngine == RecognitionEngine.SherpaTOneRussian ? 1 : 0;
        _language = AddCombo(recognition, "Язык речи", Enum.GetValues<SpokenLanguage>().Cast<object>());
        _language.SelectedItem = store.Current.SpokenLanguage;
        _model = AddCombo(recognition, "Модель Whisper", WhisperModelCatalog.Models.Select(model => model.Name).Cast<object>());
        _model.SelectedItem = store.Current.WhisperModel;
        _modelStatus = new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 5),
            FontSize = 12
        };
        recognition.Children.Add(_modelStatus);
        var download = CreateButton("Скачать выбранную модель");
        download.HorizontalAlignment = HorizontalAlignment.Left;
        download.Click += DownloadModel;
        recognition.Children.Add(download);
        _downloadProgress = new ProgressBar
        {
            Height = 5,
            Minimum = 0,
            Maximum = 1,
            Foreground = AccentColor,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };
        recognition.Children.Add(_downloadProgress);

        var output = AddSection(content, "Вставка и обработка текста");
        _clipboardOnly = AddCheck(
            output, "Только копировать текст в буфер", store.Current.InsertionMode == InsertionMode.ClipboardOnly);
        _refinement = AddCheck(output, "Refinement через локальную Ollama", store.Current.RefinementEnabled);
        _ollamaModel = AddText(output, "Модель Ollama", store.Current.OllamaModel);

        var footer = new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(26, 14, 26, 16)
        };
        var buttons = new DockPanel { LastChildFill = false };
        var microphone = CreateButton("Доступ к микрофону");
        microphone.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone")
        {
            UseShellExecute = true
        });
        DockPanel.SetDock(microphone, Dock.Left);
        buttons.Children.Add(microphone);
        var save = CreateButton("Сохранить", primary: true);
        save.IsDefault = true;
        save.Click += (_, _) => SaveAndClose();
        DockPanel.SetDock(save, Dock.Right);
        buttons.Children.Add(save);
        footer.Child = buttons;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

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
        var installed = _models.IsInstalled(name);
        _modelStatus.Text = installed ? "✓ Модель установлена" : "Модель не установлена";
        _modelStatus.Foreground = installed
            ? new SolidColorBrush(Color.FromRgb(34, 130, 84))
            : new SolidColorBrush(Color.FromRgb(180, 73, 58));
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

    private static StackPanel AddSection(Panel parent, string title)
    {
        var body = new StackPanel();
        var card = new Border
        {
            Background = PanelBackground,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 13, 16, 15),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var cardContent = new StackPanel();
        cardContent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(43, 55, 75)),
            Margin = new Thickness(0, 0, 0, 7)
        });
        cardContent.Children.Add(body);
        card.Child = cardContent;
        parent.Children.Add(card);
        return body;
    }

    private static ComboBox AddCombo(Panel parent, string title, IEnumerable<object> items)
    {
        parent.Children.Add(FieldLabel(title));
        var combo = new ComboBox
        {
            ItemsSource = items,
            MinHeight = 32,
            Padding = new Thickness(8, 3, 8, 3),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(183, 194, 210))
        };
        parent.Children.Add(combo);
        return combo;
    }

    private static CheckBox AddCheck(Panel parent, string title, bool value)
    {
        var check = new CheckBox
        {
            Content = title,
            IsChecked = value,
            Margin = new Thickness(0, 4, 0, 8),
            FontSize = 13
        };
        parent.Children.Add(check);
        return check;
    }

    private static TextBox AddText(Panel parent, string title, string value)
    {
        parent.Children.Add(FieldLabel(title));
        var text = new TextBox
        {
            Text = value,
            MinHeight = 32,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(183, 194, 210))
        };
        parent.Children.Add(text);
        return text;
    }

    private static TextBlock FieldLabel(string title) => new()
    {
        Text = title,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(77, 90, 109)),
        Margin = new Thickness(0, 8, 0, 4)
    };

    private static Button CreateButton(string title, bool primary = false) => new()
    {
        Content = title,
        Padding = new Thickness(14, 7, 14, 7),
        FontSize = 13,
        FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        Background = primary ? AccentColor : Brushes.White,
        Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(44, 57, 77)),
        BorderBrush = primary ? AccentColor : new SolidColorBrush(Color.FromRgb(183, 194, 210)),
        Margin = new Thickness(0, 0, primary ? 0 : 8, 0)
    };
}
