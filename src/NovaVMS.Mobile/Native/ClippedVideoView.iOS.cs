using Avalonia.Controls;
using Avalonia.iOS;
using AVFoundation;
using CoreAnimation;
using CoreGraphics;
using CoreMedia;
using Foundation;
using NovaVMS.Mobile.Services;
using NovaVMS.Protocols.Rtsp;
using UIKit;

namespace NovaVMS.Mobile.Native;

/// <summary>
/// Hardware decoded video: pictures go straight to an <see cref="AVSampleBufferDisplayLayer"/>, which decodes
/// them with VideoToolbox and shows them without any work on the main thread. The view is masked to its visible
/// part, so a preview scrolled partly out of the preview list is cut at its edge instead of covering the header.
/// </summary>
public sealed class ClippedVideoView : UIView, IVideoRenderer
{
    // Values of AVLayerVideoGravityResizeAspect and AVLayerVideoGravityResize.
    private const string KeepProportions = "AVLayerVideoGravityResizeAspect";
    private const string Stretch = "AVLayerVideoGravityResize";

    private readonly object _gate = new();
    private readonly AVSampleBufferDisplayLayer _display = new();

    // Size and line of the loading spinner of the desktop video tiles.
    private const double SpinnerSize = 24;
    private const double SpinnerRadius = 10;
    private const string SpinAnimation = "spin";

    private readonly CAShapeLayer _spinner = CreateSpinner();

    private readonly CALayer _mask = new()
    {
        BackgroundColor = UIColor.Black.CGColor
    };

    private CMVideoFormatDescription? _format;
    private IReadOnlyList<byte[]>? _formatParameters;
    private bool _needKeyFrame = true;
    private bool _detached;
    private AvaloniaView? _overlay;
    private CGRect? _area;
    private bool _fill;
    private double? _aspectRatio;
    private bool _connecting;

    /// <summary>The host moves the view by its frame; the mask follows in the same transaction.</summary>
    public override CGRect Frame
    {
        get => base.Frame;
        set
        {
            base.Frame = value;

            ApplyVisibleArea();
        }
    }

    public ClippedVideoView()
    {
        BackgroundColor = UIColor.Black;
        ClipsToBounds = true;
        _display.VideoGravity = KeepProportions;
        _display.BackgroundColor = UIColor.Black.CGColor;

        Layer.AddSublayer(_display);
        Layer.AddSublayer(_spinner);
    }

    public bool Render(VideoAccessUnit picture)
    {
        lock (_gate)
        {
            if (_detached)
            {
                return false;
            }

            if (Failed())
            {
                // The system stops the decoder, e.g. while the application was in the background.
                Flush(removeImage: false);

                _needKeyFrame = true;
            }

            if (_needKeyFrame && !picture.IsKeyFrame)
            {
                return false;
            }

            if (!ReferenceEquals(picture.ParameterSets, _formatParameters) && !UpdateFormat(picture))
            {
                return false;
            }

            using var sample = CreateSample(picture);

            if (sample is null)
            {
                _needKeyFrame = true;

                return false;
            }

            Enqueue(sample);

            _needKeyFrame = false;

            return true;
        }
    }

    public void RequireKeyFrame()
    {
        lock (_gate)
        {
            _needKeyFrame = true;
        }
    }

    // The layer keeps showing the last picture; the first key frame of the new stream replaces it.
    public void Restart()
    {
        lock (_gate)
        {
            _needKeyFrame = true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _needKeyFrame = true;

            if (!_detached)
            {
                Flush(removeImage: true);
            }
        }
    }

    public void SetFill(bool fill)
    {
        _fill = fill;

        RequestLayout();
    }

    public void SetAspectRatio(double? ratio)
    {
        _aspectRatio = ratio;

        RequestLayout();
    }

    public void SetVisibleArea(Avalonia.Rect? area, double scale)
    {
        _area = area is { } visible
            ? new CGRect(visible.X, visible.Y, visible.Width, visible.Height)
            : null;

        ApplyVisibleArea();
    }

