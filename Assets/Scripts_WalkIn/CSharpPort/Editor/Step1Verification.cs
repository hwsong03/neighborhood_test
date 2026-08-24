using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Temporary Editor tool: Menu bar -> WalkIn Port -> Verify Step 1.
// Loads the same 3 houses as verify_step1.py and logs the same numbers,
// so the results can be compared directly against verify_step1_output.json.
public static class Step1Verification
{
    static readonly string[] Labels = { "korea (0)", "general (1)", "modern (2)" };

    [MenuItem("WalkIn Port/Verify Step 1")]
    public static void Run()
    {
        var sb = new StringBuilder();

        for (int i = 0; i < 3; i++)
        {
            HouseData house = HouseLoader.LoadHouseByIndex(i);
            var wallPoly = RoomPolygonExtractor.GetPolygonWithWalls(house, "2");
            var objPolys = RoomPolygonExtractor.GetObjPolygons(house, "2", "OBB");

            sb.AppendLine($"=== {Labels[i]} ===");

            if (wallPoly == null)
            {
                sb.AppendLine("wall polygon: FAILED to build");
            }
            else
            {
                sb.AppendLine($"wall_area: {wallPoly.Area:F4}");
                sb.AppendLine($"wall_centroid: [{wallPoly.Centroid.X:F4}, {wallPoly.Centroid.Y:F4}]");
                sb.AppendLine($"wall_num_vertices: {wallPoly.ExteriorRing.NumPoints}");
            }

            sb.AppendLine($"num_objects: {objPolys.Count}");
            sb.AppendLine($"total_object_area: {objPolys.Sum(p => p.Area):F4}");
            sb.AppendLine();
        }

        Debug.Log(sb.ToString());
    }
}
