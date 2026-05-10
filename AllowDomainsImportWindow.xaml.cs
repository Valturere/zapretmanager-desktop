using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ZapretManager.Services;

namespace ZapretManager;

public partial class AllowDomainsImportWindow : Window
{
    private readonly AllowDomainsImportService _service = new();
    private readonly Action<IReadOnlyList<string>> _applyDomains;
    private readonly HashSet<string> _baseDomains;
    private readonly Dictionary<string, IReadOnlyList<string>> _presetDomainsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _selectedPresetDomains = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitializing;
    private bool _suppressSelectAllUpdate;

    public ObservableCollection<AllowDomainsPresetSelectionItem> Items { get; } = [];

    public AllowDomainsImportWindow(
        IReadOnlyList<AllowDomainsPreset> presets,
        IEnumerable<string> currentDomains,
        Action<IReadOnlyList<string>> applyDomains,
        bool useLightTheme)
    {
        InitializeComponent();
        ApplyTheme(useLightTheme);
        _applyDomains = applyDomains;
        _baseDomains = new HashSet<string>(currentDomains, StringComparer.OrdinalIgnoreCase);

        foreach (var preset in presets)
        {
            var item = new AllowDomainsPresetSelectionItem(preset);
            item.PropertyChanged += PresetItem_PropertyChanged;
            Items.Add(item);
        }

        PresetItemsControl.ItemsSource = Items;
        Loaded += AllowDomainsImportWindow_Loaded;
        UpdateSelectionSummary("Подготавливаем списки...");
    }

    private async void AllowDomainsImportWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _isInitializing = true;
            SetInteractiveState(false);

            var domainsByPreset = await Task.WhenAll(Items.Select(async item =>
                new KeyValuePair<string, IReadOnlyList<string>>(
                    item.Preset.Key,
                    await _service.DownloadDomainsAsync(item.Preset))));

            foreach (var pair in domainsByPreset)
            {
                _presetDomainsByKey[pair.Key] = pair.Value;
            }

            foreach (var item in Items)
            {
                if (!_presetDomainsByKey.TryGetValue(item.Preset.Key, out var domains) || domains.Count == 0)
                {
                    continue;
                }

                if (domains.All(domain => _baseDomains.Contains(domain)))
                {
                    item.IsSelected = true;
                    _selectedPresetDomains[item.Preset.Key] = domains;
                }
            }

            foreach (var domains in _selectedPresetDomains.Values)
            {
                foreach (var domain in domains)
                {
                    _baseDomains.Remove(domain);
                }
            }

