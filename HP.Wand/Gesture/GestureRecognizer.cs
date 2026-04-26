namespace HP.Wand.Gesture;

/// <summary>
/// $1 Unistroke Recognizer — Wobbrock et al. (2007), pure C# implementation.
/// </summary>
public class GestureRecognizer
{
    private const int NumPoints = 64;
    private const double SquareSize = 250.0;
    private const double DiagonalLen = SquareSize * Math.Sqrt2;
    private const double HalfDiagonal = DiagonalLen / 2.0;
    private const double AngleRange = Math.PI / 4;   // ±45°
    private const double AnglePrecision = Math.PI / 90; // 2° steps for golden section
    private const double Phi = 0.61803398874989484820; // golden ratio

    private readonly List<GestureTemplate> _templates = [];

    public void AddTemplate(GestureTemplate template)
    {
        var resampled = Resample(template.Points, NumPoints);
        var rotated = RotateToZero(resampled);
        var scaled = ScaleToSquare(rotated, SquareSize);
        var translated = TranslateTo(scaled, new GesturePoint(0, 0, 0));
        _templates.Add(new GestureTemplate { Name = template.Name, Points = translated });
    }

    public void SetTemplates(IEnumerable<GestureTemplate> templates)
    {
        _templates.Clear();
        foreach (var t in templates)
            AddTemplate(t);
    }

    /// <summary>
    /// Returns (name, score 0-1) for best matching template, or (null, 0) if none qualify.
    /// </summary>
    public (string? Name, double Score) Recognize(GesturePoint[] points, double threshold = 0.80)
    {
        if (_templates.Count == 0 || points.Length < 2)
            return (null, 0);

        var candidate = Normalize(points);

        string? bestName = null;
        double bestScore = 0;

        foreach (var t in _templates)
        {
            double d = GoldenSectionSearch(candidate, t.Points, -AngleRange, AngleRange, AnglePrecision);
            double score = 1.0 - d / HalfDiagonal;
            if (score > bestScore)
            {
                bestScore = score;
                bestName = t.Name;
            }
        }

        return bestScore >= threshold ? (bestName, bestScore) : (null, bestScore);
    }

    // ── Normalize pipeline ────────────────────────────────────────────────────

    private static GesturePoint[] Normalize(GesturePoint[] points)
    {
        var r = Resample(points, NumPoints);
        r = RotateToZero(r);
        r = ScaleToSquare(r, SquareSize);
        r = TranslateTo(r, new GesturePoint(0, 0, 0));
        return r;
    }

    // ── Resample to N equidistant points ────────────────────────────────────

    private static GesturePoint[] Resample(GesturePoint[] pts, int n)
    {
        double interval = PathLength(pts) / (n - 1);
        double accumulated = 0;
        var result = new List<GesturePoint> { pts[0] };

        for (int i = 1; i < pts.Length; i++)
        {
            double d = Distance(pts[i - 1], pts[i]);
            if (accumulated + d >= interval)
            {
                double t = (interval - accumulated) / d;
                double qx = pts[i - 1].X + t * (pts[i].X - pts[i - 1].X);
                double qy = pts[i - 1].Y + t * (pts[i].Y - pts[i - 1].Y);
                var q = new GesturePoint(qx, qy, pts[i].TimestampMs);
                result.Add(q);
                // Insert q back so the remainder continues from q
                var newPts = new GesturePoint[pts.Length - i + 1];
                newPts[0] = q;
                Array.Copy(pts, i, newPts, 1, pts.Length - i);
                pts = newPts;
                i = 0;
                accumulated = 0;
            }
            else
            {
                accumulated += d;
            }
        }

        // Floating-point drift — pad with last point if needed
        while (result.Count < n)
            result.Add(pts[^1]);

        return [.. result];
    }

    // ── Rotate to indicative angle ────────────────────────────────────────────

    private static GesturePoint[] RotateToZero(GesturePoint[] pts)
    {
        var c = Centroid(pts);
        double angle = Math.Atan2(pts[0].Y - c.Y, pts[0].X - c.X);
        return RotateBy(pts, -angle);
    }

    private static GesturePoint[] RotateBy(GesturePoint[] pts, double angle)
    {
        var c = Centroid(pts);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        return pts.Select(p =>
        {
            double qx = (p.X - c.X) * cos - (p.Y - c.Y) * sin + c.X;
            double qy = (p.X - c.X) * sin + (p.Y - c.Y) * cos + c.Y;
            return new GesturePoint(qx, qy, p.TimestampMs);
        }).ToArray();
    }

    // ── Scale to reference square ─────────────────────────────────────────────

    private static GesturePoint[] ScaleToSquare(GesturePoint[] pts, double size)
    {
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        if (w == 0) w = 1e-9;
        if (h == 0) h = 1e-9;
        return pts.Select(p => new GesturePoint(
            p.X * (size / w),
            p.Y * (size / h),
            p.TimestampMs)).ToArray();
    }

    // ── Translate centroid to reference point ─────────────────────────────────

    private static GesturePoint[] TranslateTo(GesturePoint[] pts, GesturePoint k)
    {
        var c = Centroid(pts);
        return pts.Select(p => new GesturePoint(
            p.X + k.X - c.X,
            p.Y + k.Y - c.Y,
            p.TimestampMs)).ToArray();
    }

    // ── Golden Section Search on rotation angle ───────────────────────────────

    private static double GoldenSectionSearch(
        GesturePoint[] pts, GesturePoint[] template,
        double a, double b, double threshold)
    {
        double x1 = Phi * a + (1 - Phi) * b;
        double x2 = (1 - Phi) * a + Phi * b;
        double f1 = PathDistanceAtAngle(pts, template, x1);
        double f2 = PathDistanceAtAngle(pts, template, x2);

        while (Math.Abs(b - a) > threshold)
        {
            if (f1 < f2)
            {
                b = x2; x2 = x1; f2 = f1;
                x1 = Phi * a + (1 - Phi) * b;
                f1 = PathDistanceAtAngle(pts, template, x1);
            }
            else
            {
                a = x1; x1 = x2; f1 = f2;
                x2 = (1 - Phi) * a + Phi * b;
                f2 = PathDistanceAtAngle(pts, template, x2);
            }
        }

        return Math.Min(f1, f2);
    }

    private static double PathDistanceAtAngle(GesturePoint[] pts, GesturePoint[] template, double angle)
    {
        var rotated = RotateBy(pts, angle);
        return PathDistance(rotated, template);
    }

    private static double PathDistance(GesturePoint[] a, GesturePoint[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += Distance(a[i], b[i]);
        return sum / a.Length;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double PathLength(GesturePoint[] pts)
    {
        double d = 0;
        for (int i = 1; i < pts.Length; i++)
            d += Distance(pts[i - 1], pts[i]);
        return d;
    }

    private static double Distance(GesturePoint a, GesturePoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static GesturePoint Centroid(GesturePoint[] pts) =>
        new(pts.Average(p => p.X), pts.Average(p => p.Y), 0);
}
