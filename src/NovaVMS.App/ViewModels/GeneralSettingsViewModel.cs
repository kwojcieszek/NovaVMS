using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Localization;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings page: appearance, live view options and settings export / import.
/// Every change is saved and applied immediately.
/// </summary>
public sealed partial class GeneralSettingsViewModel : ViewModelBase, IDisposable
{
    private const int MinNetworkCachingMs = 0;
    private const int MaxNetworkCachingMs = 5000;
    private const int MinHiddenTileReleaseSeconds = 0;
    private const int MaxHiddenTileReleaseSeconds = 3600;
    private const int MinFrameRate = 0;
    private const int MaxFrameRate = 60;

    private readonly ConfigurationService _configuration;
    private readonly LocalSettingsService _localSettings;
    private readonly IDialogService _dialogs;
    // Set while values are copied from the configuration, so loading them does not trigger a save.
    private bool _isLoading;
    private bool _isDisposed;
    private int _sdkStatusVersion;

    [ObservableProperty]
    private string _sdkStatusText = string.Empty;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private StreamModeOption _selectedStreamMode;

    [ObservableProperty]
    private VideoAspectModeOption _selectedAspectMode;

    [ObservableProperty]
    private GridLayoutOption _defaultLayout;

    [ObservableProperty]
    private decimal? _networkCachingMs;

    [ObservableProperty]
    private decimal? _mainStreamCachingMs;

    [ObservableProperty]
    private decimal? _mainStreamMinimumHeight;

    [ObservableProperty]
    private decimal? _hiddenTileReleaseSeconds;

    [ObservableProperty]
    private decimal? _subStreamMaxFrameRate;

    [ObservableProperty]
    private decimal? _mainStreamMaxFrameRate;

    [ObservableProperty]
    private bool _autoCreateDeviceGroups;

    [ObservableProperty]
    private bool _warnWhenGroupExceedsLayout;

    [ObservableProperty]
    private bool _showTileTitles;

    [ObservableProperty]
    private bool _showCameraSearch;

    [ObservableProperty]
    private string _hcNetSdkDirectory;

    [ObservableProperty]
    private bool _includePasswordsInExport;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _autoCheckForUpdates;

    [ObservableProperty]
    private string _updateServerUrl = string.Empty;

    /// <summary>
    /// Setting of this computer only; saved locally and never synchronized.
    /// </summary>
    [ObservableProperty]
    private bool _startFullScreen;

    public ApplicationUpdateService Updates { get; }

    public GeneralSettingsViewModel(ConfigurationService configuration, LocalSettingsService localSettings, IDialogService dialogs, ApplicationUpdateService updates)
    {
        _configuration = configuration;
        _localSettings = localSettings;
        _dialogs = dialogs;
        Updates = updates;
        _startFullScreen = localSettings.Current.StartFullScreen;

        var settings = configuration.Current.Settings;
        _selectedLanguage = LanguageOption.Find(settings.Language);
        _selectedTheme = ThemeOption.Find(settings.Theme);
        _selectedStreamMode = StreamModeOption.Find(settings.GridStreamMode);
        _selectedAspectMode = VideoAspectModeOption.Find(settings.VideoAspectMode);
        _defaultLayout = GridLayoutOption.Find(settings.DefaultLayoutId, settings.DefaultLayoutCount);
        _networkCachingMs = settings.NetworkCachingMs;
        _mainStreamCachingMs = settings.MainStreamCachingMs;
        _mainStreamMinimumHeight = settings.MainStreamMinimumHeight;
        _hiddenTileReleaseSeconds = settings.HiddenTileReleaseSeconds;
        _subStreamMaxFrameRate = settings.SubStreamMaxFrameRate;
        _mainStreamMaxFrameRate = settings.MainStreamMaxFrameRate;
        _autoCreateDeviceGroups = settings.AutoCreateDeviceGroups;
        _warnWhenGroupExceedsLayout = settings.WarnWhenGroupExceedsLayout;
        _showTileTitles = settings.ShowTileTitles;
        _showCameraSearch = settings.ShowCameraSearch;
        _hcNetSdkDirectory = settings.HcNetSdkDirectory;
        _autoCheckForUpdates = settings.AutoCheckForUpdates;
        _updateServerUrl = settings.UpdateServerUrl ?? "";

        // Settings can also change outside this page (e.g. the theme button in the header).
        _configuration.Changed += OnConfigurationChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        _ = RefreshSdkStatusAsync();
    }

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    public IReadOnlyList<ThemeOption> Themes => ThemeOption.All;

    public IReadOnlyList<StreamModeOption> StreamModes => StreamModeOption.All;

    public IReadOnlyList<VideoAspectModeOption> AspectModes => VideoAspectModeOption.All;

    public IReadOnlyList<GridLayoutOption> Layouts => GridLayoutOption.All;

    public decimal MinimumNetworkCaching => MinNetworkCachingMs;

    public decimal MaximumNetworkCaching => MaxNetworkCachingMs;

