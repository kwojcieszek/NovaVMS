using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// NET_DVR_JPEGPARA - picture size and quality of a snapshot.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrJpegParameters
{
    /// <summary>
    /// 0xFF selects the current resolution of the stream.
    /// </summary>
    public ushort PictureSize;

    /// <summary>
    /// 0 = best, 1 = good, 2 = normal.
    /// </summary>
    public ushort PictureQuality;
}
