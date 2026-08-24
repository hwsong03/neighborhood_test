using NetTopologySuite.Geometries;

// Lightweight circle representation (center + radius) for boundary/ROI circles.
// During optimization these get rotated/translated thousands of times; since a
// rotated+translated circle is still just a circle, we only need to move the
// center point (cheap) instead of rebuilding/rotating a full 32-point polygon
// every time. The polygon is only built on demand (ToPolygon()) for the cases
// that genuinely need a boolean geometry op (e.g. ROI vs freespace intersection).
public struct CircleShape
{
    public double CenterX, CenterY;
    public double Radius;

    public CircleShape(double centerX, double centerY, double radius)
    {
        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
    }

    /// <summary>
    /// Rotates this circle by angleDegrees (counter-clockwise, degrees) around
    /// pivot, then translates by (dx, dy). Matches the rigid-transform part of
    /// differential.py's fast_rotate_translate for a circle -- only the center
    /// moves, the shape/radius stay identical.
    /// </summary>
    public CircleShape Transform(double pivotX, double pivotY, double angleDegrees, double dx, double dy)
    {
        double theta = angleDegrees * System.Math.PI / 180.0;
        double cos = System.Math.Cos(theta);
        double sin = System.Math.Sin(theta);

        double relX = CenterX - pivotX;
        double relY = CenterY - pivotY;

        double rotatedX = relX * cos - relY * sin;
        double rotatedY = relX * sin + relY * cos;

        return new CircleShape(pivotX + rotatedX + dx, pivotY + rotatedY + dy, Radius);
    }

    /// <summary>
    /// Exact analytic distance between two circles' edges (matches shapely's
    /// poly1.distance(poly2) for two non-overlapping circles): positive if
    /// apart, 0 if touching/overlapping at the boundary.
    /// </summary>
    public double DistanceTo(CircleShape other)
    {
        double dx = CenterX - other.CenterX;
        double dy = CenterY - other.CenterY;
        double centerDist = System.Math.Sqrt(dx * dx + dy * dy);
        double d = centerDist - Radius - other.Radius;
        return d > 0 ? d : 0;
    }

    /// <summary>
    /// Exact analytic overlap area between two circles (closed-form formula,
    /// no polygon needed). 0 if they don't overlap.
    /// </summary>
    public double IntersectionArea(CircleShape other)
    {
        double dx = CenterX - other.CenterX;
        double dy = CenterY - other.CenterY;
        double d = System.Math.Sqrt(dx * dx + dy * dy);
        double r1 = Radius, r2 = other.Radius;

        if (d >= r1 + r2) return 0.0;               // apart
        if (d <= System.Math.Abs(r1 - r2))           // one fully inside the other
        {
            double rMin = System.Math.Min(r1, r2);
            return System.Math.PI * rMin * rMin;
        }

        double r1Sq = r1 * r1, r2Sq = r2 * r2;
        double alpha = System.Math.Acos((d * d + r1Sq - r2Sq) / (2 * d * r1));
        double beta = System.Math.Acos((d * d + r2Sq - r1Sq) / (2 * d * r2));

        return r1Sq * (alpha - System.Math.Sin(2 * alpha) / 2.0)
             + r2Sq * (beta - System.Math.Sin(2 * beta) / 2.0);
    }

    /// <summary>
    /// "Signed distance" matching differential.py's signed_distance() for two
    /// circles: positive gap if apart, or -(overlap area / smaller area) if overlapping.
    /// </summary>
    public double SignedDistanceTo(CircleShape other)
    {
        double interArea = IntersectionArea(other);
        if (interArea <= 0) return DistanceTo(other);

        double area1 = System.Math.PI * Radius * Radius;
        double area2 = System.Math.PI * other.Radius * other.Radius;
        double minArea = System.Math.Min(area1, area2);
        return -(interArea / minArea);
    }

    public Point Centroid()
    {
        return new Point(CenterX, CenterY);
    }

    /// <summary>
    /// Materializes this circle into an actual 32-sided polygon, only when a
    /// real boolean geometry op against a non-circle shape (like freespace) is needed.
    /// </summary>
    public Polygon ToPolygon()
    {
        return PolygonUtils.CreateCircle(CenterX, CenterY, Radius);
    }
}
