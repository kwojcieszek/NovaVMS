using NovaVMS.Core.Models;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of the stream mode list in the settings.
/// </summary>
public sealed class StreamModeOption : LocalizedOption<GridStreamMode>
{
    public static IReadOnlyList<StreamModeOption> All { get; } =
    [
        new(GridStreamMode.Auto, "Stream_Auto"),
        new(GridStreamMode.Main, "Stream_Main"),
        new(GridStreamMode.Sub, "Stream_Sub")
    ];

    private StreamModeOption(GridStreamMode value, string labelKey)
        : base(value, labelKey)
    {
    }

    public static StreamModeOption Find(GridStreamMode value)
    {
        return Find(All, value);
    }
}
