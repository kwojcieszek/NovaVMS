namespace NovaVMS.App.Models;

public sealed record PlaybackSpeedOption
{
    public static IReadOnlyList<PlaybackSpeedOption> All { get; } =
        new[] { -16, -8, -4, -2, 1, 2, 4, 8, 16 }.Select(rate => new PlaybackSpeedOption(rate)).ToArray();

    public int Rate { get; init; }

    public string Label => Rate < 0 ? $"◀ {Math.Abs(Rate)}×" : Rate == 1 ? "▶ 1×" : $"▶▶ {Rate}×";

    public PlaybackSpeedOption(int rate)
    {
        Rate = rate;
    }
}
