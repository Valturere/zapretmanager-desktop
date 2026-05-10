using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using ZapretManager.Services;

namespace ZapretManager;

public partial class DnsSettingsWindow : Window
{
    private readonly Dictionary<string, DnsService.DnsProfileDefinition> _profilesByKey;
    private string? _customPrimaryDraft;
    private string? _customSecondaryDraft;
    private string? _customDohTemplateDraft;
    private string? _lastSelectedProfileKey;
    private bool _isInitializing;

    private sealed record DnsProfileItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    public string SelectedProfileKey { get; private set; }
    public string? CustomPrimary { get; private set; }
    public string? CustomSecondary { get; private set; }
    public bool UseDnsOverHttps { get; private set; }
    public string? CustomDohTemplate { get; private set; }
    public bool WasApplied { get; private set; }

    public DnsSettingsWindow(
        IEnumerable<DnsService.DnsProfileDefinition> profiles,
        string currentProfileKey,
        string? customPrimary,
        string? customSecondary,
        bool useDnsOverHttps,
        string? customDohTemplate,
        bool useLightTheme)
    {
        InitializeComponent();
        ApplyTheme(useLightTheme);

        _profilesByKey = profiles.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

        ProfileComboBox.ItemsSource = _profilesByKey.Values
            .Select(item => new DnsProfileItem(item.Key, item.Label))
            .ToArray();

        _isInitializing = true;
        SelectedProfileKey = currentProfileKey;
        CustomPrimary = customPrimary;
        CustomSecondary = customSecondary;
        UseDnsOverHttps = useDnsOverHttps;
        CustomDohTemplate = customDohTemplate;
        _customPrimaryDraft = customPrimary;
        _customSecondaryDraft = customSecondary;
        _customDohTemplateDraft = customDohTemplate;
        _lastSelectedProfileKey = currentProfileKey;
        ProfileComboBox.SelectedValue = currentProfileKey;
        UseDohCheckBox.IsChecked = useDnsOverHttps;

        UpdateFormState();
        _isInitializing = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ProfileComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        if (string.Equals(_lastSelectedProfileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            _customPrimaryDraft = NormalizeDnsValue(PrimaryDnsTextBox.Text);
            _customSecondaryDraft = NormalizeDnsValue(SecondaryDnsTextBox.Text);
            _customDohTemplateDraft = NormalizeDnsValue(DohTemplateTextBox.Text);
        }

        UpdateFormState();
        _lastSelectedProfileKey = ProfileComboBox.SelectedValue as string;
    }

    private void UseDohCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        if (string.Equals(_lastSelectedProfileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            _customDohTemplateDraft = NormalizeDnsValue(DohTemplateTextBox.Text);
        }

        UpdateFormState();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCollectFormValues(
                validateDohTemplate: true,
                out var profileKey,
                out var primary,
                out var secondary,
                out var useDoh,
                out var dohTemplate,
                out var errorMessage))
        {
            DialogService.ShowError(errorMessage, owner: this);
            return;
        }

        SelectedProfileKey = profileKey;
        UseDnsOverHttps = useDoh;
        if (string.Equals(profileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            _customPrimaryDraft = primary;
            _customSecondaryDraft = secondary;
            _customDohTemplateDraft = useDoh ? dohTemplate : _customDohTemplateDraft;
        }

        CustomPrimary = _customPrimaryDraft;
        CustomSecondary = _customSecondaryDraft;
        CustomDohTemplate = _customDohTemplateDraft;
        WasApplied = true;
        Close();
    }

    private void UpdateFormState()
    {
        var selectedKey = ProfileComboBox.SelectedValue as string ?? DnsService.SystemProfileKey;
        var isSystem = string.Equals(selectedKey, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase);
        var isCustom = string.Equals(selectedKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase);
        var useDoh = !isSystem && UseDohCheckBox.IsChecked == true;

        DnsInputsGrid.Visibility = isSystem ? Visibility.Collapsed : Visibility.Visible;
        UseDohCheckBox.Visibility = isSystem ? Visibility.Collapsed : Visibility.Visible;
        DohTemplatePanel.Visibility = !isSystem && useDoh ? Visibility.Visible : Visibility.Collapsed;
        PrimaryDnsTextBox.IsEnabled = isCustom;
        SecondaryDnsTextBox.IsEnabled = isCustom;
        DohTemplateTextBox.IsEnabled = isCustom;

        if (isSystem)
        {
            return;
        }

        if (isCustom)
        {
            PrimaryDnsTextBox.Text = _customPrimaryDraft ?? string.Empty;
            SecondaryDnsTextBox.Text = _customSecondaryDraft ?? string.Empty;
            DohTemplateTextBox.Text = _customDohTemplateDraft ?? string.Empty;
            return;
        }

        if (_profilesByKey.TryGetValue(selectedKey, out var profile))
        {
            PrimaryDnsTextBox.Text = profile.ServerAddresses.ElementAtOrDefault(0) ?? string.Empty;
            SecondaryDnsTextBox.Text = profile.ServerAddresses.ElementAtOrDefault(1) ?? string.Empty;
            DohTemplateTextBox.Text = profile.DohTemplate ?? string.Empty;
        }
    }

    private bool TryCollectFormValues(
        bool validateDohTemplate,
        out string profileKey,
        out string? primary,
        out string? secondary,
        out bool useDoh,
        out string? dohTemplate,
        out string errorMessage)
    {
        profileKey = ProfileComboBox.SelectedValue as string ?? DnsService.SystemProfileKey;
        primary = NormalizeDnsValue(PrimaryDnsTextBox.Text);
        secondary = NormalizeDnsValue(SecondaryDnsTextBox.Text);
        useDoh = !string.Equals(profileKey, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase) &&
                 UseDohCheckBox.IsChecked == true;
        dohTemplate = NormalizeDnsValue(DohTemplateTextBox.Text);

        if (string.Equals(profileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(secondary))
            {
                errorMessage = "Укажите хотя бы один IPv4-адрес для пользовательского DNS.";
                return false;
            }

            if (!IsValidIpv4(primary) || !IsValidIpv4(secondary))
            {
                errorMessage = "Пользовательский DNS должен содержать корректные IPv4-адреса.";
                return false;
            }

            if (validateDohTemplate && useDoh && !IsValidHttpsUrl(dohTemplate))
            {
                errorMessage = "Для пользовательского DoH укажите корректный HTTPS URL.";
                return false;
            }
        }
        else if (validateDohTemplate &&
                 useDoh &&
                 !string.Equals(profileKey, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            var profile = _profilesByKey[profileKey];
            if (!IsValidHttpsUrl(profile.DohTemplate))
            {
                errorMessage = "Для выбранного DNS-профиля не найден корректный DoH URL.";
                return false;
            }
        }

        errorMessage = string.Empty;
        return true;
    }


    private static string? NormalizeDnsValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsValidIpv4(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static bool IsValidHttpsUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0C1626");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#D8E3F0" : "#24364F");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("PrimaryTextBrush", "#F7FBFF");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("DisabledBrush", useLightTheme ? "#EEF3F8" : "#101827");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#8DA0B8" : "#61748D");
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
