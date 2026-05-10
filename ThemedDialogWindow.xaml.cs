using System.Windows;
using System.Windows.Input;
using ZapretManager.Services;

namespace ZapretManager;

public partial class ThemedDialogWindow : Window
{
    public bool RememberChoice => RememberCheckBox.IsChecked == true;
    public DialogService.DialogChoice Choice { get; private set; } = DialogService.DialogChoice.Closed;

    public ThemedDialogWindow(
        string title,
        string message,
        bool isError,
        DialogService.DialogButtons buttons,
        bool useLightTheme,
        string? rememberText = null,
        string? primaryButtonText = null,
        string? secondaryButtonText = null,
        string? tertiaryButtonText = null)
    {
        InitializeComponent();
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ApplyTheme(useLightTheme);
        SubtitleTextBlock.Text = isError
            ? "Обратите внимание"
            : buttons == DialogService.DialogButtons.YesNo
                ? "Требуется подтверждение"
                : "Результат операции";
        StatusGlyphPath.Tag = isError ? "error" : buttons == DialogService.DialogButtons.YesNo ? "question" : "info";
        PrimaryButton.Content = primaryButtonText ?? (buttons == DialogService.DialogButtons.YesNo ? "Да" : "Закрыть");
        SecondaryButton.Content = secondaryButtonText ?? "Нет";
        SecondaryButton.Visibility = buttons == DialogService.DialogButtons.YesNo ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(tertiaryButtonText))
        {
            TertiaryButton.Content = tertiaryButtonText;
            TertiaryButton.Visibility = Visibility.Visible;
        }

        var useErrorPrimaryStyle = isError &&
                                   buttons == DialogService.DialogButtons.Ok &&
                                   string.IsNullOrWhiteSpace(primaryButtonText);
        PrimaryButton.Background = useErrorPrimaryStyle
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 91, 91))
            : (System.Windows.Media.Brush)FindResource("DialogPrimaryBrush");
        PrimaryButton.BorderBrush = useErrorPrimaryStyle
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
        Choice = DialogService.DialogChoice.Primary;
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DialogService.DialogChoice.Secondary;
        DialogResult = false;
        Close();
    }

    private void TertiaryButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DialogService.DialogChoice.Tertiary;
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DialogService.DialogChoice.Closed;
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
        SetBrushColor("DialogBackgroundBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("DialogBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("DialogTitleBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("DialogTextBrush", useLightTheme ? "#22364D" : "#D6E3F5");
        SetBrushColor("DialogMutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("DialogActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("DialogActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("DialogPrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("DialogPrimaryBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("DialogPrimaryTextBrush", "#F7FBFF");
        SetBrushColor("DialogPanelBrush", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("DialogPanelBorderBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
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
