using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Algorithm;
using UnityEngine;

// Port of drawScannet.py's getPolygonWithWalls() + getObjPolygons().
// Both drop the vertical (Y / height) axis and work purely in the XZ ground plane.

public static class RoomPolygonExtractor
{
    static readonly GeometryFactory Factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

    static double Round4(double v) => System.Math.Round(v, 4);
    static Pt Mid(Pt a, Pt b) => new Pt(Round4((a.x + b.x) / 2.0), Round4((a.y + b.y) / 2.0));
    static double Dist(Pt a, Pt b) => System.Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    /// <summary>
    /// Builds the room's outline polygon by reducing each wall to its centerline
    /// and stitching the centerlines into one closed loop.
    /// Mirrors drawScannet.py's getPolygonWithWalls(house_current, room_current).
    /// </summary>
    public static Polygon GetPolygonWithWalls(HouseData house, string roomId)
    {
        var edges = new List<(Pt, Pt)>();

        foreach (var wall in house.allHouseWalls)
        {
            string wallRoom = wall.wallName.Split('_')[1];
            if (wallRoom != roomId) continue;

            var corners = wall.wallCornerPoints;
            var pts = new List<Pt>();
            for (int i = 0; i < 4; i++)
            {
                pts.Add(new Pt(Round4(corners[i].x), Round4(corners[i].z))); // drop Y, keep (x, z)
            }

            Pt mid01 = Mid(pts[0], pts[1]);
            Pt mid23 = Mid(pts[2], pts[3]);
            Pt mid12 = Mid(pts[1], pts[2]);
            Pt mid30 = Mid(pts[3], pts[0]);

            double distA = Dist(mid01, mid23);
            double distB = Dist(mid12, mid30);

            Pt p1, p2;
            if (distA > distB) { p1 = mid01; p2 = mid23; }
            else { p1 = mid12; p2 = mid30; }

            edges.Add((p1, p2));
        }

        List<Pt> tour = EulerianTour.Eulerian(edges, 0.1);
        if (tour == null || tour.Count < 4)
        {
            Debug.LogError($"GetPolygonWithWalls: failed to build a closed wall loop for room {roomId}.");
            return null;
        }

        var coords = tour.Select(p => new Coordinate(p.x, p.y)).ToArray();
        var ring = Factory.CreateLinearRing(coords);
        return Factory.CreatePolygon(ring);
    }

    /// <summary>
    /// Builds one polygon per piece of furniture in the room, using the convex hull of its
    /// bounding-box corners projected onto the XZ plane.
    /// Mirrors drawScannet.py's getObjPolygons(house_current, room_current, bboxStyle),
    /// but uses a convex hull instead of the raw (possibly self-overlapping) corner order --
    /// same footprint result, without depending on Shapely's buffer(0) auto-repair.
    /// </summary>
    public static List<Polygon> GetObjPolygons(HouseData house, string roomId, string bboxStyle = "OBB")
    {
        var result = new List<Polygon>();

        foreach (var obj in house.allHouseObjects)
        {
            string objRoom;
            if (string.IsNullOrEmpty(obj.objInWhichFloor))
            {
                objRoom = obj.objName.Split('_')[1];
            }
            else
            {
                objRoom = obj.objInWhichFloor.Split('_')[1];
            }

            if (objRoom != roomId) continue;

            List<Vec3Data> corners = bboxStyle == "AABB" ? obj.aabb.cornerPoints : obj.obb;

            var coords = corners.Select(c => new Coordinate(c.x, c.z)).ToArray();
            var hull = new ConvexHull(coords, Factory).GetConvexHull();

            if (hull is Polygon poly)
            {
                result.Add(poly);
            }
            else
            {
                Debug.LogWarning($"GetObjPolygons: object {obj.objName} did not produce a polygon hull (got {hull.GeometryType}), skipping.");
            }
        }

        return result;
    }
}
