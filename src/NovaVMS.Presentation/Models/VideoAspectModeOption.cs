using NovaVMS.Core.Models;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of the picture proportions list in the settings.
/// </summary>
public sealed class VideoAspectModeOption : LocalizedOption<VideoAspectMode>
{
    public static IReadOnlyList<VideoAspectModeOption> All { get; } =
    [
        new(VideoAspectMode.Original, "Aspect_Original"),
        new(VideoAspectMode.Fit, "Aspect_Fit")
    ];

    private VideoAspectModeOption(VideoAspectMode value, string labelKey)
        : base(value, labelKey)
    {
    }

    public static VideoAspectModeOption Find(VideoAspectMode value)
    {
        return Find(All, value);
    }
}
