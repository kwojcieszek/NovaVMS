using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class ResourceTreeSelectionTests
{
    [AvaloniaFact]
    public async Task SelectingGroupsAndSavingTheirLayoutsDoesNotMoveScrollbarOrReplaceRows()
    {
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new()
        {
            Groups = Enumerable.Range(1, 80).Select(index => new CameraGroup { Name = $"Group {index}" }).ToList()
        }), new JsonConfigurationSerializer());
        var tree = new ResourceTreeViewModel(configuration, new DeviceSyncService(configuration, null!), null!, TestSessions.Create(configuration));
        await configuration.LoadAsync();
        var control = new TreeView { ItemsSource = tree.Groups, AutoScrollToSelectedItem = false };
        control.DataTemplates.Add(new FuncDataTemplate<ResourceNodeViewModel>((node, _) => new TextBlock { Text = node.Name }));
        control.Bind(TreeView.SelectedItemProperty, new Binding(nameof(ResourceTreeViewModel.SelectedNode))
            { Source = tree, Mode = BindingMode.TwoWay });
        var window = new Window { Width = 300, Height = 240, Content = control };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var scroll = control.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, 450);
            Dispatcher.UIThread.RunJobs();
            var offset = scroll.Offset;
            Assert.True(offset.Y > 0);
            var rows = tree.Groups.ToArray();
            var containers = control.GetVisualDescendants().OfType<TreeViewItem>().ToArray();
            var collectionChanges = 0;
            tree.Groups.CollectionChanged += (_, _) => collectionChanges++;
            foreach (var group in rows.Where(node => node.IsCustom).Skip(15).Take(10))
            {
                tree.SelectedNode = group;
                tree.SetActiveLiveView(group.ViewKey, null);
                configuration.Current.GroupLiveViews[group.ViewKey] = new() { LayoutCount = 9 };
                await configuration.SaveAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(offset, scroll.Offset);
                Assert.Same(group, tree.SelectedNode);
                Assert.Equal(rows, tree.Groups);
                Assert.Equal(containers, control.GetVisualDescendants().OfType<TreeViewItem>());
            }
            Assert.Equal(0, collectionChanges);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [Fact]
    public async Task SelectionAndActiveMarkerSurviveSavingRenamingAndFiltering()
    {
        var group = new CameraGroup { Name = "Entrance" };
        var view = new SavedCameraView { Name = "Office" };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Groups = [group], Views = [view] }),
            new JsonConfigurationSerializer());
        var tree = new ResourceTreeViewModel(configuration, new DeviceSyncService(configuration, null!), null!, TestSessions.Create(configuration));
        await configuration.LoadAsync();
        tree.SelectedNode = tree.Groups.Single(node => node.Id == group.Id);
        tree.SetActiveLiveView(group.Id.ToString(), null);
        await configuration.SaveAsync();
        var restoredGroup = tree.Groups.Single(node => node.Id == group.Id);
        Assert.Same(restoredGroup, tree.SelectedNode);
        Assert.True(restoredGroup.IsActive);

        var folder = tree.Groups.Single(node => node.IsViewsFolder);
        tree.SelectedNode = folder.Views.Single();
        tree.SetActiveLiveView(null, view.Id);
        Assert.False(restoredGroup.IsActive);
        configuration.Current.Views.Single().Name = "Office renamed";
        await configuration.SaveAsync();
        var restoredView = tree.Groups.Single(node => node.IsViewsFolder).Views.Single();
        Assert.Same(restoredView, tree.SelectedNode);
        Assert.True(restoredView.IsActive);
        Assert.Equal("Office renamed", restoredView.Name);

        tree.SearchText = "Entrance";
        tree.SearchText = "";
        Assert.True(tree.Groups.Single(node => node.IsViewsFolder).Views.Single().IsActive);
        configuration.Current.Views.Clear();
        await configuration.SaveAsync();
        Assert.DoesNotContain(tree.Groups, node => node.IsActive);
    }
}
