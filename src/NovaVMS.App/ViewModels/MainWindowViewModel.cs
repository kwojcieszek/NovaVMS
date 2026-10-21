using NovaVMS.Core.Exceptions;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Main window: resource tree, live view, playback and PTZ.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigurationService _configuration;
    private readonly DeviceSyncService _sync;
    private readonly SettingsSyncService _settingsSync;
    private readonly Func<SettingsWindowViewModel> _settingsFactory;
    private readonly IDialogService _dialogs;
    // Cancelled when the window closes so background work does not outlive the UI.
    private readonly CancellationTokenSource _lifetime = new();
    private static readonly TimeSpan SettingsUploadShutdownTimeout = TimeSpan.FromSeconds(5);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLivePage))]
    [NotifyPropertyChangedFor(nameof(IsPlaybackPage))]
    private AppPage _currentPage = AppPage.Live;

    public MainWindowViewModel(
        ConfigurationService configuration,
        DeviceSyncService sync,
        SettingsSyncService settingsSync,
        Func<SettingsWindowViewModel> settingsFactory,
        ResourceTreeViewModel tree,
        LiveViewViewModel live,
        PlaybackViewModel playback,
        PtzViewModel ptz,
        StatusBarService statusBar,
        ApplicationUpdateService updates,
        UserSessionService session,
        IDialogService dialogs)
    {
        _configuration = configuration;
        _sync = sync;
        _settingsSync = settingsSync;
        _settingsFactory = settingsFactory;
        _dialogs = dialogs;
        Session = session;
        Session.Changed += OnSessionChanged;
        Tree = tree;
        Live = live;
        Playback = playback;
        Ptz = ptz;
        StatusBar = statusBar;
        Updates = updates;

        Tree.PropertyChanged += OnTreePropertyChanged;
        Tree.OpenRequested += OnTreeOpenRequested;
        Tree.NewViewRequested += OnTreeNewViewRequested;
        Tree.ViewCameraRemoved += (_, camera) => Live.RemoveViewCamera(camera.ParentViewId, camera.ViewSlotIndex);
        Tree.ResetViewRequested += OnTreeResetViewRequested;
        Tree.GroupContentChanged += OnTreeGroupContentChanged;
        Live.PropertyChanged += OnLivePropertyChanged;
    }

    public ResourceTreeViewModel Tree { get; }

    public LiveViewViewModel Live { get; }

    public PlaybackViewModel Playback { get; }

    public PtzViewModel Ptz { get; }

    public StatusBarService StatusBar { get; }
    public ApplicationUpdateService Updates { get; }
    public UserSessionService Session { get; }

    /// <summary>
    /// Raised when nobody logs in, so the application closes.
    /// </summary>
    public event EventHandler? ExitRequested;

    public bool IsLivePage => CurrentPage == AppPage.Live;

    public bool IsPlaybackPage => CurrentPage == AppPage.Playback;

    public SettingsWindowViewModel? CreateSettings()
    {
        return Session.CanOpenSettings ? _settingsFactory() : null;
    }

    /// <summary>
    /// Stops streams and background work. Called while the main window is closing,
    /// before native video surfaces are destroyed.
    /// </summary>
    public void PrepareShutdown()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        Live.Shutdown();
        Playback.Shutdown();
    }

    /// <summary>
    /// Releases media players once the main window is closed and its video views are detached.
    /// Releasing them later, at application exit, crashes the runtime inside LibVLC callbacks.
    /// </summary>
    public async Task CompleteShutdownAsync()
    {
        PrepareShutdown();
        Live.Dispose();
        Playback.Dispose();
        await Task.WhenAll(Live.Disposal, Playback.Disposal);
        await _configuration.FlushAsync();
        // Send changes still waiting for the automatic upload, without holding the exit on an unreachable database.
        await Task.WhenAny(_settingsSync.FlushAsync(), Task.Delay(SettingsUploadShutdownTimeout));
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await _configuration.LoadAsync();
        // A newer version from the settings database is applied before devices connect.
        await _settingsSync.StartAsync();
        // Users come from the configuration, so the login follows the newest synchronized version.
        if (!Session.TryAutoLogin() && !await _dialogs.LoginAsync(Session, string.Empty))
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Updates.Start();
        Live.ApplyDefaultLayout();
        if (_configuration.Current.Views.FirstOrDefault() is { } firstView)
            await OpenSavedViewAsync(firstView.Id);

        var deviceCount = _configuration.Current.Devices.Count;
        if (deviceCount == 0)
        {
            StatusBar.Message = Localizer.Text("Main_NoDevices");
            return;
        }

        StatusBar.Message = Localizer.Text("Main_Connecting");
        try
        {
            var online = await _sync.SyncAllAsync(_lifetime.Token);
            StatusBar.Message = Localizer.Format("Main_DevicesOnline", online, deviceCount);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The application is closing.
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            StatusBar.Message = DeviceOperationErrors.Report("Initial device synchronization", exception);
        }
    }

    [RelayCommand]
    private void ShowLive()
    {
        CurrentPage = AppPage.Live;
    }

    [RelayCommand]
    private void ShowPlayback()
    {
        if (Session.CanPlayback)
        {
            CurrentPage = AppPage.Playback;
        }
    }

    /// <summary>
    /// Ends the session and asks for a login again. Live streams are released while nobody is logged in;
    /// views and groups stay as they are.
    /// </summary>
    [RelayCommand]
    private async Task LogoutAsync()
    {
        var userName = Session.UserName;
        Live.IsVideoOnly = false;
        Live.SetPageActive(false);
        Session.Logout();
        if (!await _dialogs.LoginAsync(Session, userName))
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Live.SetPageActive(IsLivePage);
        StatusBar.Message = Localizer.Format("Login_LoggedIn", Session.UserName);
    }

    [RelayCommand]
    private async Task ToggleAutoLoginAsync()
    {
        try
        {
            await Session.SetAutoLoginAsync(!Session.IsAutoLogin);
            StatusBar.Message = Localizer.Text(Session.IsAutoLogin ? "Login_AutoLoginOn" : "Login_AutoLoginOff");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusBar.Message = Localizer.Format("General_SaveFailed", exception.Message);
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        // Permissions may be withdrawn while a page that needs them is open.
        if (IsPlaybackPage && !Session.CanPlayback)
        {
            CurrentPage = AppPage.Live;
        }

        Ptz.SetTarget(Session.CanControlPtz ? Live.SelectedCamera : null);
    }

    [RelayCommand]
    private async Task CycleThemeAsync()
    {
        // The theme is a general setting shared through the configuration.
        if (!Session.CanEditGeneralSettings)
        {
            return;
        }

        var settings = _configuration.Current.Settings;
        settings.Theme = settings.Theme switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        await _configuration.SaveAsync();
        StatusBar.Message = Localizer.Format("Main_ThemeChanged", ThemeOption.Find(settings.Theme).Label);
    }

    [RelayCommand]
    private void OpenSelected()
    {
        Open(Tree.SelectedNode);
    }

    /// <summary>
    /// Shows the whole group in the live view grid. Used when a group is clicked in the tree.
    /// </summary>
    [RelayCommand]
    private void ShowGroup(ResourceGroupNodeViewModel? group)
    {
        if (group is not null && IsLivePage)
        {
            Open(group);
        }
    }

    private void Open(ResourceNodeViewModel? node)
    {
        switch (node)
        {
            case CameraNodeViewModel camera when IsPlaybackPage:
            {
                Playback.SetCamera(camera.Reference);
                break;
            }
            case CameraNodeViewModel camera:
            {
                Live.OpenCamera(camera.Reference);
                break;
            }
            case ResourceGroupNodeViewModel { IsViewsFolder: true }:
                break;
            case ResourceGroupNodeViewModel { IsSavedView: true, Id: { } viewId } when IsLivePage:
                _ = OpenSavedViewAsync(viewId);
                break;
            case ResourceGroupNodeViewModel group when IsLivePage:
            {
                var cameras = Tree.GetCameras(group);
                if (cameras.Count == 0)
                {
                    StatusBar.Message = Localizer.Format("Main_GroupEmpty", group.Name);
                    break;
                }

                Live.ShowGroup(group.ViewKey, cameras);
                StatusBar.Message = Localizer.Format("Main_ShowingGroup", group.Name, cameras.Count);
                break;
            }
        }
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        // Live streams behind the playback page are released after the tiles' grace period.
        Live.SetPageActive(value == AppPage.Live);
        if (value == AppPage.Playback)
        {
            if (Tree.SelectedNode is CameraNodeViewModel camera)
            {
                Playback.SetCamera(camera.Reference);
            }
        }
        else if (Playback.StopCommand.CanExecute(null))
        {
            Playback.StopCommand.Execute(null);
        }
    }

    private async Task OpenSavedViewAsync(Guid id)
    {
        try { await Live.ShowSavedViewAsync(id); }
        catch (Exception exception) { StatusBar.Message = DeviceOperationErrors.Report("Opening saved view", exception); }
    }

    private void OnTreeOpenRequested(object? sender, ResourceNodeViewModel node)
    {
        CurrentPage = AppPage.Live;
        Open(node);
    }

    private void OnTreeNewViewRequested(object? sender, EventArgs e)
    {
        if (!Session.CanEditViews)
        {
            return;
        }

        CurrentPage = AppPage.Live;
        Live.AddNewViewCommand.Execute(null);
    }

    private void OnTreeResetViewRequested(object? sender, ResourceGroupNodeViewModel group)
    {
        Live.ResetGroupView(group.ViewKey, Tree.GetCameras(group));
        StatusBar.Message = Localizer.Format("Main_GroupViewReset", group.Name);
    }

    private void OnTreeGroupContentChanged(object? sender, GroupContentChangedEventArgs e)
    {
        // A remembered placement no longer matches a group whose cameras changed.
        Live.ResetGroupView(e.ViewKey, e.Cameras);
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ResourceTreeViewModel.SelectedNode))
        {
            return;
        }

        if (IsPlaybackPage && Tree.SelectedNode is CameraNodeViewModel camera)
        {
            Playback.SetCamera(camera.Reference);
        }
    }

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveViewViewModel.SelectedCamera))
        {
            Ptz.SetTarget(Session.CanControlPtz ? Live.SelectedCamera : null);
        }
    }
}
