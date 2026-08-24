using UnityEditor;
using UnityEngine;

// Temporary diagnostic: check CircleShape's analytic formulas against the
// matching Python (Shapely 32-gon) test cases in the console.
public static class DebugCircleShape
{
    [MenuItem("WalkIn Port/Debug CircleShape Math")]
    public static void Run()
    {
        var c1 = new CircleShape(0, 0, 1.0);
        var c2 = new CircleShape(1.2, 0, 0.8);
        Debug.Log($"case1 (overlap): distance={c1.DistanceTo(c2):F4}, intersection_area={c1.IntersectionArea(c2):F4}, signed_distance={c1.SignedDistanceTo(c2):F4}");

        var c3 = new CircleShape(0, 0, 1.0);
        var c4 = new CircleShape(5, 0, 1.0);
        Debug.Log($"case2 (apart): distance={c3.DistanceTo(c4):F4}, intersection_area={c3.IntersectionArea(c4):F4}, signed_distance={c3.SignedDistanceTo(c4):F4}");

        var c5 = new CircleShape(0, 0, 2.0);
        var c6 = new CircleShape(0.3, 0.1, 0.5);
        Debug.Log($"case3 (inside): distance={c5.DistanceTo(c6):F4}, intersection_area={c5.IntersectionArea(c6):F4}, signed_distance={c5.SignedDistanceTo(c6):F4}");
    }
}
