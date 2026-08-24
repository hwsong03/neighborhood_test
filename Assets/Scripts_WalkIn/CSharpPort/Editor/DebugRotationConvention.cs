using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using UnityEditor;
using UnityEngine;

public static class DebugRotationConvention
{
    [MenuItem("WalkIn Port/Debug Rotation Convention")]
    public static void Run()
    {
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
        var p = factory.CreatePoint(new Coordinate(1, 0));
        double radians = 90.0 * System.Math.PI / 180.0;
        var rotated = AffineTransformation.RotationInstance(radians).Transform(p);
        Debug.Log($"NTS rotate (1,0) by +90deg: ({rotated.Coordinate.X:F4}, {rotated.Coordinate.Y:F4})");
    }
}
