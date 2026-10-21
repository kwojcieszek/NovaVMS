using LibVLCSharp.Shared;

namespace NovaVMS.App.Services;

/// <summary>Lets LibVLC pace recorded callback data using its media clock.</summary>
internal sealed class RecordingMediaInput : StreamMediaInput
{
    private readonly bool _seekable;

    public RecordingMediaInput(Stream stream) : base(stream)
    {
        _seekable = stream.CanSeek;
        // VLC 3's imem-access enables STREAM_CAN_CONTROL_PACE and pause only when a
        // seek callback is supplied. Without it a recorder's fast download is treated
        // as a live clock: frames are dropped and minutes pass in seconds.
        // Keep the callback, but reject byte seeks on SDK streams. The application
        // implements recording seeks by opening a new source at the requested time.
        // https://github.com/videolan/vlc/blob/3.0.x/modules/access/imem-access.c
        CanSeek = true;
    }

    public override bool Seek(ulong offset) => _seekable && base.Seek(offset);
}