    public decimal MinimumHiddenTileRelease => MinHiddenTileReleaseSeconds;

    public decimal MaximumHiddenTileRelease => MaxHiddenTileReleaseSeconds;

    public decimal MinimumFrameRate => MinFrameRate;

    public decimal MaximumFrameRate => MaxFrameRate;

    /// <summary>
    /// State of the Hikvision SDK library in the selected folder.
    /// </summary>
    private async Task RefreshSdkStatusAsync()
    {
        var version = ++_sdkStatusVersion;
        var directory = HcNetSdkDirectory;
        // Typing a UNC path must not synchronously probe a network share on every keypress.
        await Task.Delay(200);
        if (_isDisposed || version != _sdkStatusVersion) return;
        var text = await Task.Run(() => ReadSdkStatus(directory));
        if (!_isDisposed && version == _sdkStatusVersion) SdkStatusText = text;
    }

    private static string ReadSdkStatus(string configuredDirectory)
    {
        if (!HcNetSdkRuntime.IsPlatformSupported) return Localizer.Text("General_SdkUnsupported");
        var directory = HcNetSdkRuntime.ResolveDirectory(configuredDirectory);
        var loadedVersion = HcNetSdkRuntime.LoadedVersion;
        if (loadedVersion is not null)
            return string.Equals(HcNetSdkRuntime.LoadedDirectory, directory, StringComparison.OrdinalIgnoreCase)
                ? Localizer.Format("General_SdkLoaded", loadedVersion) : Localizer.Text("General_SdkRestart");
        return HcNetSdkRuntime.FindLibrary(configuredDirectory) is null
            ? Localizer.Format("General_SdkMissing", directory) : Localizer.Text("General_SdkFound");
    }

    public void Dispose()
    {
        _isDisposed = true;
        _sdkStatusVersion++;
        _configuration.Changed -= OnConfigurationChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        ApplyChanges();
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        ApplyChanges();
    }

    partial void OnSelectedStreamModeChanged(StreamModeOption value)
    {
        ApplyChanges();
    }

    partial void OnSelectedAspectModeChanged(VideoAspectModeOption value)
    {
        ApplyChanges();
    }

    partial void OnDefaultLayoutChanged(GridLayoutOption value)
    {
        ApplyChanges();
    }

    partial void OnNetworkCachingMsChanged(decimal? value)
    {
        ApplyChanges();
    }

    partial void OnMainStreamCachingMsChanged(decimal? value)
    {
        ApplyChanges();
    }

    partial void OnMainStreamMinimumHeightChanged(decimal? value) => ApplyChanges();

    partial void OnHiddenTileReleaseSecondsChanged(decimal? value)
    {
        ApplyChanges();
    }

    partial void OnSubStreamMaxFrameRateChanged(decimal? value) => ApplyChanges();

    partial void OnMainStreamMaxFrameRateChanged(decimal? value) => ApplyChanges();

    partial void OnAutoCreateDeviceGroupsChanged(bool value)
    {
        ApplyChanges();
    }

    partial void OnWarnWhenGroupExceedsLayoutChanged(bool value) => ApplyChanges();

    partial void OnShowTileTitlesChanged(bool value)
    {
        ApplyChanges();
    }

    partial void OnShowCameraSearchChanged(bool value)
    {
        ApplyChanges();
    }

    partial void OnHcNetSdkDirectoryChanged(string value)
    {
        _ = RefreshSdkStatusAsync();
        ApplyChanges();
    }

    partial void OnAutoCheckForUpdatesChanged(bool value) => ApplyChanges();
    partial void OnUpdateServerUrlChanged(string value) => ApplyChanges();

    partial void OnStartFullScreenChanged(bool value)
    {
        _localSettings.Current.StartFullScreen = value;
        _ = SaveLocalAsync();
    }

    private async Task SaveLocalAsync()
    {
        try
        {
            await _localSettings.SaveAsync();
            StatusText = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = Localizer.Format("General_SaveFailed", exception.Message);
        }
    }

