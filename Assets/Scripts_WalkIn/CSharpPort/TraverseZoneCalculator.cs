using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

// Computes each house's "traverse zone": the part of its own freespace that
// overlaps with everyone's boundary circles combined -- i.e. the shared,
// reachable walking area. This step existed inline in differential.py's
// main() (not inside differential_evolution_optimization itself) and hadn't
// been ported yet:
//   all_boundary = unary_union(final_boundaries)
//   traverse_zone_i = all_boundary.intersection(final_polygons[i])
public static class TraverseZoneCalculator
{
    /// <summary>
    /// One traverse zone per house, each as a list of polygon rings (normally
    /// one ring; more than one only if the shared zone splits into disconnected
    /// pieces -- equivalent to the old JSON's IsNested/NestedCoords case).
    /// </summary>
    public static List<Coordinate[]>[] Compute(DifferentialEvolutionOptimizer.Result optResult)
    {
        int n = optResult.FinalFreespaces.Length;

        var boundaryPolygons = optResult.FinalBoundaries.Select(b => (Geometry)b.ToPolygon()).ToList();
        Geometry allBoundaryUnion = UnaryUnionOp.Union(boundaryPolygons);

        var zones = new List<Coordinate[]>[n];
        for (int i = 0; i < n; i++)
        {
            Geometry intersection = allBoundaryUnion.Intersection(optResult.FinalFreespaces[i]);
            zones[i] = ExtractRings(intersection);
        }
        return zones;
    }

    static List<Coordinate[]> ExtractRings(Geometry geom)
    {
        var rings = new List<Coordinate[]>();
        if (geom == null || geom.IsEmpty) return rings;

        if (geom is Polygon poly)
        {
            rings.Add(poly.ExteriorRing.Coordinates);
        }
        else
        {
            for (int i = 0; i < geom.NumGeometries; i++)
            {
                if (geom.GetGeometryN(i) is Polygon p)
                {
                    rings.Add(p.ExteriorRing.Coordinates);
                }
            }
        }
        return rings;
    }
}
