using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using UnityEditor;
using UnityEngine;

// Isolation test: only house0 + house1 (skips house2/modern, which has the
// known Eulerian wall-merge ambiguity from Step 3), to confirm that's the
// actual source of the Step 5b connectivity/coverage mismatch.
public static class Step5cVerification
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

    [MenuItem("WalkIn Port/Verify Step 5c (2-house isolation)")]
    public static void Run()
    {
        Polygon fs0 = GetRecenteredFreespace(0);
        Polygon fs1Local = GetRecenteredFreespace(1);

        var boundary0 = new CircleShape(0, 0, 1.2);
        var roi0 = new CircleShape(0, 0, 0.6);

        (double dx, double dy, double angle) state1 = (4.0, 0.5, 20.0);
        Polygon fs1 = RigidTransformPolygon(fs1Local, state1.angle, state1.dx, state1.dy);
        CircleShape boundary1 = new CircleShape(0, 0, 1.2).Transform(0, 0, state1.angle, state1.dx, state1.dy);
        CircleShape roi1 = new CircleShape(0, 0, 0.6).Transform(0, 0, state1.angle, state1.dx, state1.dy);

        var freespaces = new[] { fs0, fs1 };
        var boundaries = new CircleShape?[] { boundary0, boundary1 };
        var rois = new CircleShape?[] { roi0, roi1 };

        var result = ObjectiveFunction.ObjectiveRoiPrimary(freespaces, boundaries, rois, ObjectiveFunction.ObjectiveParams.Default);

        Debug.Log(
            $"loss: {result.Loss:F6}\n" +
            $"roi_score: {result.RoiScore:F6}\n" +
            $"roi_mean: {result.RoiMean:F6}\n" +
            $"roi_min: {result.RoiMin:F6}\n" +
            $"roi_ownership_mean: {result.RoiOwnershipMean:F6}\n" +
            $"roi_ownership_min: {result.RoiOwnershipMin:F6}\n" +
            $"connectivity_cost: {result.ConnectivityCost:F6}\n" +
            $"coverage_score: {result.CoverageScore:F6}\n" +
            $"adjacency_score: {result.AdjacencyScore:F6}");
    }
}
