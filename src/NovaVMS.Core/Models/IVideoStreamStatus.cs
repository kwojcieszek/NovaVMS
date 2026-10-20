namespace NovaVMS.Core.Models;

/// <summary>Optional failure details from a native video source, without credentials or stream contents.</summary>
public interface IVideoStreamStatus
{
    string? ErrorMessage { get; }
}
