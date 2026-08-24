using System.Linq;
using System.Text;
using NetTopologySuite.Geometries;
using UnityEditor;
using UnityEngine;

// DE is stochastic, so this doesn't check for an exact match against Python --
// it checks that the optimizer actually converges (loss decreases) and reports
// the final loss so it can be eyeballed against a Python run for comparable quality.
public static class Step6Verification
{
    static Polygon GetRecenteredFreespace(int houseIndex)
    {
        HouseData house = HouseLoader.LoadHouseByIndex(houseIndex);
        Polygon dummyBoundary = PolygonUtils.CreateCircle(0, 0, 1.2);
        var result = FreespaceCalculator.Compute(house, "2", dummyBoundary);
        return result.Freespace;
    }

    [MenuItem("WalkIn Port/Verify Step 6 (run optimizer)")]
    public static void Run()
    {
        var freespaces = new[] { GetRecenteredFreespace(0), GetRecenteredFreespace(1), GetRecenteredFreespace(2) };
        var boundaries = new[]
        {
            new CircleShape(0, 0, 1.2),
            new CircleShape(0, 0, 1.2),
            new CircleShape(0, 0, 1.2),
        };
        var rois = new[]
        {
            new CircleShape(0, 0, 0.6),
            new CircleShape(0, 0, 0.6),
            new CircleShape(0, 0, 0.6),
        };

        var result = DifferentialEvolutionOptimizer.Optimize(
            freespaces, boundaries, rois, ObjectiveFunction.ObjectiveParams.Default,
            maxIter: 50, popSizeMultiplier: 15, translationBound: 5.0, rotationBound: 30.0);

        var sb = new StringBuilder();
        sb.AppendLine("=== loss every 10 generations ===");
        for (int gen = 0; gen < result.LossHistory.Count; gen += 10)
        {
            sb.AppendLine($"gen {gen}: {result.LossHistory[gen]:F6}");
        }
        sb.AppendLine($"gen {result.LossHistory.Count - 1} (final): {result.LossHistory[^1]:F6}");

        sb.AppendLine();
        sb.AppendLine("=== best states (house 1, house 2) ===");
        foreach (var s in result.MovingStates)
        {
            sb.AppendLine($"dx={s.Dx:F4}, dy={s.Dy:F4}, angle={s.AngleDeg:F4}");
        }

        sb.AppendLine();
        sb.AppendLine($"final loss: {result.BestLoss:F6}");

        Debug.Log(sb.ToString());
    }
}
