using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using UnityEditor;
using UnityEngine;

// Verifies TraverseZoneCalculator against verify_step7a.py, using the same
// fixed test arrangement as Step5bVerification.
public static class Step7aVerification
{
    static Polygon RigidTransformPolygon(Polygon poly, double angleDeg, double dx, double dy)
    {
        double radians = angleDeg * System.Math.PI / 180.0;
        Geometry rotated = AffineTransformation.RotationInstance(radians).Transform(poly);
        Geometry translated = AffineTransformation.TranslationInstance(dx, dy).Transform(rotated);
        return (Polygon)translated;
    }

    static Polygon GetRecenteredFreespace(int houseIndex)
    {
        HouseData house = HouseLoader.LoadHouseByIndex(houseIndex);
        Polygon dummyBoundary = PolygonUtils.CreateCircle(0, 0, 1.2);
        var result = FreespaceCalculator.Compute(house, "2", dummyBoundary);
        return result.Freespace;
    }

    [MenuItem("WalkIn Port/Verify Step 7a (traverse zone)")]
    public static void Run()
    {
        Polygon fs0 = GetRecenteredFreespace(0);
        var boundary0 = new CircleShape(0, 0, 1.2);

        (double dx, double dy, double angle) state1 = (4.0, 0.5, 20.0);
        Polygon fs1 = RigidTransformPolygon(GetRecenteredFreespace(1), state1.angle, state1.dx, state1.dy);
        CircleShape boundary1 = new CircleShape(0, 0, 1.2).Transform(0, 0, state1.angle, state1.dx, state1.dy);

        (double dx, double dy, double angle) state2 = (-0.5, 4.5, -15.0);
        Polygon fs2 = RigidTransformPolygon(GetRecenteredFreespace(2), state2.angle, state2.dx, state2.dy);
        CircleShape boundary2 = new CircleShape(0, 0, 1.2).Transform(0, 0, state2.angle, state2.dx, state2.dy);

        var optResult = new DifferentialEvolutionOptimizer.Result
        {
            FinalFreespaces = new[] { fs0, fs1, fs2 },
            FinalBoundaries = new[] { boundary0, boundary1, boundary2 },
            FinalRois = new CircleShape[0],
            MovingStates = new[]
            {
                new DifferentialEvolutionOptimizer.TransformState { Dx = state1.dx, Dy = state1.dy, AngleDeg = state1.angle },
                new DifferentialEvolutionOptimizer.TransformState { Dx = state2.dx, Dy = state2.dy, AngleDeg = state2.angle },
            },
        };

        var zones = TraverseZoneCalculator.Compute(optResult);

        var sb = new StringBuilder();
        for (int i = 0; i < zones.Length; i++)
        {
            double totalArea = 0;
            foreach (var ring in zones[i])
            {
                var poly = new GeometryFactory().CreatePolygon(ring);
                totalArea += poly.Area;
            }
            sb.AppendLine($"house {i}: num_rings={zones[i].Count}, total_area={totalArea:F6}");
            if (zones[i].Count == 1)
            {
                sb.AppendLine($"  num_vertices={zones[i][0].Length}");
            }
        }

        Debug.Log(sb.ToString());
    }
}
