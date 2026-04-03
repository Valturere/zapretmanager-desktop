using System.Windows;
using ZapretManager.ViewModels;

namespace ZapretManager.Services;

public static class DialogService
{
    public readonly record struct ConfirmResult(bool Accepted, bool RememberChoice);

    public static void ShowInfo(string message, string title = "Zapret Manager", Window? owner = null)
    {
        ShowDialog(title, message, isError: false, owner, DialogButtons.Ok);
    }

    public static void ShowError(string message, string title = "Zapret Manager", Window? owner = null)
    {
        ShowDialog(title, message, isError: true, owner, DialogButtons.Ok);
    }

    public static bool Confirm(string message, string title = "Zapret Manager", Window? owner = null)
    {
        return ShowDialog(title, message, isError: false, owner, DialogButtons.YesNo) == true;
    }

    public static ConfirmResult ConfirmWithRemember(
        string message,
        string title = "Zapret Manager",
        Window? owner = null,
        string rememberText = "Больше не спрашивать")
    {
        var dialog = CreateDialog(title, message, isError: false, owner, DialogButtons.YesNo, rememberText, primaryButtonText: null, secondaryButtonText: null);
        var accepted = dialog.ShowDialog() == true;
        return new ConfirmResult(accepted, accepted && dialog.RememberChoice);
    }

    public static bool ConfirmCustom(
        string message,
        string title = "Zapret Manager",
        Window? owner = null,
        string primaryButtonText = "Да",
        string secondaryButtonText = "Нет")
    {
        var dialog = CreateDialog(
            title,
            message,
            isError: false,
            owner,
            DialogButtons.YesNo,
            rememberText: null,
            primaryButtonText,
            secondaryButtonText);
        return dialog.ShowDialog() == true;
    }

    public static DeleteZapretChoice ChooseDeleteZapretMode(
        string rootPath,
        string title = "Zapret Manager",
        Window? owner = null)
    {
        var windowOwner = owner ?? System.Windows.Application.Current?.MainWindow;
        var useLightTheme = windowOwner is MainWindow mainWindow
            ? mainWindow.CurrentUseLightTheme
            : GetUseLightTheme();

        var dialog = new DeleteZapretChoiceWindow(rootPath, useLightTheme)
        {
            Title = title
        };

        if (windowOwner is not null && windowOwner.IsLoaded)
        {
            dialog.Owner = windowOwner;
        }

        dialog.ShowDialog();
        return dialog.Choice;
    }

    private static bool? ShowDialog(string title, string message, bool isError, Window? owner, DialogButtons buttons)
    {
        var dialog = CreateDialog(title, message, isError, owner, buttons, rememberText: null, primaryButtonText: null, secondaryButtonText: null);
        return dialog.ShowDialog();
    }

    private static ThemedDialogWindow CreateDialog(
        string title,
        string message,
        bool isError,
        Window? owner,
        DialogButtons buttons,
        string? rememberText,
        string? primaryButtonText,
        string? secondaryButtonText)
    {
        var windowOwner = owner ?? System.Windows.Application.Current?.MainWindow;
        var useLightTheme = windowOwner is MainWindow mainWindow
            ? mainWindow.CurrentUseLightTheme
            : GetUseLightTheme();
        var dialog = new ThemedDialogWindow(title, message, isError, buttons, useLightTheme, rememberText, primaryButtonText, secondaryButtonText);
        if (windowOwner is not null && windowOwner.IsLoaded)
        {
            dialog.Owner = windowOwner;
        }

        return dialog;
    }

    public static bool GetUseLightTheme()
    {
        if (System.Windows.Application.Current?.MainWindow?.DataContext is MainViewModel viewModel)
        {
            return viewModel.UseLightThemeEnabled;
        }

        return new AppSettingsService().Load().UseLightTheme;
    }

    public enum DialogButtons
    {
        Ok,
        YesNo
    }
}
