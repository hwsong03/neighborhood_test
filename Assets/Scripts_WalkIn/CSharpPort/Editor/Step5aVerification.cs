using System.Text;
using NetTopologySuite.Geometries;
using UnityEditor;
using UnityEngine;

public static class Step5aVerification
{
    [MenuItem("WalkIn Port/Verify Step 5a")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var config = ObjectiveFunction.DistanceConfig.Default;

        sb.AppendLine("=== distance_score_function ===");
        foreach (double d in new[] { -0.5, -0.1, 0.0, 0.05, 0.1, 0.5, 2.0 })
        {
            sb.AppendLine($"d={d}: score={ObjectiveFunction.DistanceScoreFunction(d, config):F4}");
        }

        sb.AppendLine();
        sb.AppendLine("=== distance arrangement (3 circles) ===");
        var boundaries = new System.Collections.Generic.List<CircleShape?>
        {
            new CircleShape(0, 0, 1.2),
            new CircleShape(2.0, 0, 1.2),
            new CircleShape(1.0, 2.5, 1.2),
        };
        double scoreSimple = ObjectiveFunction.SimpleDistanceArrangement(boundaries, config);
        double scoreKnn = ObjectiveFunction.KnnDistanceArrangement(boundaries, 2, config);
        sb.AppendLine($"simple: score={scoreSimple:F4}");
        sb.AppendLine($"knn(k=2): score={scoreKnn:F4}");

        sb.AppendLine();
        sb.AppendLine("=== morphological_connectivity_check ===");
        HouseData house = HouseLoader.LoadHouseByIndex(2);
        Polygon wall = RoomPolygonExtractor.GetPolygonWithWalls(house, "2");

        var boundaryInside = new CircleShape(wall.Centroid.X, wall.Centroid.Y, 1.2);
        double costA = ObjectiveFunction.MorphologicalConnectivityCheck(
            new[] { wall }, new[] { boundaryInside.ToPolygon() }, 0.05, 100);
        sb.AppendLine($"case A (boundary inside room): cost={costA}");

        var boundaryFar = new CircleShape(wall.Centroid.X + 100, wall.Centroid.Y + 100, 1.2);
        double costB = ObjectiveFunction.MorphologicalConnectivityCheck(
            new[] { wall }, new[] { boundaryFar.ToPolygon() }, 0.05, 100);
        sb.AppendLine($"case B (boundary far away): cost={costB}");

        Debug.Log(sb.ToString());
    }
}
