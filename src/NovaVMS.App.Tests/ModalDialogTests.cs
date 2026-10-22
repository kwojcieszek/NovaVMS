using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NovaVMS.App.Services;
using NovaVMS.App.Views;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class ModalDialogTests
{
    [AvaloniaFact]
    public async Task ConfirmationDefersInputAndKeepsVideoOverlayHiddenUntilEitherAnswer()
    {
        var tile = new VideoTileView();
        // Keep the real overlay window lifecycle without native VLC surfaces.
        tile.FindControl<Panel>("VideoHost")!.Children.Clear();
        var owner = new Window { Width = 640, Height = 480, Content = tile };
        try
        {
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            var overlay = Assert.IsType<VideoOverlayWindow>(Assert.Single(owner.OwnedWindows));
            Assert.True(overlay.IsVisible);
            foreach (var answer in new[] { true, false, true, false })
            {
                var dialog = new ConfirmDialog("Layout", "Change the layout?");
                var result = ModalDialog.ShowAsync<bool>(dialog, owner);
                // The tree/selector input handler can finish before the window opens.
                Assert.False(dialog.IsVisible);
                Dispatcher.UIThread.RunJobs();
                Assert.True(dialog.IsVisible);
                Assert.Same(owner, dialog.Owner);
                Assert.False(overlay.IsVisible);

                // Layout changes used to move or re-show the transparent surface over the dialog.
                owner.Width += 10;
                Dispatcher.UIThread.RunJobs();
                Assert.False(overlay.IsVisible);
                var button = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(candidate => answer ? candidate.IsDefault : candidate.IsCancel);
                Assert.True(button.IsEffectivelyEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(answer, await result);
                Dispatcher.UIThread.RunJobs();
                Assert.True(overlay.IsVisible);
                Assert.Same(owner, overlay.Owner);
                Assert.Equal(0, owner.GetValue(ModalDialog.DepthProperty));
            }
        }
        finally { owner.Content = null; owner.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task NestedDialogKeepsMainVideoSuppressedUntilParentCloses()
    {
        var owner = new Window();
        var settings = new Window();
        var dialog = new Window();
        try
        {
            owner.Show();
            var settingsResult = ModalDialog.ShowAsync<object?>(settings, owner);
            Dispatcher.UIThread.RunJobs();
            var answer = ModalDialog.ShowAsync<bool>(dialog, settings);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, owner.GetValue(ModalDialog.DepthProperty));
            Assert.Equal(1, settings.GetValue(ModalDialog.DepthProperty));
            dialog.Close(false);
            Assert.False(await answer);
            Assert.Equal(1, owner.GetValue(ModalDialog.DepthProperty));
            settings.Close();
            await settingsResult;
            Assert.Equal(0, owner.GetValue(ModalDialog.DepthProperty));
        }
        finally { dialog.Close(); settings.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public void InputOverlayCannotBecomeDialogOwner()
    {
        var main = new Window();
        var overlay = new VideoOverlayWindow();
        try
        {
            main.Show();
            overlay.Show(main);
            overlay.Activate();
            Assert.Same(main, DialogService.FindOwner([overlay, main], main));
        }
        finally { overlay.Close(); main.Close(); }
    }
}
