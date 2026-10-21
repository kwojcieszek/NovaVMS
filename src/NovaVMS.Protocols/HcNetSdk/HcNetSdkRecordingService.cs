using System.Diagnostics;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>Searches the recorder over its SDK connection; no HTTP or RTSP service is required.</summary>
internal sealed class HcNetSdkRecordingService(IHcNetSdkRecordingApi api)
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(60);

    public Task<IReadOnlyList<RecordingSegment>> SearchAsync(HcNetSdkSession session, int channelNumber,
        DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelNumber, 1);
        if (from >= to) throw new ArgumentException("Recording search start must precede its end.", nameof(to));
        // SDK calls can block on network I/O. Keep them off the UI thread.
        return Task.Run<IReadOnlyList<RecordingSegment>>(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = new NetDvrFileCondition
            {
                Channel = session.ToSdkChannel(channelNumber),
                FileType = 0xff, // All recording types, including motion/event recordings.
                IsLocked = 0xff, // Both locked and unlocked files.
                CardNumber = new byte[32],
                Start = NetDvrTime.From(from),
                End = NetDvrTime.From(to)
            };
            var handle = api.Find(session.UserId, ref condition);
            if (handle < 0) throw Error(session);
            try
            {
                var segments = new List<RecordingSegment>();
                var waitingSince = Stopwatch.GetTimestamp();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = api.Next(handle, out var data);
                    switch (status)
                    {
                        case 1000: // NET_DVR_FILE_SUCCESS
                            var start = data.Start.ToDateTime();
                            var end = data.End.ToDateTime();
                            if (start < end && start < to && end > from)
                                segments.Add(new RecordingSegment { ChannelNumber = channelNumber, Start = start, End = end });
                            waitingSince = Stopwatch.GetTimestamp();
                            break;
                        case 1001: // NET_DVR_FILE_NOFIND
                        case 1003: // NET_DVR_NOMOREFILE
                            return segments.DistinctBy(segment => (segment.Start, segment.End))
                                .OrderBy(segment => segment.Start).ToList();
                        case 1002: // NET_DVR_ISFINDING: wait without busy-polling.
                            if (Stopwatch.GetElapsedTime(waitingSince) >= SearchTimeout)
                                throw new DeviceCommunicationException(ProtocolText.Get("Timeout"));
                            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                            break;
                        default:
                            throw Error(session);
                    }
                }
            }
            finally
            {
                api.Close(handle);
            }
        }, cancellationToken);
    }

    private DeviceCommunicationException Error(HcNetSdkSession session)
    {
        var code = api.GetLastError();
        if (HcNetSdkErrors.InvalidatesSession(code)) HcNetSdkSessionPool.Invalidate(session);
        return HcNetSdkErrors.FromCode(code, session.Host, session.Port);
    }
}
