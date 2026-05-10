using System.Windows;
using ZapretManager.ViewModels;
using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;

namespace ZapretManager.Services;

public static class DialogService
{
    private const string DefaultErrorMessage = "Произошла ошибка. Закройте программу и попробуйте снова.";
    private const string MovedManagerMessage = "Файл ZapretManager был перемещён после запуска. Закройте программу через трей или Диспетчер задач и откройте её заново из новой папки.";
    private static readonly Regex EncodedControlRegex = new(@"_x(?<code>[0-9A-Fa-f]{4})_", RegexOptions.Compiled);
    private static readonly Regex XmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    public readonly record struct ConfirmResult(bool Accepted, bool RememberChoice);
    public enum DialogChoice
    {
        Closed,
        Primary,
        Secondary,
        Tertiary
    }

    public static void ShowInfo(string message, string title = "Zapret Manager", Window? owner = null)
    {
        ShowDialog(title, message, isError: false, owner, DialogButtons.Ok);
    }

    public static void ShowError(string message, string title = "Zapret Manager", Window? owner = null)
    {
        ShowDialog(title, NormalizeErrorMessage(message), isError: true, owner, DialogButtons.Ok);
    }

    public static void ShowError(Exception exception, string title = "Zapret Manager", Window? owner = null)
    {
        ShowDialog(title, GetDisplayMessage(exception), isError: true, owner, DialogButtons.Ok);
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
        var dialog = CreateDialog(title, message, isError: false, owner, DialogButtons.YesNo, rememberText, primaryButtonText: null, secondaryButtonText: null, tertiaryButtonText: null);
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
            secondaryButtonText,
            tertiaryButtonText: null);
        return dialog.ShowDialog() == true;
    }

    public static DialogChoice ChooseCustom(
        string message,
        string title = "Zapret Manager",
        Window? owner = null,
        string primaryButtonText = "Да",
        string secondaryButtonText = "Нет",
        string tertiaryButtonText = "Отмена")
    {
        var dialog = CreateDialog(
            title,
            message,
            isError: false,
            owner,
            DialogButtons.YesNo,
            rememberText: null,
            primaryButtonText,
            secondaryButtonText,
            tertiaryButtonText);
        _ = dialog.ShowDialog();
        return dialog.Choice;
    }

    public static DialogChoice ChooseErrorCustom(
        string message,
        string title = "Zapret Manager",
        Window? owner = null,
        string primaryButtonText = "Исправить",
        string secondaryButtonText = "Закрыть")
    {
        var dialog = CreateDialog(
            title,
            message,
            isError: true,
            owner,
            DialogButtons.YesNo,
            rememberText: null,
            primaryButtonText,
            secondaryButtonText,
            tertiaryButtonText: null);
        _ = dialog.ShowDialog();
        return dialog.Choice;
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
        var dialog = CreateDialog(title, message, isError, owner, buttons, rememberText: null, primaryButtonText: null, secondaryButtonText: null, tertiaryButtonText: null);
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
        string? secondaryButtonText,
        string? tertiaryButtonText)
    {
        var windowOwner = owner ?? System.Windows.Application.Current?.MainWindow;
        var useLightTheme = windowOwner is MainWindow mainWindow
            ? mainWindow.CurrentUseLightTheme
            : GetUseLightTheme();
        var dialog = new ThemedDialogWindow(title, message, isError, buttons, useLightTheme, rememberText, primaryButtonText, secondaryButtonText, tertiaryButtonText);
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

        var settings = new AppSettingsService().Load();
        return ApplicationThemeService.ResolveUseLightTheme(settings.ApplicationThemeMode, settings.UseLightTheme);
    }

    public static string GetDisplayMessage(Exception exception, string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException aggregateException && aggregateException.InnerExceptions.Count == 1)
        {
            exception = aggregateException.InnerExceptions[0];
        }

        if (exception is System.Reflection.TargetInvocationException targetInvocationException &&
            targetInvocationException.InnerException is not null)
        {
            exception = targetInvocationException.InnerException;
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return MovedManagerMessage;
        }

        if (exception is Win32Exception win32Exception && win32Exception.NativeErrorCode == 2)
        {
            return MovedManagerMessage;
        }

        var message = exception.Message;
        return NormalizeErrorMessage(message, fallback ?? DefaultErrorMessage);
    }

    public static string GetShortDisplayMessage(Exception exception, string? fallback = null)
    {
        return GetShortDisplayMessage(GetDisplayMessage(exception, fallback), fallback);
    }

    public static string GetShortDisplayMessage(string? message, string? fallback = null)
    {
        var normalized = NormalizeErrorMessage(message, fallback);
        var firstLine = normalized
            .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));

        return string.IsNullOrWhiteSpace(firstLine)
            ? NormalizeErrorMessage(null, fallback)
            : firstLine;
    }

    public static string NormalizeErrorMessage(string? message, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            var raw = message.Trim();
            if (LooksLikeRawPowerShellMessage(raw))
            {
                var details = NormalizeTechnicalPayload(raw);
                return BuildPowerShellErrorMessage(details);
            }

            if (LooksLikeMarkupPayload(raw))
            {
                var details = NormalizeTechnicalPayload(raw);
                return BuildUnexpectedResponseMessage(details);
            }

            return raw;
        }

        return string.IsNullOrWhiteSpace(fallback) ? DefaultErrorMessage : fallback.Trim();
    }

    private static bool LooksLikeRawPowerShellMessage(string message)
    {
        return ContainsAny(
            message,
            "CLIXML",
            "<Objs Version=",
            "CategoryInfo",
            "FullyQualifiedErrorId",
            "CommandNotFoundException",
            "NativeCommandError",
            "At line:",
            "строка:",
            "Line |");
    }

    private static bool LooksLikeMarkupPayload(string message)
    {
        return ContainsAny(
            message,
            "<?xml",
            "<html",
            "<!DOCTYPE",
            "<body",
            "</html>",
            "&lt;html",
            "&lt;!DOCTYPE");
    }

    private static string NormalizeTechnicalPayload(string rawMessage)
    {
        var text = rawMessage
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        text = EncodedControlRegex.Replace(text, match =>
        {
            var code = match.Groups["code"].Value;
            return code.Equals("000D", StringComparison.OrdinalIgnoreCase) ||
                   code.Equals("000A", StringComparison.OrdinalIgnoreCase)
                ? "\n"
                : " ";
        });

        text = WebUtility.HtmlDecode(text);
        text = text.Replace("#< CLIXML", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (ContainsAny(text, "<Objs", "<?xml", "<html", "<!DOCTYPE", "&lt;"))
        {
            text = XmlTagRegex.Replace(text, " ");
        }

        var cleanedLines = text
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !StartsWithAny(
                line,
                "At line:",
                "строка:",
                "Line |",
                "CategoryInfo",
                "FullyQualifiedErrorId",
                "+ ",
                "   + "))
            .Select(static line => Regex.Replace(line, @"\s{2,}", " ").Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToArray();

        return cleanedLines.Length == 0
            ? "Windows вернула техническую ошибку без понятного текста."
            : string.Join(Environment.NewLine, cleanedLines);
    }

    private static string BuildPowerShellErrorMessage(string details)
    {
        return
            "Не удалось выполнить системную команду Windows." +
            $"{Environment.NewLine}{Environment.NewLine}Проверьте права администратора и повторите попытку." +
            $"{Environment.NewLine}{Environment.NewLine}Техническая причина:{Environment.NewLine}{details}";
    }

    private static string BuildUnexpectedResponseMessage(string details)
    {
        return
            "Не удалось получить корректный ответ от системы или внешнего сервиса." +
            $"{Environment.NewLine}{Environment.NewLine}Попробуйте повторить действие ещё раз." +
            $"{Environment.NewLine}{Environment.NewLine}Техническая причина:{Environment.NewLine}{details}";
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment => value.StartsWith(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public enum DialogButtons
    {
        Ok,
        YesNo
    }
}
