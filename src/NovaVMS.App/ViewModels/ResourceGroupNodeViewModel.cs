using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Camera group in the resource tree. Favorites have no group identifier.
/// </summary>
public sealed partial class ResourceGroupNodeViewModel : ResourceNodeViewModel
{
    private readonly HashSet<CameraNodeViewModel> _observedCameras = new();

    [ObservableProperty]
    private bool _isRecorderOffline;

    [ObservableProperty]
    private bool _hasUnavailableCamera;

    public Guid? Id { get; }

    public Guid? SourceDeviceId { get; set; }

    /// <summary>
    /// Key under which the live view remembers the split and camera placement of this group.
    /// </summary>
    public string ViewKey => GetViewKey(Id, Kind);

    public GroupNodeKind Kind { get; }

    public bool IsCustom => Kind == GroupNodeKind.Custom;

    public bool IsDeviceGroup => Kind == GroupNodeKind.Device;

    public bool IsFavorites => Kind == GroupNodeKind.Favorites;

    public bool IsViewsFolder => Kind == GroupNodeKind.ViewsFolder;

    public bool IsSavedView => Kind == GroupNodeKind.SavedView;

    public bool IsCameraGroup => !IsViewsFolder && !IsSavedView;

    public ObservableCollection<ResourceGroupNodeViewModel> Views { get; } = new();

    public IEnumerable<ResourceNodeViewModel> Children => IsViewsFolder ? Views : Cameras;

    public ObservableCollection<CameraNodeViewModel> Cameras { get; } = new();

    public ResourceGroupNodeViewModel(ResourceTreeViewModel owner, Guid? id, string name, GroupNodeKind kind)
        : base(owner, name)
    {
        Id = id;
        Kind = kind;

        Cameras.CollectionChanged += OnCamerasChanged;
    }

    public static string GetViewKey(Guid? id, GroupNodeKind kind)
    {
        return id?.ToString() ?? $"special:{kind}";
    }

    private void OnCamerasChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Track the current cameras rather than the event items, so a reset cannot leave stale subscriptions.
        foreach (var camera in _observedCameras.Except(Cameras).ToList())
        {
            camera.PropertyChanged -= OnCameraPropertyChanged;

            _observedCameras.Remove(camera);
        }

        foreach (var camera in Cameras)
        {
            if (_observedCameras.Add(camera))
            {
                camera.PropertyChanged += OnCameraPropertyChanged;
            }
        }

        UpdateHasUnavailableCamera();
    }

    private void OnCameraPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraNodeViewModel.IsUnavailable))
        {
            UpdateHasUnavailableCamera();
        }
    }

    private void UpdateHasUnavailableCamera()
    {
        HasUnavailableCamera = Cameras.Any(camera => camera.IsUnavailable);
    }
}
