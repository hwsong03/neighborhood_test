using UnityEditor;
using UnityEngine;

// This transform never existed in Python -- it's Unity-application-layer logic
// (TransferManager.cs's TransformToLocalSpace). So instead of comparing against
// a Python reference, this checks a hand-computable, by-hand-verified test case
// for self-consistency: distances between houses must come out the same
// regardless of whose local frame you measure them in (rigid transforms
// preserve distance).
public static class Step7Verification
{
    [MenuItem("WalkIn Port/Verify Step 7 (coordinate transform)")]
    public static void Run()
    {
        var house0 = new CoordinateTransform.HouseFrame
        {
            Centroid = new CoordinateTransform.Point2D(0, 0),
            RotationDeg = 0,
        };
        var house1 = new CoordinateTransform.HouseFrame
        {
            Centroid = new CoordinateTransform.Point2D(3, 4),
            RotationDeg = 90,
        };

        // From house0's own frame: where does house1 appear?
        var house1FromHouse0 = CoordinateTransform.TransformToLocalSpace(house1.Centroid, house0);
        double dist0 = System.Math.Sqrt(house1FromHouse0.X * house1FromHouse0.X + house1FromHouse0.Y * house1FromHouse0.Y);

        // From house1's own frame: where does house0 appear?
        var house0FromHouse1 = CoordinateTransform.TransformToLocalSpace(house0.Centroid, house1);
        double dist1 = System.Math.Sqrt(house0FromHouse1.X * house0FromHouse1.X + house0FromHouse1.Y * house0FromHouse1.Y);

        double rawDist = System.Math.Sqrt((3 - 0) * (3 - 0) + (4 - 0) * (4 - 0));

        Debug.Log(
            $"house1 seen from house0's frame: ({house1FromHouse0.X:F4}, {house1FromHouse0.Y:F4}) -- expected (3.0000, 4.0000)\n" +
            $"house0 seen from house1's frame: ({house0FromHouse1.X:F4}, {house0FromHouse1.Y:F4}) -- expected (-4.0000, 3.0000)\n" +
            $"raw distance: {rawDist:F4}\n" +
            $"distance via house0's frame: {dist0:F4}\n" +
            $"distance via house1's frame: {dist1:F4}\n" +
            $"all three distances should match exactly (rigid transforms preserve distance)");
    }
}
