namespace NovaVMS.Core.Models;

public sealed class GroupLiveView
{
    public string? LayoutId { get; set; }
    public int LayoutCount { get; set; } = 4;
    public List<CameraReference?> Cameras { get; set; } = new();
    public HashSet<int> EmptySlots { get; set; } = new();
    public GroupLiveView Copy() => new() { LayoutId = LayoutId, LayoutCount = LayoutCount,
        Cameras = Cameras.ToList(), EmptySlots = EmptySlots.ToHashSet() };
}

