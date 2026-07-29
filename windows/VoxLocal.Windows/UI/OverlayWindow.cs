using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VoxLocal.Win.Core;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace VoxLocal.Win.UI;

public sealed class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly TextBlock _status;
    private readonly ProgressBar _level;

    public OverlayWindow()
    {
        Width = 320;
        Height = 82;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;

        var border = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 75, 82)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 12, 18, 12)
        };
        var panel = new StackPanel();
        _status = new TextBlock
        {
            Text = "Готово",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _level = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Height = 6,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 130)),
            Background = new SolidColorBrush(Color.FromRgb(55, 55, 60)),
            BorderThickness = new Thickness(0)
        };
        panel.Children.Add(_status);
        panel.Children.Add(_level);
        border.Child = panel;
        Content = border;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        };
    }

    public void SetState(DictationState state, string message)
    {
        _status.Text = state == DictationState.Recording ? $"●  {message}   Esc — отмена" : message;
        _level.Visibility = state == DictationState.Recording
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (state == DictationState.Idle)
        {
            Hide();
            return;
        }
        Position();
        if (!IsVisible)
            Show();
    }

    public void SetLevel(float level) => _level.Value = Math.Clamp(level * 2.5, 0, 1);

    private void Position()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 42;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint window, int index, int value);
}
