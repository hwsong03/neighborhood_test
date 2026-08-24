using System.Text;
using NetTopologySuite.Geometries;
using UnityEditor;
using UnityEngine;

// Menu bar -> WalkIn Port -> Verify Step 3.
// Uses a fixed test user position (same one used in the matching Python check script)
// so the numbers can be compared directly.
public static class Step3Verification
{
    static readonly string[] Labels = { "korea (0)", "general (1)", "modern (2)" };

    // same fixed test position for all 3 houses, matches verify_step3.py
    const double TestX = 1.0;
    const double TestY = 1.0;
    const double Radius = 1.2;

    [MenuItem("WalkIn Port/Verify Step 3")]
    public static void Run()
    {
        var sb = new StringBuilder();

        for (int i = 0; i < 3; i++)
        {
            HouseData house = HouseLoader.LoadHouseByIndex(i);
            Polygon boundary = PolygonUtils.CreateCircle(TestX, TestY, Radius);

            var result = FreespaceCalculator.Compute(house, "2", boundary);

            sb.AppendLine($"=== {Labels[i]} ===");
            if (result == null)
            {
                sb.AppendLine("FAILED");
            }
            else
            {
                sb.AppendLine($"freespace_area: {result.Freespace.Area:F4}");
                sb.AppendLine($"freespace_num_vertices: {result.Freespace.ExteriorRing.NumPoints}");
                sb.AppendLine($"fs_centroid (wall-relative): [{result.CentroidX:F4}, {result.CentroidY:F4}]");
                sb.AppendLine($"original_fs_centroid: [{result.OriginalCentroidX:F4}, {result.OriginalCentroidY:F4}]");
                sb.AppendLine($"boundary_centroid (after recenter): [{result.Boundary.Centroid.X:F4}, {result.Boundary.Centroid.Y:F4}]");

                Polygon roi = PolygonUtils.CreateRoi(result.Boundary.Centroid.X, result.Boundary.Centroid.Y, 1.0, 0.5);
                sb.AppendLine($"roi_area: {roi.Area:F4} (expect ~0.7854), roi_centroid: [{roi.Centroid.X:F4}, {roi.Centroid.Y:F4}]");
            }
            sb.AppendLine();
        }

        Debug.Log(sb.ToString());
    }
}
