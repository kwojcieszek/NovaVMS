using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class GroupCameraPlacementTests
{
    private static CameraReference Camera(int channel) => new() { DeviceId = Guid.Empty, ChannelNumber = channel };

    [Fact]
    public void LargerGridGetsNextCamerasInGroupOrderWithoutDuplicates()
    {
        var result = GroupCameraPlacement.Fill([Camera(1), Camera(2), null, null, null],
            [Camera(1), Camera(2), Camera(3), Camera(3), Camera(4)], new HashSet<int>());
        Assert.Equal(new CameraReference?[] { Camera(1), Camera(2), Camera(3), Camera(4), null }, result);
    }

    [Fact]
    public void GroupOrderOverridesSavedPlacementWhileExplicitEmptySlotsArePreserved()
    {
        var result = GroupCameraPlacement.Fill([Camera(3), null, Camera(99), null, null],
            [Camera(1), Camera(2), Camera(3)], new HashSet<int> { 1 });
        Assert.Equal(new CameraReference?[] { Camera(1), null, Camera(2), Camera(3), null }, result);
    }

    [Fact]
    public void EmptyGroupLeavesUnusedSlotsEmpty()
    {
        var result = GroupCameraPlacement.Fill([null, Camera(99), null], [], new HashSet<int>());
        Assert.Equal(new CameraReference?[] { null, null, null }, result);
    }

    [Fact]
    public void SavedPreviewWithOnlyForeignCamerasIsReplacedByGroupMembers()
    {
        var result = GroupCameraPlacement.Fill([Camera(90), Camera(91), Camera(92), Camera(93)],
            [Camera(17), Camera(18), Camera(19), Camera(20)], new HashSet<int>());
        Assert.Equal(new CameraReference?[] { Camera(17), Camera(18), Camera(19), Camera(20) }, result);
    }

    [Fact]
    public void RepeatedSavedCameraDoesNotHideAnotherGroupMember()
    {
        var result = GroupCameraPlacement.Fill([Camera(2), Camera(2), null],
            [Camera(1), Camera(2), Camera(3)], new HashSet<int>());
        Assert.Equal(new CameraReference?[] { Camera(1), Camera(2), Camera(3) }, result);
    }

    [Fact]
    public void ReorderedGroupReplacesExistingPositionsAndUsesFirstMembersWhenGridIsSmaller()
    {
        var result = GroupCameraPlacement.Fill([Camera(1), Camera(2), Camera(3)],
            [Camera(4), Camera(3), Camera(2), Camera(1)], new HashSet<int>());
        Assert.Equal(new CameraReference?[] { Camera(4), Camera(3), Camera(2) }, result);
    }

    [Fact]
    public void PreviouslyUnavailableCameraCanFillAnAutomaticGap()
    {
        var first = GroupCameraPlacement.Fill([null, null, null], [Camera(1)], new HashSet<int>());
        var second = GroupCameraPlacement.Fill(first, [Camera(1), Camera(2)], new HashSet<int>());
        Assert.Equal(new CameraReference?[] { Camera(1), Camera(2), null }, second);
    }
}
