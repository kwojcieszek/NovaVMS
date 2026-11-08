using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// General settings of the desktop application except software updates and the desktop SDK folder,
/// together with the settings export / import. Every change is saved at once and, like on the desktop,
/// synchronized with the settings database.
/// </summary>
public sealed partial class GeneralSettingsViewModel : PageViewModel
{
    private const int MinNetworkCachingMs = 0;
    private const int MaxNetworkCachingMs = 5000;
    private const int MinHiddenTileReleaseSeconds = 0;
    private const int MaxHiddenTileReleaseSeconds = 3600;
    private const int MinFrameRate = 0;
    private const int MaxFrameRate = 60;
    private const int MinMainStreamHeight = 1;
    private const int MaxMainStreamHeight = 16384;

    private readonly ShellViewModel _shell;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private bool _useOptionalProtocol;

    [ObservableProperty]
    private StreamModeOption _selectedStreamMode;

    [ObservableProperty]
    private VideoAspectModeOption _selectedAspectMode;

    [ObservableProperty]
    private GridLayoutOption _defaultLayout;

    [ObservableProperty]
    private decimal? _mainStreamMinimumHeight;

    [ObservableProperty]
    private decimal? _networkCachingMs;

    [ObservableProperty]
    private decimal? _mainStreamCachingMs;

    [ObservableProperty]
    private decimal? _subStreamMaxFrameRate;

    [ObservableProperty]
    private decimal? _mainStreamMaxFrameRate;

    [ObservableProperty]
    private decimal? _hiddenTileReleaseSeconds;

    [ObservableProperty]
    private bool _showTileTitles;

    [ObservableProperty]
    private bool _showCameraSearch;

    [ObservableProperty]
    private bool _warnWhenGroupExceedsLayout;

    [ObservableProperty]
    private bool _autoCreateDeviceGroups;

    [ObservableProperty]
    private bool _includePasswordsInExport;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public override MobileTab Tab => MobileTab.Settings;

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

    public GeneralSettingsViewModel(ShellViewModel shell)
    {
        var settings = shell.Current.Settings;

        _shell = shell;
        _selectedLanguage = LanguageOption.Find(settings.Language);
        _selectedTheme = ThemeOption.Find(settings.Theme);
        _useOptionalProtocol = shell.LocalSettings.UseOptionalProtocol;
        _selectedStreamMode = StreamModeOption.Find(settings.GridStreamMode);
        _selectedAspectMode = VideoAspectModeOption.Find(settings.VideoAspectMode);
        _defaultLayout = GridLayoutOption.Find(settings.DefaultLayoutId, settings.DefaultLayoutCount);
        _mainStreamMinimumHeight = settings.MainStreamMinimumHeight;
        _networkCachingMs = settings.NetworkCachingMs;
        _mainStreamCachingMs = settings.MainStreamCachingMs;
        _subStreamMaxFrameRate = settings.SubStreamMaxFrameRate;
        _mainStreamMaxFrameRate = settings.MainStreamMaxFrameRate;
        _hiddenTileReleaseSeconds = settings.HiddenTileReleaseSeconds;
        _showTileTitles = settings.ShowTileTitles;
        _showCameraSearch = settings.ShowCameraSearch;
        _warnWhenGroupExceedsLayout = settings.WarnWhenGroupExceedsLayout;
        _autoCreateDeviceGroups = settings.AutoCreateDeviceGroups;
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowSettings();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_shell.Files is not { } files)
        {
            return;
        }

        try
        {
            await using var stream = await files.SaveJsonFileAsync(Localizer.Text("General_ExportTitle"), $"novavms-settings-{DateTime.Now:yyyyMMdd}.json");

            if (stream is null)
            {
                return;
            }

            await _shell.Configuration.ExportAsync(stream, IncludePasswordsInExport);

            if (stream.CanSeek)
            {
                // Truncate in case an existing, longer file was overwritten.
                stream.SetLength(stream.Position);
            }

            StatusText = Localizer.Text(IncludePasswordsInExport ? "General_ExportedWithPasswords" : "General_ExportedWithoutPasswords");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = Localizer.Format("General_SaveFailed", exception.Message);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_shell.Files is not { } files)
        {
            return;
        }

        AppConfiguration imported;

