using NetTopologySuite.Geometries;

// Computes where every house/avatar/traverse-zone point should appear in
// MY (myType's) own local view, from the optimizer's result. This is the
// calculation half of TransferManager.cs's ApplyHouseArrangementForClient(),
// ApplyAvatarPositionsForClient(), and TransformPointsToLocalSpace() --
// deliberately kept as pure math here (no GameObject access) so it can be
// hand-verified before anything touches the actual Unity scene.
public static class HouseArrangementApplier
{
    public struct HousePlacement
    {
        public CoordinateTransform.Point2D Position; // where to put house[i]'s prefab, in my local space
        public double RotationDeg;                   // rotation to apply to house[i]'s prefab (Unity Quaternion.Euler(0, RotationDeg, 0))
    }

    static CoordinateTransform.Point2D RotatePoint(CoordinateTransform.Point2D p, double angleDeg)
    {
        double theta = angleDeg * System.Math.PI / 180.0;
        // same convention as CoordinateTransform.TransformToLocalSpace (Unity's Quaternion.Euler(0, angle, 0))
        double x = p.X * System.Math.Cos(theta) + p.Y * System.Math.Sin(theta);
        double y = -p.X * System.Math.Sin(theta) + p.Y * System.Math.Cos(theta);
        return new CoordinateTransform.Point2D(x, y);
    }

    /// <summary>
    /// Where to place every house's prefab in myType's local scene.
    /// myType's own entry is always position (0,0), rotation 0 (my own prefab never moves).
    /// originalLocalCentroids[i] is house i's fixed freespace-centroid-relative-to-its-own-prefab-pivot
    /// offset (FreespaceCalculator.Result.OriginalCentroidX/Y) -- independent of optimization,
    /// baked into how that house's scan data/prefab was built.
    /// Mirrors ApplyHouseArrangementForClient().
    /// </summary>
    public static HousePlacement[] ComputeHousePlacements(
        DifferentialEvolutionOptimizer.Result optResult,
        CoordinateTransform.Point2D[] originalLocalCentroids,
        int myType)
    {
        CoordinateTransform.HouseFrame[] frames = CoordinateTransform.GetAbsoluteFrames(optResult);
        CoordinateTransform.HouseFrame myFrame = frames[myType];
        CoordinateTransform.Point2D myLocalCentroid = originalLocalCentroids[myType];

        var placements = new HousePlacement[frames.Length];

        for (int i = 0; i < frames.Length; i++)
        {
            if (i == myType)
            {
                placements[i] = new HousePlacement { Position = new CoordinateTransform.Point2D(0, 0), RotationDeg = 0 };
                continue;
            }

            CoordinateTransform.Point2D localPosition = CoordinateTransform.TransformToLocalSpace(frames[i].Centroid, myFrame);
            double localRotation = frames[i].RotationDeg - myFrame.RotationDeg;
            double appliedRotationDeg = -localRotation; // matches Quaternion.Euler(0, -localRotation, 0) in the original

            CoordinateTransform.Point2D localCentroid = originalLocalCentroids[i];
            CoordinateTransform.Point2D rotatedLocalCentroid = RotatePoint(localCentroid, appliedRotationDeg);

            var targetWorldCentroid = new CoordinateTransform.Point2D(
                myLocalCentroid.X + localPosition.X,
                myLocalCentroid.Y + localPosition.Y);

            var position = new CoordinateTransform.Point2D(
                targetWorldCentroid.X - rotatedLocalCentroid.X,
                targetWorldCentroid.Y - rotatedLocalCentroid.Y);

            placements[i] = new HousePlacement { Position = position, RotationDeg = appliedRotationDeg };
        }

        return placements;
    }

    /// <summary>
    /// Transforms a single point (an avatar/boundary centroid, or a traverse-zone
    /// vertex) from the shared optimized space into myType's local view.
    /// Mirrors the shared math inside ApplyAvatarPositionsForClient() and
    /// TransformPointsToLocalSpace() (both use the identical formula).
    /// </summary>
    public static CoordinateTransform.Point2D TransformPointToMyView(
        CoordinateTransform.Point2D point,
        CoordinateTransform.HouseFrame myFrame,
        CoordinateTransform.Point2D myLocalCentroid)
    {
        CoordinateTransform.Point2D local = CoordinateTransform.TransformToLocalSpace(point, myFrame);
        return new CoordinateTransform.Point2D(myLocalCentroid.X + local.X, myLocalCentroid.Y + local.Y);
    }

    /// <summary>
    /// Convenience wrapper: where does house i's avatar (its boundary circle's
    /// center) appear in myType's local view.
    /// </summary>
    public static CoordinateTransform.Point2D ComputeAvatarPosition(
        DifferentialEvolutionOptimizer.Result optResult,
        CoordinateTransform.Point2D[] originalLocalCentroids,
        int myType,
        int houseIndex)
    {
        CoordinateTransform.HouseFrame[] frames = CoordinateTransform.GetAbsoluteFrames(optResult);
        var boundaryCentroid = new CoordinateTransform.Point2D(
            optResult.FinalBoundaries[houseIndex].CenterX,
            optResult.FinalBoundaries[houseIndex].CenterY);

        return TransformPointToMyView(boundaryCentroid, frames[myType], originalLocalCentroids[myType]);
    }
}