    public void SetOverlay(Control? overlay)
    {
        if (_overlay is { } previous)
        {
            _overlay = null;

            OverlayBackground.Detach(previous.Content);

            previous.Content = null;

            previous.RemoveFromSuperview();
            previous.Dispose();
        }

        if (overlay is null)
        {
            return;
        }

        // A second Avalonia view with a transparent background, as a subview above the video layer.
        var view = new AvaloniaView
        {
            Opaque = false,
            BackgroundColor = UIColor.Clear,
            Hidden = true,
            Frame = Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
            Content = overlay
        };

        view.Layer.Opaque = false;

        OverlayBackground.Attach(overlay);

        AddSubview(view);

        _overlay = view;
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_overlay is { } overlay)
        {
            overlay.Hidden = !visible;
        }
    }

    public void SetConnecting(bool connecting)
    {
        if (NSThread.IsMain)
        {
            ApplyConnecting(connecting);
        }
        else
        {
            BeginInvokeOnMainThread(() => ApplyConnecting(connecting));
        }
    }

    public void Detach()
    {
        SetOverlay(null);
        ApplyConnecting(false);

        lock (_gate)
        {
            if (_detached)
            {
                return;
            }

            _detached = true;

            Flush(removeImage: true);
            _format?.Dispose();

            _format = null;
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        var bounds = Bounds;

        CATransaction.Begin();

        CATransaction.DisableActions = true;

        if (!_fill && _aspectRatio is { } ratio and > 0 && bounds.Width > 0 && bounds.Height > 0)
        {
            // Known camera proportions: the layer gets their size and the picture is stretched into it.
            var width = Math.Min(bounds.Width, bounds.Height * ratio);
            var height = Math.Min(bounds.Height, bounds.Width / ratio);

            _display.VideoGravity = Stretch;
            _display.Frame = new CGRect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        }
        else
        {
            _display.VideoGravity = _fill ? Stretch : KeepProportions;
            _display.Frame = bounds;
        }

        _spinner.Position = new CGPoint(bounds.Width / 2, bounds.Height / 2);

        CATransaction.Commit();
    }

    // A three quarter ring from the top clockwise to the left, as the desktop path "M 12,2 A 10,10 0 1 1 2,12".
    private static CAShapeLayer CreateSpinner()
    {
        var center = new CGPoint(SpinnerSize / 2, SpinnerSize / 2);
        using var path = UIBezierPath.FromArc(center, (nfloat)SpinnerRadius, (nfloat)(-Math.PI / 2), (nfloat)Math.PI, true);

        return new CAShapeLayer
        {
            Bounds = new CGRect(0, 0, SpinnerSize, SpinnerSize),
            Path = path.CGPath,
            StrokeColor = UIColor.White.ColorWithAlpha(0.75f).CGColor,
            FillColor = UIColor.Clear.CGColor,
            LineWidth = 2,
            LineCap = CAShapeLayer.CapRound,
            Hidden = true
        };
    }

    private void ApplyConnecting(bool connecting)
    {
        if (_connecting == connecting)
        {
            return;
        }

        _connecting = connecting;

        CATransaction.Begin();

        CATransaction.DisableActions = true;
        _spinner.Hidden = !connecting;

        if (connecting)
        {
            var spin = CABasicAnimation.FromKeyPath("transform.rotation.z");

            spin.From = NSNumber.FromDouble(0);
            spin.To = NSNumber.FromDouble(2 * Math.PI);
            spin.Duration = 0.9;
            spin.RepeatCount = float.PositiveInfinity;
            // Leaving the application and coming back keeps the ring turning.
            spin.RemovedOnCompletion = false;

            _spinner.AddAnimation(spin, SpinAnimation);
        }
        else
        {
            _spinner.RemoveAnimation(SpinAnimation);
        }

        CATransaction.Commit();
    }

    private void RequestLayout()
    {
        if (NSThread.IsMain)
        {
            SetNeedsLayout();
        }
        else
        {
            BeginInvokeOnMainThread(SetNeedsLayout);
        }
    }

    // The area is in the coordinates of the Avalonia view that hosts this view; the mask is in its own coordinates.
    private void ApplyVisibleArea()
    {
        CATransaction.Begin();

        CATransaction.DisableActions = true;

        if (_area is { } area)
        {
            var frame = base.Frame;

            _mask.Frame = new CGRect(area.X - frame.X, area.Y - frame.Y, area.Width, area.Height);
            Layer.Mask = _mask;
        }
        else
        {
            Layer.Mask = null;
        }

        CATransaction.Commit();
    }

    // iOS 17 moved decoding to the sample buffer renderer of the layer; the layer methods remain for iOS 15 and 16.
    private bool Failed()
    {
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            return _display.SampleBufferRenderer.Status == AVQueuedSampleBufferRenderingStatus.Failed;
        }

#pragma warning disable CA1422
        return _display.Status == AVQueuedSampleBufferRenderingStatus.Failed;
#pragma warning restore CA1422
    }

    private void Enqueue(CMSampleBuffer sample)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            _display.SampleBufferRenderer.Enqueue(sample);

            return;
        }