    private void ApplyChanges()
    {
        if (_isLoading)
        {
            return;
        }

        var settings = _configuration.Current.Settings;
        settings.Language = SelectedLanguage.Code;
        settings.Theme = SelectedTheme.Value;
        settings.GridStreamMode = SelectedStreamMode.Value;
        settings.VideoAspectMode = SelectedAspectMode.Value;
        settings.DefaultLayoutCount = DefaultLayout.Count;
        settings.DefaultLayoutId = DefaultLayout.Id;
        settings.NetworkCachingMs = (int)Math.Clamp(NetworkCachingMs ?? settings.NetworkCachingMs, MinNetworkCachingMs, MaxNetworkCachingMs);
        settings.MainStreamCachingMs = (int)Math.Clamp(MainStreamCachingMs ?? settings.MainStreamCachingMs, MinNetworkCachingMs, MaxNetworkCachingMs);
        settings.MainStreamMinimumHeight = (int)Math.Clamp(MainStreamMinimumHeight ?? settings.MainStreamMinimumHeight, 1, 16384);
        settings.HiddenTileReleaseSeconds = (int)Math.Clamp(HiddenTileReleaseSeconds ?? settings.HiddenTileReleaseSeconds, MinHiddenTileReleaseSeconds, MaxHiddenTileReleaseSeconds);
        settings.SubStreamMaxFrameRate = (int)Math.Clamp(SubStreamMaxFrameRate ?? settings.SubStreamMaxFrameRate, MinFrameRate, MaxFrameRate);
        settings.MainStreamMaxFrameRate = (int)Math.Clamp(MainStreamMaxFrameRate ?? settings.MainStreamMaxFrameRate, MinFrameRate, MaxFrameRate);
        settings.AutoCreateDeviceGroups = AutoCreateDeviceGroups;
        settings.WarnWhenGroupExceedsLayout = WarnWhenGroupExceedsLayout;
        settings.ShowTileTitles = ShowTileTitles;
        settings.ShowCameraSearch = ShowCameraSearch;
        settings.HcNetSdkDirectory = HcNetSdkDirectory.Trim();
        settings.AutoCheckForUpdates = AutoCheckForUpdates;
        settings.UpdateServerUrl = UpdateServerUrl.Trim();

        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _configuration.SaveAsync();
            StatusText = string.Empty;
        }
        catch (IOException exception)
        {
            StatusText = Localizer.Format("General_SaveFailed", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusText = Localizer.Format("General_SaveFailed", exception.Message);
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        LoadFromSettings(_configuration.Current.Settings);
        _ = RefreshSdkStatusAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _ = RefreshSdkStatusAsync();
    }

    [RelayCommand]
    private async Task BrowseSdkDirectoryAsync()
    {
        var folder = await _dialogs.PickFolderAsync(Localizer.Text("General_SdkBrowseTitle"));
        if (folder is not null)
        {
            HcNetSdkDirectory = folder;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        await using var stream = await _dialogs.SaveJsonFileAsync(Localizer.Text("General_ExportTitle"), $"novavms-settings-{DateTime.Now:yyyyMMdd}.json");
        if (stream is null)
        {
            return;
        }

        await _configuration.ExportAsync(stream, IncludePasswordsInExport);
        if (stream.CanSeek)
        {
            // Truncate in case an existing, longer file was overwritten.
            stream.SetLength(stream.Position);
        }

        StatusText = IncludePasswordsInExport
            ? Localizer.Text("General_ExportedWithPasswords")
            : Localizer.Text("General_ExportedWithoutPasswords");
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        AppConfiguration imported;
        await using (var stream = await _dialogs.OpenJsonFileAsync(Localizer.Text("General_ImportTitle")))
        {
            if (stream is null)
            {
                return;
            }

            try
            {
                imported = await _configuration.ReadImportAsync(stream);
            }
            catch (JsonException)
            {
                StatusText = Localizer.Text("General_ImportInvalid");
                return;
            }
        }

        var message = Localizer.Format("General_ImportConfirm", imported.Devices.Count, imported.Groups.Count);
        if (!await _dialogs.ConfirmAsync(Localizer.Text("General_ImportTitle"), message))
        {
            return;
        }

        var result = await _configuration.ImportAsync(imported);
        LoadFromSettings(_configuration.Current.Settings);
        StatusText = Localizer.Format("General_Imported", result.DevicesAdded, result.DevicesUpdated, result.GroupsAdded, result.GroupsUpdated);
    }

    private void LoadFromSettings(AppSettings settings)
    {
        _isLoading = true;
        try
        {
            SelectedLanguage = LanguageOption.Find(settings.Language);
            SelectedTheme = ThemeOption.Find(settings.Theme);
            SelectedStreamMode = StreamModeOption.Find(settings.GridStreamMode);
            SelectedAspectMode = VideoAspectModeOption.Find(settings.VideoAspectMode);
            DefaultLayout = GridLayoutOption.Find(settings.DefaultLayoutId, settings.DefaultLayoutCount);
            NetworkCachingMs = settings.NetworkCachingMs;
            MainStreamCachingMs = settings.MainStreamCachingMs;
            MainStreamMinimumHeight = settings.MainStreamMinimumHeight;
            HiddenTileReleaseSeconds = settings.HiddenTileReleaseSeconds;
            SubStreamMaxFrameRate = settings.SubStreamMaxFrameRate;
            MainStreamMaxFrameRate = settings.MainStreamMaxFrameRate;
            AutoCreateDeviceGroups = settings.AutoCreateDeviceGroups;
            WarnWhenGroupExceedsLayout = settings.WarnWhenGroupExceedsLayout;
            ShowTileTitles = settings.ShowTileTitles;
            ShowCameraSearch = settings.ShowCameraSearch;
            HcNetSdkDirectory = settings.HcNetSdkDirectory;
            AutoCheckForUpdates = settings.AutoCheckForUpdates;
            UpdateServerUrl = settings.UpdateServerUrl ?? "";
        }
        finally
        {
            _isLoading = false;
        }
    }
}

