namespace NovaVMS.Core.Models;

public sealed class SavedCameraView
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public GroupLiveView Layout { get; set; } = new();
    public int? MaximizedTileIndex { get; set; }

    public SavedCameraView Copy()
    {
        return new() { Id = Id, Name = Name, Layout = Layout.Copy(), MaximizedTileIndex = MaximizedTileIndex };
    }
}
