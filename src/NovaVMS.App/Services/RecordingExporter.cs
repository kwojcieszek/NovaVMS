using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using LibVLCSharp.Shared;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>Independent player writes a bounded MP4 without interrupting the displayed recording.</summary>
public sealed class RecordingExporter([Microsoft.Extensions.DependencyInjection.FromKeyedServices(VideoPlayerFactory.Recordings)] LibVLC libVlc)
{
    internal static string CameraDirectoryName(string cameraName)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var name = new string(cameraName.Where(character => !invalid.Contains(character) && !char.IsControl(character))
            .Take(80).ToArray()).Trim(' ', '.');
        if (name.Length == 0) return "Camera";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
            name = "_" + name;
        return name;
    }

    internal static string FileName(string cameraName, int channel, DateTime start, DateTime end)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var name = new string(cameraName.Where(character => !invalid.Contains(character) && !char.IsControl(character)).Take(40).ToArray()).Trim(' ', '.');
        if (name.Length == 0) name = "Camera";
        return $"NovaVMS_{name}_CH{channel}_{start:yyyyMMdd_HHmmss}-{end:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.mp4";
    }

    public Task ExportAsync(VideoSource source, string username, string password, TimeSpan duration,
        string destination, IProgress<double>? progress, CancellationToken token)
        => Task.Run(async () =>
        {
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            token.ThrowIfCancellationRequested();
            // The completed file appears only after the MP4 index has been finalized.
            var partial = destination + $".{Guid.NewGuid():N}.partial";
            try
            {
                using var media = VideoMedia.Create(libVlc, source, username, password, 300, paceStream: true);
                media.Media.AddOption(":sout=" + BuildOutput(partial));
                media.Media.AddOption(":run-time=" + duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                media.Media.AddOption(":no-sout-all");
                using var player = VideoPlayerFactory.Create(libVlc);
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                player.EndReached += (_, _) => completion.TrySetResult(true);
                player.EncounteredError += (_, _) => completion.TrySetException(new IOException(media.ErrorMessage ?? "Recording export failed."));
                try
                {
                    if (!player.Play(media.Media)) throw new IOException("Recording export could not start.");
                    var lastPosition = -1L;
                    var lastProgress = Stopwatch.GetTimestamp();
                    while (!completion.Task.IsCompleted)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.WhenAny(completion.Task, Task.Delay(250, token)).ConfigureAwait(false);
                        var position = player.Time;
                        if (position > lastPosition)
                        {
                            lastPosition = position;
                            lastProgress = Stopwatch.GetTimestamp();
                            progress?.Report(Math.Clamp(position / duration.TotalMilliseconds, 0, .99));
                        }
                        if (position >= duration.TotalMilliseconds) break;
                        if (Stopwatch.GetElapsedTime(lastProgress) > TimeSpan.FromSeconds(45))
                            throw new IOException("The recorder stopped sending export data.");
                    }
                    if (completion.Task.IsCompleted) await completion.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                finally
                {
                    try { media.StopTransfer(); }
                    finally { player.Stop(); }
                }
                if (media.ErrorMessage is { } error) throw new IOException(error);
                if (!File.Exists(partial) || new FileInfo(partial).Length < 128)
                    throw new IOException("The recording export is empty.");
                // Fast inputs can finish between TimeChanged notifications. Validate the actual
                // finalized container, not the last UI/progress sample, before publishing it.
                var actual = ReadMp4Duration(partial);
                var tolerance = Math.Min(1500, duration.TotalMilliseconds * .1);
                if (actual.TotalMilliseconds < duration.TotalMilliseconds - tolerance)
                    throw new IOException("The recording ended before the selected export range was received.");
                token.ThrowIfCancellationRequested();
                File.Move(partial, destination); // Never replace an existing recording.
                progress?.Report(1);
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
        }, token);

    internal static string BuildOutput(string path)
    {
        var escaped = path.Replace('\\', '/').Replace("'", "\\'");
        // Also accepts decoded/encrypted SDK streams (raw video cannot simply be remuxed into MP4).
        var encoder = OperatingSystem.IsWindows()
            ? "x26410b{profile=high10,preset=veryfast,crf=20}"
            : "x264{preset=veryfast,crf=20}";
        return $"#transcode{{vcodec=h264,venc={encoder},fps=25,acodec=mp4a,ab=128}}:std{{access=file,mux=mp4,dst='{escaped}'}}";
    }

    // Reading the finalized index avoids launching another native parser while the export
    // player's event callbacks are still unwinding.
    internal static TimeSpan ReadMp4Duration(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadBoxes(stream, stream.Length);
    }

    private static TimeSpan ReadBoxes(Stream stream, long end)
    {
        Span<byte> header = stackalloc byte[8];
        Span<byte> data = stackalloc byte[32];
        while (stream.Position + 8 <= end)
        {
            var start = stream.Position;
            stream.ReadExactly(header);
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(header);
            if (size == 1)
            {
                stream.ReadExactly(data[..8]);
                var extended = BinaryPrimitives.ReadUInt64BigEndian(data);
                if (extended > long.MaxValue) throw new InvalidDataException("Invalid MP4 box size.");
                size = (long)extended;
            }
            if (size == 0) size = end - start;
            if (size < stream.Position - start || size > end - start) throw new InvalidDataException("Incomplete MP4 file.");
            var boxEnd = start + size;
            if (header[4..].SequenceEqual("moov"u8)) return ReadBoxes(stream, boxEnd);
            if (header[4..].SequenceEqual("mvhd"u8))
            {
                var length = (int)Math.Min(32, boxEnd - stream.Position);
                stream.ReadExactly(data[..length]);
                if (length < 20 || data[0] > 1 || (data[0] == 1 && length < 32)) break;
                var scale = BinaryPrimitives.ReadUInt32BigEndian(data[(data[0] == 1 ? 20 : 12)..]);
                var duration = data[0] == 1 ? BinaryPrimitives.ReadUInt64BigEndian(data[24..]) : BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
                if (scale == 0) break;
                return TimeSpan.FromSeconds((double)duration / scale);
            }
            stream.Position = boxEnd;
        }
        throw new InvalidDataException("The MP4 recording has no duration index.");
    }
}
