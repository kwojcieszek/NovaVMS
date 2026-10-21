using LibVLCSharp.Shared;

namespace NovaVMS.App.Services;

internal static class VideoPlayerFactory
{
    internal const string Recordings = "recordings";

    // VLC 3's video output inherits frame dropping from the player/instance,
    // not media options. SDK recordings can start with timestamps behind the
    // initial clock; dropping then leaves only their first picture visible.
    // A separate instance preserves low-latency dropping for live cameras.
    public static LibVLC CreateRecordingEngine(params string[] options) => new(
        new[] { "--no-osd", "--no-snapshot-preview", "--no-video-title-show",
            "--no-avcodec-hurry-up", "--no-drop-late-frames", "--no-skip-frames" }
        .Concat(options).ToArray());

    // LibVLC 3's D3D11 decoder crashed with 0xc0000005 on recording Stop in
    // release testing. A native access violation bypasses managed recovery.
    // LibVLCSharp applies this per player on Play, overriding LibVLC arguments.
    public static bool UseHardwareDecoding => !OperatingSystem.IsWindows();

    public static MediaPlayer Create(LibVLC libVlc) => new(libVlc)
    {
        EnableHardwareDecoding = UseHardwareDecoding
    };
}