        try
        {
            await using var stream = await files.OpenJsonFileAsync(Localizer.Text("General_ImportTitle"));

            if (stream is null)
            {
                return;
            }

            imported = await _shell.Configuration.ReadImportAsync(stream);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            StatusText = Localizer.Text("General_ImportInvalid");

            return;
        }

        var message = Localizer.Format("General_ImportConfirm", imported.Devices.Count, imported.Groups.Count);

        if (!await _shell.ConfirmAsync(Localizer.Text("General_ImportTitle"), message))
        {
            return;
        }

        var result = await _shell.Configuration.ImportAsync(imported);

        // The page is rebuilt, so every field shows the imported settings.
        _shell.ShowGeneralSettings();
        _shell.Status = Localizer.Format("General_Imported", result.DevicesAdded, result.DevicesUpdated, result.GroupsAdded, result.GroupsUpdated);
    }

    private void ApplyChanges()
    {
        var settings = _shell.Current.Settings;

        settings.Language = SelectedLanguage.Code;
        settings.Theme = SelectedTheme.Value;
        settings.GridStreamMode = SelectedStreamMode.Value;
        settings.VideoAspectMode = SelectedAspectMode.Value;
        settings.DefaultLayoutCount = DefaultLayout.Count;
        settings.DefaultLayoutId = DefaultLayout.Id;
        settings.MainStreamMinimumHeight = (int)Math.Clamp(MainStreamMinimumHeight ?? settings.MainStreamMinimumHeight, MinMainStreamHeight, MaxMainStreamHeight);
        settings.NetworkCachingMs = (int)Math.Clamp(NetworkCachingMs ?? settings.NetworkCachingMs, MinNetworkCachingMs, MaxNetworkCachingMs);
        settings.MainStreamCachingMs = (int)Math.Clamp(MainStreamCachingMs ?? settings.MainStreamCachingMs, MinNetworkCachingMs, MaxNetworkCachingMs);
        settings.SubStreamMaxFrameRate = (int)Math.Clamp(SubStreamMaxFrameRate ?? settings.SubStreamMaxFrameRate, MinFrameRate, MaxFrameRate);
        settings.MainStreamMaxFrameRate = (int)Math.Clamp(MainStreamMaxFrameRate ?? settings.MainStreamMaxFrameRate, MinFrameRate, MaxFrameRate);
        settings.HiddenTileReleaseSeconds = (int)Math.Clamp(HiddenTileReleaseSeconds ?? settings.HiddenTileReleaseSeconds, MinHiddenTileReleaseSeconds, MaxHiddenTileReleaseSeconds);
        settings.ShowTileTitles = ShowTileTitles;
        settings.ShowCameraSearch = ShowCameraSearch;
        settings.WarnWhenGroupExceedsLayout = WarnWhenGroupExceedsLayout;
        settings.AutoCreateDeviceGroups = AutoCreateDeviceGroups;

        _ = _shell.SaveConfigurationAsync();
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        ApplyChanges();
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        ApplyChanges();
    }

    partial void OnUseOptionalProtocolChanged(bool value)
    {
        _shell.LocalSettings.UseOptionalProtocol = value;
        _ = _shell.SaveLocalSettingsAsync();
    }

    partial void OnSelectedStreamModeChanged(StreamModeOption value)
    {
        _shell.MainStream = value.Value == GridStreamMode.Main;

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

    partial void OnMainStreamMinimumHeightChanged(decimal? value)
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

    partial void OnSubStreamMaxFrameRateChanged(decimal? value)
    {
        ApplyChanges();
    }

    partial void OnMainStreamMaxFrameRateChanged(decimal? value)
    {
        ApplyChanges();
    }

    partial void OnHiddenTileReleaseSecondsChanged(decimal? value)
    {
        ApplyChanges();
    }

    partial void OnShowTileTitlesChanged(bool value)
    {
        ApplyChanges();
    }

    partial void OnShowCameraSearchChanged(bool value)
    {
        ApplyChanges();
    }

    partial void OnWarnWhenGroupExceedsLayoutChanged(bool value)
    {
        ApplyChanges();
    }

    partial void OnAutoCreateDeviceGroupsChanged(bool value)
    {
        ApplyChanges();
    }
}
