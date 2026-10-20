using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Controls;

/// <summary>
/// 24-hour timeline with recorded segments. Clicking or dragging picks the playback moment;
/// the part of the day outside the searched range is shaded.
/// </summary>
public sealed class TimelineBar : Control
{
    public static readonly StyledProperty<IEnumerable<RecordingSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<TimelineBar, IEnumerable<RecordingSegment>?>(nameof(Segments));

    public static readonly StyledProperty<DateTime> DayProperty =
        AvaloniaProperty.Register<TimelineBar, DateTime>(nameof(Day), DateTime.Today);

    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<TimelineBar, double>(nameof(Position), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> RangeStartProperty =
        AvaloniaProperty.Register<TimelineBar, double>(nameof(RangeStart));

    public static readonly StyledProperty<double> RangeEndProperty =
        AvaloniaProperty.Register<TimelineBar, double>(nameof(RangeEnd), SecondsPerDay);

    public static readonly StyledProperty<bool> IsInteractingProperty =
        AvaloniaProperty.Register<TimelineBar, bool>(nameof(IsInteracting), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<TimelineBar, ICommand?>(nameof(SeekCommand));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(TrackBrush), Brushes.Black);

    public static readonly StyledProperty<IBrush?> SegmentBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(SegmentBrush), Brushes.SteelBlue);

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(TickBrush), Brushes.DimGray);

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(LabelBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> CursorBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(CursorBrush), Brushes.Red);

    public static readonly StyledProperty<IBrush?> OutOfRangeBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(OutOfRangeBrush));

    private const double SecondsPerDay = 86400;
    private const double HoursPerDay = 24;
    private const double LabelAreaHeight = 16;
    private const double LabelFontSize = 10;
    private const double NarrowWidth = 700;
    private const double CursorThickness = 2;

    private static readonly Typeface LabelTypeface = new(new FontFamily("fonts:Inter#Inter, $Default"));

    static TimelineBar()
    {
        AffectsRender<TimelineBar>(
            SegmentsProperty,
            DayProperty,
            PositionProperty,
            RangeStartProperty,
            RangeEndProperty,
            TrackBrushProperty,
            SegmentBrushProperty,
            TickBrushProperty,
            LabelBrushProperty,
            CursorBrushProperty,
            OutOfRangeBrushProperty);
        ClipToBoundsProperty.OverrideDefaultValue<TimelineBar>(true);
    }

    public IEnumerable<RecordingSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public DateTime Day
    {
        get => GetValue(DayProperty);
        set => SetValue(DayProperty, value);
    }

    /// <summary>
    /// Cursor position in seconds from midnight.
    /// </summary>
    public double Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>
    /// Start of the searched range in seconds from midnight.
    /// </summary>
    public double RangeStart
    {
        get => GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    /// <summary>
    /// End of the searched range in seconds from midnight.
    /// </summary>
    public double RangeEnd
    {
        get => GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    public bool IsInteracting
    {
        get => GetValue(IsInteractingProperty);
        set => SetValue(IsInteractingProperty, value);
    }

    /// <summary>
    /// Executed when the mouse button is released; the parameter is the position in seconds.
    /// </summary>
    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? SegmentBrush
    {
        get => GetValue(SegmentBrushProperty);
        set => SetValue(SegmentBrushProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? CursorBrush
    {
        get => GetValue(CursorBrushProperty);
        set => SetValue(CursorBrushProperty, value);
    }

    public IBrush? OutOfRangeBrush
    {
        get => GetValue(OutOfRangeBrushProperty);
        set => SetValue(OutOfRangeBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (TrackBrush is not null)
        {
            context.FillRectangle(TrackBrush, new Rect(0, 0, width, height));
        }

        RenderHourMarks(context, width, height);
        RenderSegments(context, width, height);
        RenderOutOfRange(context, width, height);

        if (CursorBrush is not null)
        {
            var cursorX = ToX(Position, width);
            context.DrawLine(new Pen(CursorBrush, CursorThickness), new Point(cursorX, 0), new Point(cursorX, height));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != SegmentsProperty)
        {
            return;
        }

        if (change.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnSegmentsCollectionChanged;
        }

        if (change.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += OnSegmentsCollectionChanged;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(this);
        IsInteracting = true;
        UpdatePosition(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (IsInteracting)
        {
            UpdatePosition(e.GetPosition(this).X);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!IsInteracting)
        {
            return;
        }

        UpdatePosition(e.GetPosition(this).X);
        IsInteracting = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        var command = SeekCommand;
        if (command is not null && command.CanExecute(Position))
        {
            command.Execute(Position);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        IsInteracting = false;
    }

    private static double ToX(double seconds, double width)
    {
        return Math.Clamp(seconds / SecondsPerDay, 0, 1) * width;
    }

    private void OnSegmentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void UpdatePosition(double x)
    {
        var width = Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        Position = Math.Clamp(x / width, 0, 1) * SecondsPerDay;
    }

    private void RenderSegments(DrawingContext context, double width, double height)
    {
        var segments = Segments;
        var brush = SegmentBrush;
        if (segments is null || brush is null)
        {
            return;
        }

        var dayStart = Day.Date;
        var dayEnd = dayStart.AddDays(1);
        var top = LabelAreaHeight + 4;
        var barHeight = Math.Max(0, height - top - 4);

        foreach (var segment in segments)
        {
            var start = segment.Start < dayStart ? dayStart : segment.Start;
            var end = segment.End > dayEnd ? dayEnd : segment.End;
            if (end <= start)
            {
                continue;
            }

            var left = ToX((start - dayStart).TotalSeconds, width);
            var right = ToX((end - dayStart).TotalSeconds, width);
            context.FillRectangle(brush, new Rect(left, top, Math.Max(1, right - left), barHeight));
        }
    }

    private void RenderOutOfRange(DrawingContext context, double width, double height)
    {
        var brush = OutOfRangeBrush;
        if (brush is null)
        {
            return;
        }

        var rangeStartX = ToX(RangeStart, width);
        var rangeEndX = ToX(RangeEnd, width);

        if (rangeStartX > 0)
        {
            context.FillRectangle(brush, new Rect(0, 0, rangeStartX, height));
        }

        if (rangeEndX < width)
        {
            context.FillRectangle(brush, new Rect(rangeEndX, 0, width - rangeEndX, height));
        }
    }

    private void RenderHourMarks(DrawingContext context, double width, double height)
    {
        var tickBrush = TickBrush;
        var labelBrush = LabelBrush;
        var tickPen = tickBrush is null ? null : new Pen(tickBrush);
        var labelStep = width < NarrowWidth ? 4 : 2;

        for (var hour = 0; hour <= HoursPerDay; hour++)
        {
            var x = hour / HoursPerDay * width;
            if (tickPen is not null)
            {
                context.DrawLine(tickPen, new Point(x, LabelAreaHeight - 4), new Point(x, height));
            }

            if (labelBrush is null || hour % labelStep != 0 || hour == HoursPerDay)
            {
                continue;
            }

            var label = new FormattedText($"{hour:00}:00", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, LabelFontSize, labelBrush);
            context.DrawText(label, new Point(x + 3, 1));
        }
    }
}
