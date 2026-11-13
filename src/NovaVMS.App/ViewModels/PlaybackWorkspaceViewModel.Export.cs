using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.App.ViewModels;

public sealed partial class PlaybackWorkspaceViewModel
{
    private CancellationTokenSource? _exportCancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelExportCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private double _exportProgress;

    [ObservableProperty]
    private string _exportStatus = string.Empty;

    public ExportRangeViewModel ExportRange { get; } = new();

    internal void RefreshExportSelection()
    {
        ExportCommand.NotifyCanExecuteChanged();
    }

    internal IReadOnlyList<(CameraDescriptor Camera, string Folder)> GetExportTargets()
    {
        var targets = new List<(CameraDescriptor Camera, string Folder)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tile in Tiles.Where(tile => tile.IsExportSelected && tile.Camera is not null && tile.Playback.HasCamera))
        {
            var camera = _configuration.FindCamera(tile.Camera!);

            if (camera is null)
            {
                continue;
            }

            var folder = RecordingExporter.CameraDirectoryName(camera.DisplayName);

            if (!used.Add(folder))
            {
                folder += $"_{RecordingExporter.CameraDirectoryName(camera.Device.DisplayName)}_CH{camera.Channel.Number}";

                if (!used.Add(folder))
                {
                    folder += $"_{camera.Device.Id:N}_{camera.Channel.Id:N}";

                    used.Add(folder);
                }
            }

            targets.Add((camera, folder));
        }

        return targets;
    }

    private bool CanExport()
    {
        return !_shutdown && !IsExporting && Tiles.Any(tile => tile.IsExportSelected && tile.Playback.HasCamera)
            && ExportRange.IsValid;
    }

    [RelayCommand(CanExecute = nameof(HasCamera))]
    private void MarkExportStart()
    {
        ExportRange.MarkStart(Day, TimelinePosition);
    }

    [RelayCommand(CanExecute = nameof(HasCamera))]
    private void MarkExportEnd()
    {
        ExportRange.MarkEnd(Day, TimelinePosition);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportAsync()
    {
        if (!CanExport())
        {
            return Task.CompletedTask;
        }

        var task = ExportCamerasAsync();

        Track(task);

        return task;
    }

    private async Task ExportCamerasAsync()
    {
        var targets = GetExportTargets();
        var from = ExportRange.From;
        var to = ExportRange.To;
        using var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;
        IsExporting = true;
        ExportProgress = 0;
        ExportStatus = Localizer.Text("Playback_ExportPreparing");
        var files = 0;
        var failures = 0;
        var empty = 0;

        try
        {
            var root = await _dialogs.PickFolderAsync(Localizer.Text("Playback_ExportFolder"));

            if (root is null)
            {
                ExportStatus = string.Empty;

                return;
            }

            var plans = new List<(CameraDescriptor Camera, string Folder, IReadOnlyList<RecordingSegment> Coverage)>();

            foreach (var (camera, folder) in targets)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                try
                {
                    using var client = await _clients.CreateAsync(camera.Device, cancellationToken: cancellation.Token);
                    var found = await client.SearchRecordingsAsync(camera.Channel.Number, from, to, cancellation.Token);
                    var coverage = RecordingTimeline.Merge(found, from, to);

                    if (coverage.Count == 0)
                    {
                        empty++;
                        continue;
                    }

                    plans.Add((camera, Path.Combine(root, folder, "nagrania"), coverage));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;

                    DeviceOperationErrors.Report($"Searching export recordings: {camera.FullName}", exception);
                }
            }

            var total = plans.Sum(plan => plan.Coverage.Sum(segment => (segment.End - segment.Start).TotalSeconds));
            var done = 0d;

            foreach (var (camera, folder, coverage) in plans)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                try
                {
                    Directory.CreateDirectory(folder);

                    using var client = await _clients.CreateAsync(camera.Device, cancellationToken: cancellation.Token);

                    foreach (var segment in coverage)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();

                        var duration = segment.End - segment.Start;
                        var before = done;
                        var source = await client.GetPlaybackSourceAsync(camera.Channel.Number, segment.Start, segment.End, cancellation.Token);
                        var progress = new Progress<double>(value =>
                        {
                            if (_exportCancellation != cancellation || cancellation.IsCancellationRequested)
                            {
                                return;
                            }

                            ExportProgress = (before + value * duration.TotalSeconds) / total * 100;
                        });
                        ExportStatus = Localizer.Format("Playback_ExportCamera", camera.FullName, files + 1);
                        var filename = RecordingExporter.FileName(camera.DisplayName, camera.Channel.Number, segment.Start, segment.End);

                        await _exporter.ExportAsync(source, camera.Device.Username, camera.Device.Password, duration,
                            Path.Combine(folder, filename), progress, cancellation.Token);

                        files++;

                        done += duration.TotalSeconds;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;

                    DeviceOperationErrors.Report($"Exporting recordings: {camera.FullName}", exception);
                }
            }

            ExportProgress = 100;
            ExportStatus = Localizer.Format("Playback_ExportBatchComplete", files, empty, failures, root);
        }
        catch (OperationCanceledException)
        {
            ExportStatus = Localizer.Text("Playback_ExportCancelled");
        }
        catch (Exception exception)
        {
            ExportStatus = DeviceOperationErrors.Report("Exporting camera recordings", exception);
        }
        finally
        {
            _exportCancellation = null;
            IsExporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsExporting))]
    private void CancelExport()
    {
        _exportCancellation?.Cancel();
    }

    private void OnExportRangeChanged(object? sender, PropertyChangedEventArgs e)
    {
        ExportCommand.NotifyCanExecuteChanged();
    }
}
