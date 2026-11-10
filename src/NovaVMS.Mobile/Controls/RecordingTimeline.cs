using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Mobile.Controls;

/// <summary>
/// The 24 hours of one day with its recorded segments and the time being played. Hours are marked along the bar;
/// a finger moved over it shows the time under it and lifting the finger runs <see cref="Command"/> with that time.
/// </summary>
public sealed class RecordingTimeline : Control
{
    private const double LabelHeight = 18;
    private const double TrackHeight = 26;
    private const double LabelFontSize = 11;
    private const double MinimumLabelSpacing = 44;
    private const double TrackRadius = 6;

    private static readonly int[] LabelSteps = [1, 2, 3, 4, 6, 12];

    private DateTime? _touchTime;

    public static readonly StyledProperty<DateTime> DayProperty =
        AvaloniaProperty.Register<RecordingTimeline, DateTime>(nameof(Day), DateTime.Today);

    public static readonly StyledProperty<IReadOnlyList<RecordingSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<RecordingTimeline, IReadOnlyList<RecordingSegment>?>(nameof(Segments));

    /// <summary>Time being played; null hides the cursor.</summary>
    public static readonly StyledProperty<DateTime?> PositionProperty =
        AvaloniaProperty.Register<RecordingTimeline, DateTime?>(nameof(Position));

    /// <summary>Runs with the chosen <see cref="DateTime"/> when the finger is lifted.</summary>
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<RecordingTimeline, ICommand?>(nameof(Command));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RecordingTimeline, IBrush?>(nameof(TrackBrush), new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));

    public static readonly StyledProperty<IBrush?> SegmentBrushProperty =
        AvaloniaProperty.Register<RecordingTimeline, IBrush?>(nameof(SegmentBrush), Brushes.IndianRed);

    public static readonly StyledProperty<IBrush?> CursorBrushProperty =
        AvaloniaProperty.Register<RecordingTimeline, IBrush?>(nameof(CursorBrush), Brushes.White);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<RecordingTimeline, IBrush?>(nameof(Foreground), Brushes.White);

    public DateTime Day
    {
        get => GetValue(DayProperty);
        set => SetValue(DayProperty, value);
    }

    public IReadOnlyList<RecordingSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public DateTime? Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
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

    public IBrush? CursorBrush
    {
        get => GetValue(CursorBrushProperty);
        set => SetValue(CursorBrushProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static RecordingTimeline()
    {
        AffectsRender<RecordingTimeline>(
            DayProperty,
            SegmentsProperty,
            PositionProperty,
            TrackBrushProperty,
            SegmentBrushProperty,
            CursorBrushProperty,
            ForegroundProperty);
        FocusableProperty.OverrideDefaultValue<RecordingTimeline>(false);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;

        if (width <= 0)
        {
            return;
        }

        // A transparent background makes the space around the bar hit-testable as well.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var track = new Rect(0, LabelHeight, width, TrackHeight);

        context.DrawRectangle(TrackBrush, null, track, TrackRadius, TrackRadius);

        DrawSegments(context, track);
        DrawHours(context, track);

        var cursor = _touchTime ?? Position;

        if (cursor is { } time && time >= Day && time <= Day.AddDays(1))
        {
            DrawCursor(context, track, time, _touchTime is not null);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;

        return new Size(width, LabelHeight + TrackHeight + 4);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        e.Pointer.Capture(this);

        _touchTime = TimeAt(e.GetPosition(this).X);
        e.Handled = true;

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_touchTime is null)
        {
            return;
        }

        _touchTime = TimeAt(e.GetPosition(this).X);
        e.Handled = true;

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_touchTime is not { } time)
        {
            return;
        }

        _touchTime = null;
        e.Handled = true;

        e.Pointer.Capture(null);

        InvalidateVisual();

        if (Command is { } command && command.CanExecute(time))
        {
            command.Execute(time);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _touchTime = null;

        InvalidateVisual();
    }

    private static FormattedText Label(string text, IBrush? brush, double size, FontWeight weight = FontWeight.Normal)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight),
            size,
            brush);
    }

    // Moments are whole minutes; the end of the bar is the last minute of the day.
    private DateTime TimeAt(double x)
    {
        var share = Bounds.Width <= 0 ? 0 : Math.Clamp(x / Bounds.Width, 0, 1);
        var minutes = Math.Min(Math.Round(share * 24 * 60), 24 * 60 - 1);

        return Day.AddMinutes(minutes);
    }

    private double XAt(DateTime time)
    {
        var hours = Math.Clamp((time - Day).TotalHours, 0, 24);

        return hours / 24 * Bounds.Width;
    }

    private void DrawSegments(DrawingContext context, Rect track)
    {
        if (Segments is not { Count: > 0 } segments || SegmentBrush is not { } brush)
        {
            return;
        }

        using var clip = context.PushClip(new RoundedRect(track, TrackRadius));

        foreach (var segment in segments)
        {
            var left = XAt(segment.Start);
            var right = XAt(segment.End);

            if (right <= 0 || left >= track.Width)
            {
                continue;
            }

            // Short segments stay visible as a thin line.
            context.FillRectangle(brush, new Rect(left, track.Y, Math.Max(right - left, 1.5), track.Height));
        }
    }

    private void DrawHours(DrawingContext context, Rect track)
    {
        var hourWidth = track.Width / 24;
        var step = LabelSteps.FirstOrDefault(hours => hours * hourWidth >= MinimumLabelSpacing, 12);
        var tick = new Pen(Foreground, 1);
        var culture = Localizer.Instance.Culture;

        for (var hour = 0; hour <= 24; hour++)
        {
            var x = Math.Round(hour * hourWidth) + 0.5;
            var labelled = hour % step == 0;
            var top = labelled ? track.Y : track.Bottom - track.Height / 3;

            using (context.PushOpacity(labelled ? 0.55 : 0.3))
            {
                context.DrawLine(tick, new Point(x, top), new Point(x, track.Bottom));
            }

            if (!labelled || hour == 24)
            {
                continue;
            }

            var text = Label(Day.AddHours(hour).ToString("HH", culture), Foreground, LabelFontSize);
            var left = Math.Clamp(x - text.Width / 2, 0, track.Width - text.Width);

            context.DrawText(text, new Point(left, (LabelHeight - text.Height) / 2));
        }
    }

    private void DrawCursor(DrawingContext context, Rect track, DateTime time, bool touched)
    {
        var x = XAt(time);
        var pen = new Pen(CursorBrush, touched ? 3 : 2);

        context.DrawLine(pen, new Point(x, track.Y - 2), new Point(x, track.Bottom + 2));

        if (!touched)
        {
            return;
        }

        // The chosen time is shown above the finger, over the hour labels.
        var text = Label(time.ToString("t", Localizer.Instance.Culture), Brushes.White, 13, FontWeight.SemiBold);
        var box = new Rect(
            Math.Clamp(x - text.Width / 2 - 6, 0, Math.Max(track.Width - text.Width - 12, 0)),
            0,
            text.Width + 12,
            LabelHeight);

        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x13, 0x14, 0x16)), null, box, 4, 4);
        context.DrawText(text, new Point(box.X + 6, (LabelHeight - text.Height) / 2));
    }
}
