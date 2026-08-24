using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using UnityEditor;
using UnityEngine;

// Verifies the full ObjectiveRoiPrimary against a fixed test arrangement,
// matching verify_step5b.py: house 0 fixed, houses 1 and 2 each rigidly
// rotated+translated (freespace + boundary + roi together).
public static class Step5bVerification
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

    [MenuItem("WalkIn Port/Verify Step 5b")]
    public static void Run()
    {
        Polygon fs0 = GetRecenteredFreespace(0);
        Polygon fs1Local = GetRecenteredFreespace(1);
        Polygon fs2Local = GetRecenteredFreespace(2);

        var boundary0 = new CircleShape(0, 0, 1.2);
        var roi0 = new CircleShape(0, 0, 0.6);

        (double dx, double dy, double angle) state1 = (4.0, 0.5, 20.0);
        Polygon fs1 = RigidTransformPolygon(fs1Local, state1.angle, state1.dx, state1.dy);
        CircleShape boundary1 = new CircleShape(0, 0, 1.2).Transform(0, 0, state1.angle, state1.dx, state1.dy);
        CircleShape roi1 = new CircleShape(0, 0, 0.6).Transform(0, 0, state1.angle, state1.dx, state1.dy);

        (double dx, double dy, double angle) state2 = (-0.5, 4.5, -15.0);
        Polygon fs2 = RigidTransformPolygon(fs2Local, state2.angle, state2.dx, state2.dy);
        CircleShape boundary2 = new CircleShape(0, 0, 1.2).Transform(0, 0, state2.angle, state2.dx, state2.dy);
        CircleShape roi2 = new CircleShape(0, 0, 0.6).Transform(0, 0, state2.angle, state2.dx, state2.dy);

        var freespaces = new[] { fs0, fs1, fs2 };
        var boundaries = new CircleShape?[] { boundary0, boundary1, boundary2 };
        var rois = new CircleShape?[] { roi0, roi1, roi2 };

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
