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
        if (isSystem && UseDohCheckBox.IsChecked == true)
        {
            UseDohCheckBox.IsChecked = false;
        }

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
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#5A7591" : "#A7BBD1");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0A1725");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#87A8C8" : "#2D5379");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#6FD6A4" : "#7BE2B2");
        SetBrushColor("PrimaryTextBrush", "#0A2416");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("DisabledBrush", useLightTheme ? "#DCE9F5" : "#13283F");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#4F6B88" : "#8EA6C2");
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
