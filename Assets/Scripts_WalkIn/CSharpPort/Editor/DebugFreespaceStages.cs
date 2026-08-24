using System.Linq;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;
using UnityEditor;
using UnityEngine;

// Temporary diagnostic: dump per-stage vertex counts for the "modern" house
// to compare against the matching Python stage-by-stage debug output.
public static class DebugFreespaceStages
{
    [MenuItem("WalkIn Port/Debug Freespace Stages (modern)")]
    public static void Run()
    {
        var sb = new StringBuilder();
        HouseData house = HouseLoader.LoadHouseByIndex(2);

        Polygon wall = RoomPolygonExtractor.GetPolygonWithWalls(house, "2");
        var objs = RoomPolygonExtractor.GetObjPolygons(house, "2", "OBB");

        sb.AppendLine($"wall vertices: {wall.ExteriorRing.NumPoints}, wall area: {wall.Area:F4}");
        foreach (var c in wall.ExteriorRing.Coordinates)
        {
            sb.AppendLine($"  [{c.X:F4}, {c.Y:F4}]");
        }

        double wcx = wall.Centroid.X, wcy = wall.Centroid.Y;

        var cleaned = objs.Select(p =>
        {
            var t = AffineTransformation.TranslationInstance(-wcx, -wcy).Transform(p);
            return (Polygon)t;
        }).ToList();

        sb.AppendLine("per-object vertex counts (translated): " + string.Join(", ", cleaned.Select(p => p.ExteriorRing.NumPoints)));
        sb.AppendLine("per-object areas: " + string.Join(", ", cleaned.Select(p => p.Area.ToString("F4"))));

        Geometry merged = UnaryUnionOp.Union(cleaned.Cast<Geometry>());
        sb.AppendLine($"merged geom type: {merged.GeometryType}");
        if (merged.GeometryType == "MultiPolygon")
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < merged.NumGeometries; i++)
                parts.Add(merged.GetGeometryN(i).NumPoints.ToString());
            sb.AppendLine($"merged num parts: {merged.NumGeometries}, vertex counts: {string.Join(", ", parts)}");
        }
        else
        {
            sb.AppendLine($"merged vertices: {merged.NumPoints}");
        }
        sb.AppendLine($"merged area: {merged.Area:F4}");

        Debug.Log(sb.ToString());
    }
}
