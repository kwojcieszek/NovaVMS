using System.Globalization;

namespace NovaVMS.Core.Models;

/// <summary>Keeps a camera's display proportions across stream switches and reconnects.</summary>
public sealed class VideoDisplayGeometry
{
    public string? AspectRatio { get; private set; }

    public void Reset(string? preferredAspectRatio = null)
    {
        AspectRatio = null;
        Learn(preferredAspectRatio);
    }

    public void Learn(string? aspectRatio)
    {
        if (AspectRatio is not null || aspectRatio is null) return;
        var parts = aspectRatio.Split(':');
        if (parts.Length == 2 && uint.TryParse(parts[0], out var width) && uint.TryParse(parts[1], out var height))
            AspectRatio = FromDimensions(width, height);
    }

    public static string? FromDimensions(uint width, uint height, uint sarNumerator = 1, uint sarDenominator = 1)
    {
        if (width == 0 || height == 0) return null;
        if (sarNumerator == 0 || sarDenominator == 0) sarNumerator = sarDenominator = 1;
        var numerator = (ulong)width * sarNumerator;
        var denominator = (ulong)height * sarDenominator;
        var a = numerator;
        var b = denominator;
        while (b != 0) (a, b) = (b, a % b);
        return string.Create(CultureInfo.InvariantCulture, $"{numerator / a}:{denominator / a}");
    }
}
