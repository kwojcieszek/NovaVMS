using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Localization;
using NovaVMS.App.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Updater;

namespace NovaVMS.App.Services;

public sealed partial class ApplicationUpdateService : ObservableObject, IDisposable
{
    private readonly ConfigurationService _configuration;
    private readonly IDialogService _dialogs;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private bool _started;
    private string _lastUrl = "";
    private bool _lastAutomatic;
    private CancellationTokenSource? _operation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(AvailableText))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private ApplicationUpdate? _available;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private double _progress;

    public bool IsIndeterminate => IsBusy && Progress == 0;

    public bool HasUpdate => Available is not null;
    public string AvailableText => Available is null ? "" : Localizer.Format("Updates_Available", Available.VersionText);
    public string CurrentVersion => ApplicationVersion.Number;

    public ApplicationUpdateService(ConfigurationService configuration, IDialogService dialogs)
    {
        _configuration = configuration;
        _dialogs = dialogs;
        _configuration.Changed += OnConfigurationChanged;
        _timer.Tick += OnTimer;
    }

    public void Start()
    {
        _started = true;
        OnConfigurationChanged(this, EventArgs.Empty);
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        if (!_started) return;
        var settings = _configuration.Current.Settings;
        var url = (settings.UpdateServerUrl ?? "").Trim();
        if (url == _lastUrl && settings.AutoCheckForUpdates == _lastAutomatic) return;
        if (url != _lastUrl) Available = null;
        _lastUrl = url;
        _lastAutomatic = settings.AutoCheckForUpdates;
        _timer.Stop();
        if (_lastAutomatic && url.Length > 0)
        {
            _timer.Start();
            _ = CheckAfterDelayAsync(url);
        }
    }

    private async Task CheckAfterDelayAsync(string url)
    {
        try
        {
            await Task.Delay(2000, _lifetime.Token);
            if (url == _lastUrl && _lastAutomatic && !IsBusy) await CheckAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        if (!IsBusy && _configuration.Current.Settings.AutoCheckForUpdates) _ = CheckAsync();
    }

    private bool CanCheck() => !IsBusy && !_lifetime.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        if (!CanCheck()) return;
        var url = _configuration.Current.Settings.UpdateServerUrl;
        IsBusy = true;
        Progress = 0;
        StatusText = Localizer.Text("Updates_Checking");
        try
        {
            if (!OperatingSystem.IsWindows()) throw new NotSupportedException(Localizer.Text("Updates_WindowsOnly"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _operation = timeout;
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var found = await UpdateCatalog.CheckAsync(_http, url, CurrentVersion, timeout.Token);
            if (url != _configuration.Current.Settings.UpdateServerUrl) return;
            Available = found;
            StatusText = found is null ? Localizer.Text("Updates_Current") : AvailableText;
        }
        catch (OperationCanceledException) { StatusText = Localizer.Text("Updates_Cancelled"); }
        catch (Exception exception) { StatusText = Localizer.Format("Updates_Failed", exception.Message); }
        finally
        {
            _operation = null;
            IsBusy = false;
            if (url != _configuration.Current.Settings.UpdateServerUrl && _lastAutomatic) _ = CheckAfterDelayAsync(_lastUrl);
        }
    }

    private bool CanInstall() => HasUpdate && !IsBusy && !_lifetime.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (Available is not { } update || !CanInstall()) return;
        IsBusy = true;
        Progress = 0;
        StatusText = Localizer.Text("Updates_Downloading");
        var job = Path.Combine(Path.GetTempPath(), "NovaVMS-updates", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(job, "package.zip");
        var handedOff = false;
        using var download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        download.CancelAfter(TimeSpan.FromMinutes(15));
        _operation = download;
        try
        {
            Directory.CreateDirectory(job);
            var executable = Environment.ProcessPath;
            if (executable is null || !Path.GetFileName(executable).Equals("NovaVMS.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException(Localizer.Text("Updates_RunExecutable"));
            var install = Path.GetDirectoryName(executable)!;
            // Check permissions before closing the running application.
            var writeTest = Path.Combine(install, ".novavms-write-test-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(writeTest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }

            using (var response = await _http.GetAsync(update.PackageUri, HttpCompletionOption.ResponseHeadersRead, download.Token))
            {
                response.EnsureSuccessStatusCode();
                var size = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(download.Token);
                await using var output = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await UpdateCatalog.CopyLimitedAsync(input, output, 4L * 1024 * 1024 * 1024, downloaded =>
                {
                    if (size is > 0) Dispatcher.UIThread.Post(() => Progress = Math.Min(99, downloaded * 100d / size.Value));
                }, download.Token);
            }
            var hash = await Task.Run(() =>
            {
                var actual = UpdatePackage.Sha256(package);
                if (update.Sha256 is not null && !actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Localizer.Text("Updates_ChecksumFailed"));
                using var archive = ZipFile.OpenRead(package);
                UpdatePackage.Validate(archive);
                return actual;
            });
            Progress = 100;
            download.Token.ThrowIfCancellationRequested();
            if (!await _dialogs.ConfirmAsync(Localizer.Text("Updates_Title"), Localizer.Format("Updates_InstallConfirm", update.VersionText)))
            {
                StatusText = AvailableText;
                return;
            }
            // Copy the installer out of the application directory so it can replace its own files.
            var helperSource = Path.Combine(AppContext.BaseDirectory, "updater");
            var helper = Path.Combine(job, "helper");
            if (!File.Exists(Path.Combine(helperSource, "NovaVMS.Updater.exe"))) throw new IOException(Localizer.Text("Updates_HelperMissing"));
            foreach (var source in Directory.EnumerateFiles(helperSource, "*", SearchOption.AllDirectories))
            {
                var target = UpdatePackage.SafePath(helper, Path.GetRelativePath(helperSource, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            var plan = new UpdatePlan { InstallDirectory = install, JobDirectory = job, PackagePath = package,
                PackageSha256 = hash, Version = update.VersionText, Arguments = Environment.GetCommandLineArgs().Skip(1).ToArray() };
            var planPath = Path.Combine(job, "plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), download.Token);
            await _configuration.FlushAsync();
            var start = new ProcessStartInfo(Path.Combine(helper, "NovaVMS.Updater.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helper };
            start.ArgumentList.Add(planPath);
            using var process = Process.Start(start) ?? throw new IOException(Localizer.Text("Updates_HelperMissing"));
            handedOff = true;
            StatusText = Localizer.Text("Updates_Restarting");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows.Where(window => window != desktop.MainWindow).ToArray()) window.Close();
                desktop.MainWindow?.Close(); // Normal shutdown flushes settings and stops native SDK/VLC workers.
            }
        }
        catch (OperationCanceledException) { StatusText = Localizer.Text("Updates_Cancelled"); }
        catch (Exception exception) { StatusText = Localizer.Format("Updates_Failed", exception.Message); }
        finally
        {
            if (!handedOff && File.Exists(package))
                try { File.Delete(package); } catch (IOException exception) { UnhandledExceptionLogger.Log("Cleaning cancelled update download", exception); }
            _operation = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _operation?.Cancel();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimer;
        _configuration.Changed -= OnConfigurationChanged;
        _lifetime.Cancel();
        _http.Dispose();
    }
}
