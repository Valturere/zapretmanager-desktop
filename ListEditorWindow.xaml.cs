using System.Net;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.ComponentModel;
using ZapretManager.Services;

namespace ZapretManager;

public partial class ListEditorWindow : Window
{
    private const string DummyDomainValue = "domain.example.abc";
    private const string DummySubnetValue = "203.0.113.113/32";

    private readonly string _filePath;
    private readonly ListEditorValidationMode _validationMode;
    private readonly string? _defaultContent;
    private readonly bool _allowDomainsImport;
    private readonly bool _useLightTheme;
    private readonly AllowDomainsImportService _allowDomainsImportService = new();
    private string _originalContent = string.Empty;
    private bool _skipUnsavedChangesPrompt;
    public bool WasSaved { get; private set; }

    public ListEditorWindow(
        string title,
        string description,
        string placeholder,
        string filePath,
        bool useLightTheme,
        ListEditorValidationMode validationMode = ListEditorValidationMode.None,
        string? defaultContent = null,
        bool allowDomainsImport = false)
    {
        InitializeComponent();
        _filePath = filePath;
        _validationMode = validationMode;
        _defaultContent = defaultContent;
        _allowDomainsImport = allowDomainsImport;
        _useLightTheme = useLightTheme;
        Title = title;
        TitleTextBlock.Text = title;
        DescriptionTextBlock.Text = description;
        PlaceholderTextBlock.Text = placeholder;
        ApplyTheme(useLightTheme);
        LoadContent();
        _originalContent = EditorTextBox.Text;
        ResetToDefaultButton.Visibility = validationMode == ListEditorValidationMode.TargetFile && !string.IsNullOrWhiteSpace(defaultContent)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ImportAllowDomainsButton.Visibility = _allowDomainsImport && validationMode == ListEditorValidationMode.DomainList
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateState();
        Closing += ListEditorWindow_Closing;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_validationMode == ListEditorValidationMode.TargetFile)
        {
            if (!ValidateTargetFile(EditorTextBox.Text, out var error))
            {
                Services.DialogService.ShowError(error, "Zapret Manager", this);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var normalizedText = NormalizeRawText(EditorTextBox.Text);
            File.WriteAllText(_filePath, normalizedText);
            _originalContent = EditorTextBox.Text;
            _skipUnsavedChangesPrompt = true;
            WasSaved = true;
            Close();
            return;
        }

        var lines = NormalizeLines(EditorTextBox.Text);
        if (_validationMode == ListEditorValidationMode.DomainList)
        {
            var invalid = lines.FirstOrDefault(line => !IsValidDomain(line));
            if (invalid is not null)
            {
                Services.DialogService.ShowError($"Некорректный домен: {invalid}", "Zapret Manager", this);
                return;
            }
        }

        if (_validationMode == ListEditorValidationMode.SubnetList)
        {
            var invalid = lines.FirstOrDefault(line => !IsValidSubnet(line));
            if (invalid is not null)
            {
                Services.DialogService.ShowError($"Некорректная подсеть или IP: {invalid}", "Zapret Manager", this);
                return;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var finalLines = GetFinalLinesForSave(lines);
        File.WriteAllText(_filePath, string.Join(Environment.NewLine, finalLines) + Environment.NewLine);
        _originalContent = EditorTextBox.Text;
        _skipUnsavedChangesPrompt = true;
        WasSaved = true;
        Close();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var clearMessage = _validationMode switch
        {
            ListEditorValidationMode.DomainList => "Очистить список полностью? Изменения попадут в реальный файл только после кнопки \"Сохранить\". После сохранения программа оставит в файле только служебную доменную заглушку.",
            ListEditorValidationMode.SubnetList => "Очистить список полностью? Изменения попадут в реальный файл только после кнопки \"Сохранить\". После сохранения программа оставит в файле только служебную IP-заглушку.",
            _ => "Очистить список полностью? Изменения попадут в реальный файл только после кнопки \"Сохранить\"."
        };

        if (!Services.DialogService.Confirm(clearMessage, "Zapret Manager", this))
        {
            return;
        }

        EditorTextBox.Clear();
    }

    private void ResetToDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_defaultContent))
        {
            return;
        }

        if (!Services.DialogService.Confirm("Вернуть targets.txt к значениям по умолчанию?", "Zapret Manager", this))
        {
            return;
        }

