using System.Windows;
using System.Windows.Input;
using ZapretManager.Services;

namespace ZapretManager;

public partial class ThemedDialogWindow : Window
{
    public bool RememberChoice => RememberCheckBox.IsChecked == true;

    public ThemedDialogWindow(
        string title,
        string message,
        bool isError,
        DialogService.DialogButtons buttons,
        bool useLightTheme,
        string? rememberText = null,
        string? primaryButtonText = null,
        string? secondaryButtonText = null)
    {
        InitializeComponent();
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ApplyTheme(useLightTheme);
        SubtitleTextBlock.Text = buttons == DialogService.DialogButtons.YesNo
            ? "Требуется подтверждение"
            : isError
                ? "Обратите внимание"
                : "Результат операции";
        StatusGlyphTextBlock.Text = isError ? "!" : buttons == DialogService.DialogButtons.YesNo ? "?" : "i";
        PrimaryButton.Content = primaryButtonText ?? (buttons == DialogService.DialogButtons.YesNo ? "Да" : "Закрыть");
        SecondaryButton.Content = secondaryButtonText ?? "Нет";
        SecondaryButton.Visibility = buttons == DialogService.DialogButtons.YesNo ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Background = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 91, 91))
            : (System.Windows.Media.Brush)FindResource("DialogPrimaryBrush");
        PrimaryButton.BorderBrush = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 124, 124))
            : (System.Windows.Media.Brush)FindResource("DialogPrimaryBorderBrush");

        if (!string.IsNullOrWhiteSpace(rememberText))
        {
            RememberCheckBox.Content = rememberText;
            RememberCheckBox.Visibility = Visibility.Visible;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("DialogBackgroundBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("DialogBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("DialogTitleBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("DialogTextBrush", useLightTheme ? "#4F6B88" : "#D5E4F5");
        SetBrushColor("DialogMutedBrush", useLightTheme ? "#68839E" : "#89A6C2");
        SetBrushColor("DialogActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("DialogActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("DialogPrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("DialogPrimaryBorderBrush", useLightTheme ? "#6FD6A4" : "#7BE2B2");
        SetBrushColor("DialogPrimaryTextBrush", "#0A2416");
        SetBrushColor("DialogPanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("DialogPanelBorderBrush", useLightTheme ? "#BED1E4" : "#274A6B");
    }

    private void SetBrushColor(string resourceKey, string hexColor)
    {
        if (Resources[resourceKey] is not System.Windows.Media.SolidColorBrush brush)
        {
            return;
        }

        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
        if (brush.IsFrozen)
        {
            Resources[resourceKey] = new System.Windows.Media.SolidColorBrush(color);
            return;
        }

        brush.Color = color;
    }
}