#pragma warning disable CA1422
        _display.Enqueue(sample);
#pragma warning restore CA1422
    }

    private void Flush(bool removeImage)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            if (removeImage)
            {
                _display.SampleBufferRenderer.FlushWithRemovalOfDisplayedImage(true, () => { });
            }
            else
            {
                _display.SampleBufferRenderer.Flush();
            }

            return;
        }

#pragma warning disable CA1422
        if (removeImage)
        {
            _display.FlushAndRemoveImage();
        }
        else
        {
            _display.Flush();
        }
#pragma warning restore CA1422
    }

    private bool UpdateFormat(VideoAccessUnit picture)
    {
        var parameters = picture.ParameterSets.ToList();
        CMFormatDescriptionError error;
        var format = picture.Codec == VideoCodec.H265
            ? CMVideoFormatDescription.FromHevcParameterSets(parameters, 4, null!, out error)
            : CMVideoFormatDescription.FromH264ParameterSets(parameters, 4, out error);

        if (format is null || error != CMFormatDescriptionError.None)
        {
            format?.Dispose();

            _needKeyFrame = true;

            return false;
        }

        // Pictures of the previous stream are dropped from the queue; the one on screen stays until a new one is decoded.
        if (_format is not null)
        {
            Flush(removeImage: false);
        }

        var size = format.Dimensions;
        var previous = _format?.Dimensions;

        if (previous is not { } old || old.Width != size.Width || old.Height != size.Height)
        {
            // The shown size includes the pixel aspect ratio of the stream, which decides the proportions on screen.
            var shown = format.GetPresentationDimensions(true, false);

            MobileLog.Post($"Video: {picture.Codec} {size.Width}x{size.Height}, shown as {shown.Width:0}x{shown.Height:0}");
        }

        _format?.Dispose();

        _format = format;
        _formatParameters = picture.ParameterSets;

        return true;
    }

    private CMSampleBuffer? CreateSample(VideoAccessUnit picture)
    {
        var data = picture.ToLengthPrefixed();
        using var block = CMBlockBuffer.FromMemoryBlock(data, 0, CMBlockBufferFlags.AssureMemoryNow, out var blockError);

        if (block is null || blockError != CMBlockBufferError.None)
        {
            return null;
        }

        var sample = CMSampleBuffer.CreateReady(block, _format, 1, null, [(nuint)data.Length], out var sampleError);

        if (sample is null || sampleError != CMSampleBufferError.None)
        {
            sample?.Dispose();

            return null;
        }

        // Live video is shown as soon as it is decoded, without a presentation clock.
        if (sample.GetSampleAttachments(true) is [{ } attachments, ..])
        {
            attachments.DisplayImmediately = true;
        }

        return sample;
    }
}
