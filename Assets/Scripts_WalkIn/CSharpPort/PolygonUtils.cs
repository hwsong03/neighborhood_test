using System;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

// Small shared geometry helpers, port of polygon_func.py's create_circle()
// and differential.py's extract_largest_polygon().
public static class PolygonUtils
{
    static readonly GeometryFactory Factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

    /// <summary>
    /// Builds a 32-sided polygon approximating a circle, same as polygon_func.py's create_circle().
    /// </summary>
    public static Polygon CreateCircle(double centerX, double centerY, double radius, int numPoints = 32)
    {
        var coords = new Coordinate[numPoints + 1];
        for (int i = 0; i < numPoints; i++)
        {
            double theta = i * (2.0 * Math.PI / numPoints);
            coords[i] = new Coordinate(
                centerX + Math.Cos(theta) * radius,
                centerY + Math.Sin(theta) * radius);
        }
        coords[numPoints] = coords[0]; // close the ring

        var ring = Factory.CreateLinearRing(coords);
        return Factory.CreatePolygon(ring);
    }

    /// <summary>
    /// Builds the ROI (personal-zone) circle inside a boundary circle, same as
    /// differential.py's create_roi(center, radius, size_ratio).
    /// </summary>
    public static Polygon CreateRoi(double centerX, double centerY, double radius, double sizeRatio = 0.3)
    {
        return CreateCircle(centerX, centerY, radius * sizeRatio);
    }

    /// <summary>
    /// Rotates a polygon around the origin (0,0) then translates it -- used to move
    /// a house's freespace polygon during optimization. Mirrors the polygon half of
    /// differential.py's fast_rotate_translate() (rotation pivot = the freespace's
    /// own centroid, which is (0,0) since freespace polygons are always re-centered
    /// there in Step 3).
    /// </summary>
    public static Polygon RigidTransform(Polygon poly, double angleDegrees, double dx, double dy)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        Geometry rotated = AffineTransformation.RotationInstance(radians).Transform(poly);
        Geometry translated = AffineTransformation.TranslationInstance(dx, dy).Transform(rotated);
        return (Polygon)translated;
    }

    /// <summary>
    /// If a boolean operation (difference/union/etc.) produces a MultiPolygon or
    /// GeometryCollection, picks the single largest Polygon piece by area.
    /// Mirrors differential.py's extract_largest_polygon().
    /// </summary>
    public static Polygon ExtractLargestPolygon(Geometry geom)
    {
        if (geom == null || geom.IsEmpty) return null;

        if (geom.GeometryType == "Polygon")
        {
            return (Polygon)geom;
        }

        if (geom.GeometryType == "MultiPolygon")
        {
            return LargestOf(AllSubGeometries(geom));
        }

        if (geom.GeometryType == "GeometryCollection")
        {
            var polygons = AllSubGeometries(geom).Where(g => g.GeometryType == "Polygon").ToList();
            if (polygons.Count > 0) return LargestOf(polygons);

            var fromMultiPolys = AllSubGeometries(geom)
                .Where(g => g.GeometryType == "MultiPolygon")
                .SelectMany(AllSubGeometries)
                .ToList();
            if (fromMultiPolys.Count > 0) return LargestOf(fromMultiPolys);

            return null;
        }

        return null;
    }

    static System.Collections.Generic.IEnumerable<Geometry> AllSubGeometries(Geometry geom)
    {
        for (int i = 0; i < geom.NumGeometries; i++)
        {
            yield return geom.GetGeometryN(i);
        }
    }

    static Polygon LargestOf(System.Collections.Generic.IEnumerable<Geometry> geoms)
    {
        Geometry best = geoms.OrderByDescending(g => g.Area).FirstOrDefault();
        return best as Polygon;
    }
}
