using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// NET_DVR_TIME - date and time in the device's local time.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrTime
{
    public uint Year;
    public uint Month;
    public uint Day;
    public uint Hour;
    public uint Minute;
    public uint Second;

    public readonly DateTime ToDateTime() => new((int)Year, (int)Month, (int)Day,
        (int)Hour, (int)Minute, (int)Second, DateTimeKind.Unspecified);

    public static NetDvrTime From(DateTime value)
    {
        return new NetDvrTime
        {
            Year = (uint)value.Year,
            Month = (uint)value.Month,
            Day = (uint)value.Day,
            Hour = (uint)value.Hour,
            Minute = (uint)value.Minute,
            Second = (uint)value.Second
        };
    }
}
