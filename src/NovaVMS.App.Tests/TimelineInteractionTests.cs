using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Controls;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class TimelineInteractionTests
{
    [AvaloniaFact]
    public void ZoomKeepsTheTimeUnderTheMouseAndChangingDayRestoresTheWholeDay()
    {
        var timeline = new TimelineBar();
        var window = new Window { Width = 800, Height = 60, Content = timeline };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var center = new Point(timeline.Bounds.Width / 2, 30);
            window.MouseWheel(center, new Vector(0, 1));
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Assert.Equal(43200, timeline.Position);
            var quarter = new Point(timeline.Bounds.Width / 4, 30);
            window.MouseDown(quarter, MouseButton.Left);
            window.MouseUp(quarter, MouseButton.Left);
            Assert.Equal(32400, timeline.Position);
            timeline.Day = timeline.Day.AddDays(-1);
            window.MouseDown(quarter, MouseButton.Left);
            window.MouseUp(quarter, MouseButton.Left);
            Assert.Equal(21600, timeline.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RepeatedDragsKeepPositionAndInteractionBindingsAndShowTheChosenTime()
    {
        var state = new TimelineState();
        var requested = new List<double>();
        var timeline = new TimelineBar { SeekCommand = new RelayCommand<double>(requested.Add) };
        timeline.Bind(TimelineBar.PositionProperty, new Binding(nameof(TimelineState.Position)) { Source = state, Mode = BindingMode.TwoWay });
        timeline.Bind(TimelineBar.IsInteractingProperty, new Binding(nameof(TimelineState.Interacting)) { Source = state, Mode = BindingMode.TwoWay });
        var window = new Window { Width = 800, Height = 60, Content = timeline };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            for (var index = 1; index <= 3; index++)
            {
                var x = timeline.Bounds.Width * index / 4;
                window.MouseDown(new Point(x, 30), MouseButton.Left);
                Assert.True(state.Interacting);
                window.MouseMove(new Point(x + 10, 30));
                window.MouseUp(new Point(x + 10, 30), MouseButton.Left);
                Assert.False(state.Interacting);
                Assert.Equal(timeline.Position, state.Position);
                Assert.Equal(state.Position, requested[^1]);
                state.Position = index * 1000;
                Assert.Equal(state.Position, timeline.Position);
            }
            Assert.Equal(3, requested.Count);
        }
        finally { window.Close(); }
    }

    private sealed class TimelineState : ObservableObject
    {
        private double _position;
        private bool _interacting;
        public double Position { get => _position; set => SetProperty(ref _position, value); }
        public bool Interacting { get => _interacting; set => SetProperty(ref _interacting, value); }
    }
}
