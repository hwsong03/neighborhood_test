using System;

// Converts the optimizer's output (each house's final position/rotation in the
// shared "optimized space", house 0 fixed) into "where does house Y appear,
// relative to MY OWN house's origin" -- i.e. what each client actually needs to
// position things in their own local Unity scene.
//
// Mirrors TransferManager.cs's TransformToLocalSpace()/TransformPointsToLocalSpace(),
// so it can act as a drop-in replacement once wired up, using the SAME rotation
// convention that code already uses (Unity's Quaternion.Euler(0, angle, 0) applied
// directly with the optimizer's angle, un-negated -- see the "Python CCW = Unity
// negative Y-rotation" comment there for why that is not a sign error).
public static class CoordinateTransform
{
    public struct Point2D
    {
        public double X, Y; // matches Unity's (x, z) ground-plane convention used throughout this project
        public Point2D(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// Absolute position/rotation of every house in the optimizer's shared frame
    /// (house 0 fixed at its own freespace centroid, rotation 0).
    /// </summary>
    public struct HouseFrame
    {
        public Point2D Centroid;
        public double RotationDeg;
    }

    public static HouseFrame[] GetAbsoluteFrames(DifferentialEvolutionOptimizer.Result optResult)
    {
        int n = optResult.FinalFreespaces.Length;
        var frames = new HouseFrame[n];
        for (int i = 0; i < n; i++)
        {
            var c = optResult.FinalFreespaces[i].Centroid;
            frames[i] = new HouseFrame
            {
                Centroid = new Point2D(c.X, c.Y),
                RotationDeg = i == 0 ? 0.0 : optResult.MovingStates[i - 1].AngleDeg,
            };
        }
        return frames;
    }

    /// <summary>
    /// Transforms a point from the shared optimized space into MY local space --
    /// i.e. undoes my own translation and rotation, so my own centroid always
    /// maps to (0,0). Exact port of TransferManager.cs's TransformToLocalSpace().
    /// </summary>
    public static Point2D TransformToLocalSpace(Point2D point, HouseFrame myFrame)
    {
        double tx = point.X - myFrame.Centroid.X;
        double ty = point.Y - myFrame.Centroid.Y;

        double theta = myFrame.RotationDeg * Math.PI / 180.0;
        // Same convention as Unity's Quaternion.Euler(0, angle, 0) applied to a
        // (x, 0, z) vector, with our (X, Y) here standing in for Unity's (x, z).
        double rx = tx * Math.Cos(theta) + ty * Math.Sin(theta);
        double ry = -tx * Math.Sin(theta) + ty * Math.Cos(theta);

        return new Point2D(rx, ry);
    }

    /// <summary>
    /// For a given viewer (myType), returns every house's centroid position in
    /// the viewer's own local space -- myType's own entry is always (0,0).
    /// </summary>
    public static Point2D[] GetCentroidsRelativeTo(DifferentialEvolutionOptimizer.Result optResult, int myType)
    {
        HouseFrame[] frames = GetAbsoluteFrames(optResult);
        HouseFrame myFrame = frames[myType];

        var result = new Point2D[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            result[i] = TransformToLocalSpace(frames[i].Centroid, myFrame);
        }
        return result;
    }
}
