using System.Diagnostics;
using LibVLCSharp.Shared;

namespace NovaVMS.App.Services;

/// <summary>Prepares recording seeks behind the last displayed picture.</summary>
internal sealed class RecordingPlaybackBuffer : IAsyncDisposable
{
    private sealed class Slot(MediaPlayer player)
    {
        internal MediaPlayer Player { get; } = player;
        internal VideoMedia? Media { get; set; }
    }

    private readonly AsyncWorkQueue _operations = new();
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly Slot _primary;
    private readonly Slot _secondary;
    private Slot _active;
    private int _version;
    private bool _paused;
    private bool _disposed;

    internal RecordingPlaybackBuffer(LibVLC vlc)
    {
        _primary = new(VideoPlayerFactory.Create(vlc));
        _secondary = new(VideoPlayerFactory.Create(vlc));
        _active = _primary;
    }

    internal MediaPlayer PrimaryPlayer => _primary.Player;
    internal MediaPlayer SecondaryPlayer => _secondary.Player;
    internal MediaPlayer ActivePlayer => _active.Player;
    internal VideoMedia? ActiveMedia => _active.Media;
    internal Task WhenIdle => _operations.WhenIdle;
    internal event EventHandler? ActivePlayerChanged;

    internal void CancelPending() => Interlocked.Increment(ref _version);

    internal async Task<bool> SwitchAsync(Func<VideoMedia> createMedia, CancellationToken token)
    {
        var version = Interlocked.Increment(ref _version);
        await _switchGate.WaitAsync(token);
        Slot? target = null;
        Slot? previous = null;
        var promoted = false;
        bool Current() => !_disposed && version == Volatile.Read(ref _version) && !token.IsCancellationRequested;
        try
        {
            if (!Current()) return false;
            previous = _active;
            target = previous.Media is null ? previous : ReferenceEquals(previous, _primary) ? _secondary : _primary;
            await StopSlotAsync(target);
            // Pause keeps the outgoing frame on its native surface while the next source buffers.
            if (previous.Media is not null) await _operations.RunAsync(() => previous.Player.SetPause(true));
            var media = await _operations.RunAsync(createMedia);
            if (!Current()) { await _operations.RunAsync(media.Dispose); return false; }
            target.Media = media;
            var started = await _operations.RunAsync(() =>
            {
                if (!Current()) return false;
                target.Player.Mute = true;
                return target.Player.Play(media.Media);
            });
            if (!Current()) return false;
            if (!started) throw new IOException("Recording playback could not start.");

            var timeout = Stopwatch.StartNew();
            while (media.Media.Statistics.DisplayedPictures == 0)
            {
                if (!Current()) return false;
                if (target.Player.State is VLCState.Error or VLCState.Ended || timeout.Elapsed > TimeSpan.FromSeconds(15))
                    throw new IOException(media.ErrorMessage ?? "The recording did not produce a video frame.");
                await Task.Delay(40, token);
            }
            if (!Current()) return false;

            _active = target;
            promoted = true;
            ActivePlayerChanged?.Invoke(this, EventArgs.Empty);
            await _operations.RunAsync(() => target.Player.SetPause(_paused));
            // Native stacking and painting must finish before the old surface is cleared.
            await Task.Delay(150, token);
            if (!ReferenceEquals(previous, target)) await StopSlotAsync(previous);
            if (Current()) await _operations.RunAsync(() => target.Player.Mute = false);
            return Current();
        }
        finally
        {
            try
            {
                if (target is not null && (!promoted || !ReferenceEquals(target, _active))) await StopSlotAsync(target);
                if (previous is not null && !ReferenceEquals(previous, _active)) await StopSlotAsync(previous);
                if (_active.Media is not null) await _operations.RunAsync(() => _active.Player.SetPause(_paused));
            }
            finally { _switchGate.Release(); }
        }
    }

    internal Task SetPauseAsync(bool paused)
    {
        _paused = paused;
        var player = ActivePlayer;
        return _operations.RunAsync(() => player.SetPause(paused));
    }

    internal void Stop()
    {
        Release();
        _paused = false;
    }

    /// <summary>Stops every recording session but keeps the pause state for the next switch.</summary>
    internal void Release()
    {
        CancelPending();
        _ = StopSlotAsync(_primary);
        _ = StopSlotAsync(_secondary);
    }

    private Task StopSlotAsync(Slot slot)
    {
        var media = slot.Media;
        slot.Media = null;
        return media is null ? Task.CompletedTask : _operations.RunAsync(() => VideoMedia.Stop(slot.Player, media));
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Stop();
        await _switchGate.WaitAsync();
        try
        {
            await _operations.WhenIdle;
            await _operations.RunAsync(() => { PrimaryPlayer.Dispose(); SecondaryPlayer.Dispose(); });
        }
        finally { _switchGate.Release(); }
    }
}