        EditorTextBox.Text = _defaultContent;
    }

    private async void ImportAllowDomainsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowDomainsImport)
        {
            return;
        }

        var importDialog = new AllowDomainsImportWindow(
            _allowDomainsImportService.GetPresets(),
            NormalizeLines(EditorTextBox.Text),
            domains =>
            {
                EditorTextBox.Text = string.Join(Environment.NewLine, domains);
                EditorTextBox.CaretIndex = EditorTextBox.Text.Length;
                EditorTextBox.ScrollToEnd();
            },
            _useLightTheme);
        if (Owner is not null && Owner.IsLoaded)
        {
            importDialog.Owner = Owner;
        }

        importDialog.ShowDialog();
    }

    private void EditorTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateState();
    }

    private void ListEditorWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_skipUnsavedChangesPrompt || !HasUnsavedChanges())
        {
            return;
        }

        if (Services.DialogService.Confirm("Есть несохранённые изменения. Закрыть окно без сохранения?", "Zapret Manager", this))
        {
            return;
        }

        e.Cancel = true;
    }

    private void LoadContent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, string.Empty);
        }

        var content = File.ReadAllText(_filePath);
        EditorTextBox.Text = SanitizeLoadedText(content);
    }

    private void UpdateState()
    {
        var lines = _validationMode == ListEditorValidationMode.TargetFile
            ? CountTargetEntries(EditorTextBox.Text)
            : NormalizeLines(EditorTextBox.Text).Count;

        CountTextBlock.Text = _validationMode switch
        {
            ListEditorValidationMode.SubnetList => $"Записей в списке: {lines}",
            ListEditorValidationMode.TargetFile => $"Целей в списке: {lines}",
            _ => $"Доменов в списке: {lines}"
        };
        PlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(EditorTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static List<string> NormalizeLines(string text)
    {
        return text
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !string.Equals(line, DummyDomainValue, StringComparison.OrdinalIgnoreCase))
            .Where(line => !string.Equals(line, DummySubnetValue, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> GetFinalLinesForSave(IReadOnlyList<string> lines)
    {
        if (lines.Count > 0)
        {
            return lines.ToList();
        }

        return _validationMode switch
        {
            ListEditorValidationMode.DomainList => [DummyDomainValue],
            ListEditorValidationMode.SubnetList => [DummySubnetValue],
            _ => []
        };
    }

    private string SanitizeLoadedText(string text)
    {
        if (_validationMode == ListEditorValidationMode.TargetFile)
        {
            return text;
        }

        var lines = text
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !string.Equals(line, DummyDomainValue, StringComparison.OrdinalIgnoreCase))
            .Where(line => !string.Equals(line, DummySubnetValue, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeRawText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var trimmedLines = lines.Select(line => line.TrimEnd()).ToArray();
        var result = string.Join(Environment.NewLine, trimmedLines).TrimEnd();
        return string.IsNullOrWhiteSpace(result) ? string.Empty : result + Environment.NewLine;
    }

    private bool HasUnsavedChanges()
    {
        return !string.Equals(
            NormalizeEditorText(EditorTextBox.Text),
            NormalizeEditorText(_originalContent),
            StringComparison.Ordinal);
    }

    private string NormalizeEditorText(string text)
    {
        return _validationMode == ListEditorValidationMode.TargetFile
            ? NormalizeRawText(text)
            : string.Join("\n", NormalizeLines(text));
    }

    private static int CountTargetEntries(string text)
    {
        return text
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Count(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'));
    }

    private static bool ValidateTargetFile(string text, out string error)
    {
        foreach (var rawLine in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var match = Regex.Match(line, "^(?<name>[\\p{L}\\p{N}_-]+)\\s*=\\s*\"(?<value>[^\"]+)\"$", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                error = $"Некорректная строка: {line}{Environment.NewLine}Используйте формат KeyName = \"https://site.com\" или KeyName = \"PING:1.1.1.1\".";
                return false;
            }

            var value = match.Groups["value"].Value.Trim();
            if (value.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
            {
                var pingValue = value["PING:".Length..].Trim();
                if (!IPAddress.TryParse(pingValue, out _))
                {
                    error = $"Некорректный PING-адрес: {pingValue}";
                    return false;
                }

                continue;
            }

            if (!IsValidUrl(value))
            {
                error = $"Некорректный URL в targets.txt: {value}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Uri.CheckHostName(uri.Host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    private static bool IsValidDomain(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.Contains('/') ||
            trimmed.Contains('\\') ||
            trimmed.Contains(' ') ||
            trimmed.Contains('\t'))
        {
            return false;
        }

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.Contains(':'))
        {
            return false;
        }

        return Regex.IsMatch(trimmed,
            @"^(?=.{1,253}$)(?!-)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$",
            RegexOptions.CultureInvariant);
    }

    private static bool IsValidSubnet(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (!trimmed.Contains('/'))
        {
            return IPAddress.TryParse(trimmed, out _);
        }

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var prefix))
        {
            return false;
        }

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => prefix is >= 0 and <= 32,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => prefix is >= 0 and <= 128,
            _ => false
        };
    }

    public enum ListEditorValidationMode
    {
        None,
        DomainList,
        TargetFile,
        SubnetList
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#5A7591" : "#A7BBD1");
        SetBrushColor("PanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#9CB7CF" : "#2C5478");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0A1725");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#87A8C8" : "#2D5379");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#6FD6A4" : "#7BE2B2");
        SetBrushColor("PrimaryTextBrush", "#0A2416");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
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
