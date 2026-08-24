using NetTopologySuite.Geometries;
using UnityEditor;
using UnityEngine;

// Hand-computed test case for HouseArrangementApplier (no Python equivalent --
// this is Unity-application-layer logic). Values below were worked out by hand
// before writing this test; see the conversation record for the derivation.
public static class Step7bVerification
{
    static Polygon TinySquareAt(double x, double y)
    {
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
        double e = 0.0005;
        var coords = new[]
        {
            new Coordinate(x - e, y - e),
            new Coordinate(x + e, y - e),
            new Coordinate(x + e, y + e),
            new Coordinate(x - e, y + e),
            new Coordinate(x - e, y - e),
        };
        return factory.CreatePolygon(coords);
    }

    [MenuItem("WalkIn Port/Verify Step 7b (house arrangement)")]
    public static void Run()
    {
        // house0 fixed at (0,0); house1 optimizer state: dx=2, dy=3, angle=90
        var optResult = new DifferentialEvolutionOptimizer.Result
        {
            FinalFreespaces = new[] { TinySquareAt(0, 0), TinySquareAt(2, 3) },
            FinalBoundaries = new[]
            {
                new CircleShape(0, 0, 1.2),
                new CircleShape(0, 0, 1.2).Transform(0, 0, 90, 2, 3),
            },
            FinalRois = new CircleShape[0],
            MovingStates = new[]
            {
                new DifferentialEvolutionOptimizer.TransformState { Dx = 2, Dy = 3, AngleDeg = 90 },
            },
        };

        var originalLocalCentroids = new[]
        {
            new CoordinateTransform.Point2D(0.5, 0.2),  // house0's own prefab-pivot-to-freespace offset
            new CoordinateTransform.Point2D(0.3, -0.1), // house1's own prefab-pivot-to-freespace offset
        };

        var placements = HouseArrangementApplier.ComputeHousePlacements(optResult, originalLocalCentroids, myType: 0);
        var avatar1Pos = HouseArrangementApplier.ComputeAvatarPosition(optResult, originalLocalCentroids, myType: 0, houseIndex: 1);

        Debug.Log(
            $"house1 placement: position=({placements[1].Position.X:F4}, {placements[1].Position.Y:F4}), rotation={placements[1].RotationDeg:F4} " +
            $"-- expected position=(2.4000, 2.9000), rotation=-90.0000\n" +
            $"avatar1 position: ({avatar1Pos.X:F4}, {avatar1Pos.Y:F4}) -- expected (2.5000, 3.2000)");
    }
}
