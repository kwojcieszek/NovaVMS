using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class SavedViewChangesTests
{
    [Fact]
    public void CameraLayoutAndMaximizationChangesUpdateOnlyTargetView()
    {
        var view = new SavedCameraView { Name = "Original" };
        var other = new SavedCameraView { Name = "Other" };
        var camera = new CameraReference { DeviceId = Guid.NewGuid(), ChannelNumber = 3 };
        var layout = new GroupLiveView { LayoutId = "standard-9", LayoutCount = 9, Cameras = [null, camera], EmptySlots = [0] };
        Assert.True(SavedViewChanges.Apply(view, layout, 1));
        layout.Cameras.Clear();
        layout.EmptySlots.Clear();
        Assert.Equal("Original", view.Name);
        Assert.Equal("standard-9", view.Layout.LayoutId);
        Assert.Equal(new CameraReference?[] { null, camera }, view.Layout.Cameras);
        Assert.Contains(0, view.Layout.EmptySlots);
        Assert.Equal(1, view.MaximizedTileIndex);
        Assert.Empty(other.Layout.Cameras);
        Assert.Null(other.MaximizedTileIndex);
    }

    [Fact]
    public void RepeatedNotificationsDoNotRequestAnotherSave()
    {
        var view = new SavedCameraView { Layout = new GroupLiveView { LayoutId = "standard-4", LayoutCount = 4,
            Cameras = [null, null], EmptySlots = [0, 1] } };
        var identical = view.Layout.Copy();
        identical.EmptySlots = [1, 0];
        Assert.False(SavedViewChanges.Apply(view, identical, null));
        Assert.True(SavedViewChanges.Apply(view, identical, 0));
        Assert.False(SavedViewChanges.Apply(view, identical, 0));
        Assert.True(SavedViewChanges.Apply(view, identical, null));
    }

    [Fact]
    public void ClosingCameraAndChangingGridVariantAreSaved()
    {
        var camera = new CameraReference { DeviceId = Guid.NewGuid(), ChannelNumber = 1 };
        var view = new SavedCameraView { Layout = new GroupLiveView { LayoutId = "standard-9", LayoutCount = 9, Cameras = [camera] } };
        var edited = view.Layout.Copy();
        edited.Cameras[0] = null;
        edited.EmptySlots.Add(0);
        Assert.True(SavedViewChanges.Apply(view, edited, null));
        edited.LayoutId = "wide-9";
        Assert.True(SavedViewChanges.Apply(view, edited, null));
        Assert.Equal("wide-9", view.Layout.LayoutId);
        Assert.Null(view.Layout.Cameras[0]);
        Assert.Contains(0, view.Layout.EmptySlots);
    }
}