            ApplyCurrentDomains();
            UpdateSelectAllState();
            UpdateSelectionSummary();
        }
        catch (Exception ex)
        {
            DialogService.ShowError(BuildPresetLoadErrorMessage(ex), owner: this);
            Close();
        }
        finally
        {
            _isInitializing = false;
            SetInteractiveState(true);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllUpdate)
        {
            return;
        }

        _isInitializing = true;
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }
        _isInitializing = false;

        RebuildSelectionsFromItems();
        ApplyCurrentDomains();
        UpdateSelectAllState();
        UpdateSelectionSummary();
    }

    private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllUpdate || _isInitializing)
        {
            return;
        }

        _isInitializing = true;
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
        _isInitializing = false;

        RebuildSelectionsFromItems();
        ApplyCurrentDomains();
        UpdateSelectAllState();
        UpdateSelectionSummary();
    }

    private void PresetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSelectAllState();
        UpdateSelectionSummary();
    }

    private void PresetItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AllowDomainsPresetSelectionItem.IsSelected) || sender is not AllowDomainsPresetSelectionItem item)
        {
            return;
        }

        _ = ApplySelectionChangeAsync(item);
    }

    private async Task ApplySelectionChangeAsync(AllowDomainsPresetSelectionItem item)
    {
        if (_isInitializing)
        {
            return;
        }

        try
        {
            SetInteractiveState(false);

            if (!_presetDomainsByKey.TryGetValue(item.Preset.Key, out var domains))
            {
                domains = await _service.DownloadDomainsAsync(item.Preset);
                _presetDomainsByKey[item.Preset.Key] = domains;
            }

            if (item.IsSelected)
            {
                _selectedPresetDomains[item.Preset.Key] = domains;
            }
            else
            {
                _selectedPresetDomains.Remove(item.Preset.Key);
            }

            ApplyCurrentDomains();
            UpdateSelectAllState();
            UpdateSelectionSummary();
        }
        catch (Exception ex)
        {
            DialogService.ShowError(BuildPresetUpdateErrorMessage(item.Label, ex), owner: this);
            _isInitializing = true;
            item.IsSelected = !item.IsSelected;
            _isInitializing = false;
            UpdateSelectAllState();
            UpdateSelectionSummary();
        }
        finally
        {
            SetInteractiveState(true);
        }
    }

    private void RebuildSelectionsFromItems()
    {
        _selectedPresetDomains.Clear();
        foreach (var item in Items.Where(item => item.IsSelected))
        {
            if (_presetDomainsByKey.TryGetValue(item.Preset.Key, out var domains))
            {
                _selectedPresetDomains[item.Preset.Key] = domains;
            }
        }
    }

    private void ApplyCurrentDomains()
    {
        var result = new HashSet<string>(_baseDomains, StringComparer.OrdinalIgnoreCase);
        foreach (var domains in _selectedPresetDomains.Values)
        {
            foreach (var domain in domains)
            {
                result.Add(domain);
            }
        }

        _applyDomains(result.OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void UpdateSelectAllState()
    {
        _suppressSelectAllUpdate = true;
        SelectAllCheckBox.IsChecked = Items.Count > 0 && Items.All(item => item.IsSelected);
        _suppressSelectAllUpdate = false;
    }

    private void UpdateSelectionSummary(string? overrideText = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideText))
        {
            SelectionSummaryTextBlock.Text = overrideText;
            return;
        }

        var selectedCount = Items.Count(item => item.IsSelected);
        SelectionSummaryTextBlock.Text = selectedCount == 0
            ? "Отметьте нужные наборы."
            : $"Выбрано наборов: {selectedCount}";
    }

    private static string BuildPresetLoadErrorMessage(Exception exception)
    {
        var details = DialogService.GetDisplayMessage(exception, "Не удалось загрузить готовые списки доменов.");
        if (details.Contains("Что можно попробовать:", StringComparison.OrdinalIgnoreCase) ||
            details.StartsWith("Не удалось загрузить список доменов", StringComparison.OrdinalIgnoreCase))
        {
            return details;
        }

        return
            "Не удалось загрузить готовые списки доменов." +
            $"{Environment.NewLine}{Environment.NewLine}Проверьте интернет и доступ к GitHub, затем повторите попытку." +
            $"{Environment.NewLine}{Environment.NewLine}Техническая причина:{Environment.NewLine}{details}";
    }

    private static string BuildPresetUpdateErrorMessage(string label, Exception exception)
    {
        var details = DialogService.GetDisplayMessage(exception, $"Не удалось обновить список {label}.");
        if (details.Contains("Что можно попробовать:", StringComparison.OrdinalIgnoreCase) ||
            details.StartsWith("Не удалось загрузить список доменов", StringComparison.OrdinalIgnoreCase))
        {
            return details;
        }

        return
            $"Не удалось обновить список {label}." +
            $"{Environment.NewLine}{Environment.NewLine}Попробуйте повторить действие ещё раз." +
            $"{Environment.NewLine}{Environment.NewLine}Техническая причина:{Environment.NewLine}{details}";
    }

    private void SetInteractiveState(bool isEnabled)
    {
        SelectAllCheckBox.IsEnabled = isEnabled;
        PresetItemsControl.IsEnabled = isEnabled;
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TitleBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("TextBrush", useLightTheme ? "#22364D" : "#D6E3F5");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0C1626");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#D8E3F0" : "#24364F");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("PrimaryTextBrush", "#F7FBFF");
        SetBrushColor("SelectionBrush", useLightTheme ? "#E8F0FF" : "#142848");
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

public sealed class AllowDomainsPresetSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public AllowDomainsPresetSelectionItem(AllowDomainsPreset preset)
    {
        Preset = preset;
    }

    public AllowDomainsPreset Preset { get; }
    public string Label => Preset.Label;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
