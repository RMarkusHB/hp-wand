using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using HP.Wand.Gesture;
using System.Drawing;

namespace HP.Wand.Camera;

/// <summary>
/// Captures frames from V4L2, isolates the brightest IR blob,
/// and fires gesture events.
/// </summary>
public class CameraTracker : IDisposable
{
    private const int BrightnessThreshold = 200; // 0-255; IR LED reflects very bright
    private const int MinBlobArea = 5;
    private const int NoMotionTimeoutMs = 800;
    private const double StillnessThreshold = 0.005; // in normalized coords

    public event Action<GesturePoint>? OnPoint;
    public event Action<GesturePoint[]>? OnGestureEnd;

    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    private readonly List<GesturePoint> _currentGesture = [];
    private DateTime _lastBlobTime = DateTime.MinValue;
    private GesturePoint? _lastPoint;

    public void Start(int deviceIndex = 0)
    {
        _capture = new VideoCapture(deviceIndex, VideoCapture.API.V4L2);
        if (!_capture.IsOpened)
            throw new InvalidOperationException($"Cannot open /dev/video{deviceIndex}");

        _capture.Set(CapProp.FrameWidth, 640);
        _capture.Set(CapProp.FrameHeight, 480);
        _capture.Set(CapProp.Fps, 30);

        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _captureTask?.Wait(TimeSpan.FromSeconds(2));
    }

    private void CaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var gray = new Mat();
        using var thresholded = new Mat();

        while (!ct.IsCancellationRequested)
        {
            if (!_capture!.Read(frame) || frame.IsEmpty)
                continue;

            CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);
            CvInvoke.Threshold(gray, thresholded, BrightnessThreshold, 255, ThresholdType.Binary);

            var centroid = FindBlobCentroid(thresholded);

            if (centroid.HasValue)
            {
                _lastBlobTime = DateTime.UtcNow;
                var pt = new GesturePoint(centroid.Value.X, centroid.Value.Y,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                // Suppress jitter: skip if nearly stationary
                if (_lastPoint == null ||
                    Distance(_lastPoint, pt) > StillnessThreshold)
                {
                    _currentGesture.Add(pt);
                    _lastPoint = pt;
                    OnPoint?.Invoke(pt);
                }
            }
            else if (_currentGesture.Count > 0 &&
                     (DateTime.UtcNow - _lastBlobTime).TotalMilliseconds > NoMotionTimeoutMs)
            {
                FireGestureEnd();
            }
        }
    }

    private void FireGestureEnd()
    {
        if (_currentGesture.Count < 2)
        {
            _currentGesture.Clear();
            _lastPoint = null;
            return;
        }

        var points = _currentGesture.ToArray();
        _currentGesture.Clear();
        _lastPoint = null;

        OnGestureEnd?.Invoke(points);
    }

    private static PointF? FindBlobCentroid(Mat binary)
    {
        using var contours = new VectorOfVectorOfPoint();
        using var hierarchy = new Mat();
        CvInvoke.FindContours(binary, contours, hierarchy,
            RetrType.External, ChainApproxMethod.ChainApproxSimple);

        double bestArea = 0;
        PointF? best = null;

        for (int i = 0; i < contours.Size; i++)
        {
            double area = CvInvoke.ContourArea(contours[i]);
            if (area < MinBlobArea || area <= bestArea) continue;

            var m = CvInvoke.Moments(contours[i]);
            if (m.M00 == 0) continue;

            bestArea = area;
            // Normalize to [0, 1]
            double cx = (m.M10 / m.M00) / binary.Width;
            double cy = (m.M01 / m.M00) / binary.Height;
            best = new PointF((float)cx, (float)cy);
        }

        return best;
    }

    private static double Distance(GesturePoint a, GesturePoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
        _cts?.Dispose();
    }
}
