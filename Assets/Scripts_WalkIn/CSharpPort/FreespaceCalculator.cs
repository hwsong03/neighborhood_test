using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;
using UnityEngine;

// Port of differential.py's freespace(): room polygon minus furniture = walkable space,
// then re-centers everything on the freespace's own centroid.
public static class FreespaceCalculator
{
    static readonly GeometryFactory Factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

    public class Result
    {
        public Polygon Freespace;          // final walkable-space polygon, centered at (0,0)
        public Polygon Boundary;           // the input user-position circle, moved into the same (0,0)-centered frame
        public double CentroidX, CentroidY;                 // freespace centroid, relative to the wall centroid
        public double OriginalCentroidX, OriginalCentroidY; // freespace centroid, in the room's original (raw) coordinates
    }

    static Geometry Translate(Geometry g, double dx, double dy) =>
        AffineTransformation.TranslationInstance(dx, dy).Transform(g);

    static Polygon CleanGeom(Polygon p)
    {
        if (p == null) return null;
        if (p.IsValid && !p.IsEmpty) return p;
        var fixedGeom = p.Buffer(0);
        if (fixedGeom == null || fixedGeom.IsEmpty) return null;
        return fixedGeom as Polygon ?? PolygonUtils.ExtractLargestPolygon(fixedGeom);
    }

    /// <summary>
    /// Computes the walkable freespace for one room, given the room and a user-position
    /// boundary circle (built with PolygonUtils.CreateCircle). Mirrors differential.py's freespace().
    /// </summary>
    public static Result Compute(HouseData house, string roomId, Polygon boundaryCircle)
    {
        Polygon wallPolygon = RoomPolygonExtractor.GetPolygonWithWalls(house, roomId);
        List<Polygon> objectPolygons = RoomPolygonExtractor.GetObjPolygons(house, roomId, "OBB");

        if (wallPolygon == null)
        {
            Debug.LogError($"FreespaceCalculator: no wall polygon for room {roomId}.");
            return null;
        }

        double wallCentroidX = wallPolygon.Centroid.X;
        double wallCentroidY = wallPolygon.Centroid.Y;

        // clean up each furniture polygon, then shift into the wall-centroid-relative frame
        var cleanedObjects = objectPolygons
            .Select(CleanGeom)
            .Where(p => p != null)
            .Select(p => (Polygon)Translate(p, -wallCentroidX, -wallCentroidY))
            .ToList();

        var translatedWall = (Polygon)Translate(wallPolygon, -wallCentroidX, -wallCentroidY);

        Geometry merged = cleanedObjects.Count > 0
            ? UnaryUnionOp.Union(cleanedObjects.Cast<Geometry>())
            : Factory.CreatePolygon();

        // room outline minus furniture = walkable space
        Geometry freespaceRaw = translatedWall.Difference(merged);
        Polygon freespace = PolygonUtils.ExtractLargestPolygon(freespaceRaw);

        if (freespace == null)
        {
            Debug.LogError($"FreespaceCalculator: failed to extract a freespace polygon for room {roomId}.");
            return null;
        }

        double fsCx = freespace.Centroid.X;
        double fsCy = freespace.Centroid.Y;

        double originalCentroidX = wallCentroidX + fsCx;
        double originalCentroidY = wallCentroidY + fsCy;

        var translatedBoundary = (Polygon)Translate(boundaryCircle, -wallCentroidX, -wallCentroidY);

        // re-center so the freespace's own centroid becomes (0, 0)
        var finalFreespace = (Polygon)Translate(freespace, -fsCx, -fsCy);
        var finalBoundary = (Polygon)Translate(translatedBoundary, -fsCx, -fsCy);

        return new Result
        {
            Freespace = finalFreespace,
            Boundary = finalBoundary,
            CentroidX = fsCx,
            CentroidY = fsCy,
            OriginalCentroidX = originalCentroidX,
            OriginalCentroidY = originalCentroidY,
        };
    }
}
