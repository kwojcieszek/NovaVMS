using LibVLCSharp.Shared;
using System.Diagnostics;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class RecordingPlaybackBufferTests
{
    [Fact]
    public async Task SlowSeekKeepsLastFrameAndSwitchesOnlyAfterTheIncomingFrameIsDisplayed()
    {
        using var vlc = Engine();
        await using var buffer = new RecordingPlaybackBuffer(vlc);
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var first = new MemoryStream(bytes);
        Assert.True(await buffer.SwitchAsync(() => Media(vlc, first), CancellationToken.None));
        var outgoing = buffer.ActivePlayer;
        var picture = buffer.ActiveMedia;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = 0;
        outgoing.Stopped += (_, _) => Interlocked.Increment(ref stopped);
        var incoming = new MemoryStream(bytes);
        var readyAtSwitch = false;
        var oldFrameAtSwitch = false;
        buffer.ActivePlayerChanged += (_, _) =>
        {
            readyAtSwitch = buffer.ActiveMedia!.Media.Statistics.DisplayedPictures > 0;
            oldFrameAtSwitch = first.CanRead && outgoing.State == VLCState.Paused;
        };
        var seek = buffer.SwitchAsync(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Media(vlc, incoming);
        }, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(300);
            Assert.Same(outgoing, buffer.ActivePlayer);
            Assert.Same(picture, buffer.ActiveMedia);
            Assert.True(picture!.Media.Statistics.DisplayedPictures > 0);
            Assert.Equal(VLCState.Paused, outgoing.State);
            Assert.True(first.CanRead);
            Assert.Equal(0, stopped);
            release.Set();
            Assert.True(await seek.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(readyAtSwitch);
            Assert.True(oldFrameAtSwitch);
            Assert.NotSame(outgoing, buffer.ActivePlayer);
            Assert.False(first.CanRead);
            Assert.True(incoming.CanRead);
        }
        finally { release.Set(); await seek; }
    }

    [Fact]
    public async Task FailedSeekLeavesWorkingPictureAndAllowsAnotherSeek()
    {
        using var vlc = Engine();
        await using var buffer = new RecordingPlaybackBuffer(vlc);
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var stream = new MemoryStream(bytes);
        Assert.True(await buffer.SwitchAsync(() => Media(vlc, stream), CancellationToken.None));
        var player = buffer.ActivePlayer;
        var media = buffer.ActiveMedia;
        await Assert.ThrowsAsync<IOException>(() => buffer.SwitchAsync(() => throw new IOException("Source unavailable"), CancellationToken.None));
        Assert.Same(player, buffer.ActivePlayer);
        Assert.Same(media, buffer.ActiveMedia);
        Assert.True(stream.CanRead);
        await WaitForAsync(() => player.State == VLCState.Playing);
        Assert.True(await buffer.SwitchAsync(() => Media(vlc, new MemoryStream(bytes)), CancellationToken.None));
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task StopDuringOpeningCannotRestoreStalePlayback()
    {
        using var vlc = Engine();
        await using var buffer = new RecordingPlaybackBuffer(vlc);
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var first = new MemoryStream(bytes);
        Assert.True(await buffer.SwitchAsync(() => Media(vlc, first), CancellationToken.None));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new MemoryStream(bytes);
        var opening = buffer.SwitchAsync(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Media(vlc, pending);
        }, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            buffer.Stop();
            release.Set();
            Assert.False(await opening.WaitAsync(TimeSpan.FromSeconds(5)));
            await buffer.WhenIdle;
            Assert.Null(buffer.ActiveMedia);
            Assert.False(buffer.PrimaryPlayer.IsPlaying);
            Assert.False(buffer.SecondaryPlayer.IsPlaying);
            Assert.False(first.CanRead);
            Assert.False(pending.CanRead);
        }
        finally { release.Set(); await opening; }
    }

    [Fact]
    public async Task RepeatedSeeksAlternatePlayersAndPreservePause()
    {
        using var vlc = Engine();
        await using var buffer = new RecordingPlaybackBuffer(vlc);
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        Assert.True(await buffer.SwitchAsync(() => Media(vlc, new MemoryStream(bytes)), CancellationToken.None));
        await buffer.SetPauseAsync(true);
        for (var index = 0; index < 4; index++)
        {
            var previous = buffer.ActivePlayer;
            Assert.True(await buffer.SwitchAsync(() => Media(vlc, new MemoryStream(bytes)), CancellationToken.None));
            Assert.NotSame(previous, buffer.ActivePlayer);
            Assert.True(buffer.ActiveMedia!.Media.Statistics.DisplayedPictures > 0);
            Assert.Equal(VLCState.Paused, buffer.ActivePlayer.State);
            Assert.False(previous.IsPlaying);
        }
        await buffer.SetPauseAsync(false);
        await WaitForAsync(() => buffer.ActivePlayer.State == VLCState.Playing);
    }

    private static async Task WaitForAsync(Func<bool> ready)
    {
        var timeout = Stopwatch.StartNew();
        while (!ready() && timeout.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
        Assert.True(ready());
    }

    private static LibVLC Engine() => VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");

    private static VideoMedia Media(LibVLC vlc, Stream stream) =>
        VideoMedia.Create(vlc, VideoSource.FromStream(() => stream), "", "", 0, withAudio: false, paceStream: true);
}
