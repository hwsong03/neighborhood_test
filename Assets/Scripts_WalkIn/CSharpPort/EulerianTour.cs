using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Port of eulerian.py.
// Given a set of line segments (edges between 2D points), stitches them end-to-end
// into one closed loop (used to turn a room's wall centerlines into a room outline polygon).

public struct Pt : IEquatable<Pt>
{
    public double x, y;
    public Pt(double x, double y) { this.x = x; this.y = y; }
    public bool Equals(Pt other) => x == other.x && y == other.y;
    public override bool Equals(object obj) => obj is Pt p && Equals(p);
    public override int GetHashCode() => (x, y).GetHashCode();
    public override string ToString() => $"({x}, {y})";
}

public static class EulerianTour
{
    static double Distance(Pt a, Pt b)
    {
        double dx = a.x - b.x, dy = a.y - b.y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Connects the single closest pair of not-yet-connected points within threshold.
    static void ConnectClosestPoints(List<(Pt, Pt)> graph, double threshold = 0.1)
    {
        var nodes = new List<Pt>();
        var seen = new HashSet<Pt>();
        foreach (var e in graph)
        {
            if (seen.Add(e.Item1)) nodes.Add(e.Item1);
            if (seen.Add(e.Item2)) nodes.Add(e.Item2);
        }

        double minDist = double.PositiveInfinity;
        (Pt, Pt)? closestPair = null;

        for (int i = 0; i < nodes.Count; i++)
        {
            for (int j = i + 1; j < nodes.Count; j++)
            {
                double d = Distance(nodes[i], nodes[j]);
                if (d < minDist && d <= threshold)
                {
                    minDist = d;
                    closestPair = (nodes[i], nodes[j]);
                }
            }
        }

        if (closestPair.HasValue) graph.Add(closestPair.Value);
    }

    // Finds nodes with an odd number of connected edges and pairs up the closest ones
    // (a valid Eulerian circuit requires every node to have an even degree).
    static void CheckAndConnectOddNodes(List<(Pt, Pt)> graph, double threshold = 0.1)
    {
        var degree = new Dictionary<Pt, int>();
        void Bump(Pt p) { degree[p] = degree.TryGetValue(p, out var d) ? d + 1 : 1; }
        foreach (var e in graph) { Bump(e.Item1); Bump(e.Item2); }

        List<Pt> oddNodes = degree.Where(kv => kv.Value % 2 != 0).Select(kv => kv.Key).ToList();

        while (oddNodes.Count > 1)
        {
            double minDist = double.PositiveInfinity;
            (Pt, Pt)? closestPair = null;

            for (int i = 0; i < oddNodes.Count; i++)
            {
                for (int j = i + 1; j < oddNodes.Count; j++)
                {
                    double d = Distance(oddNodes[i], oddNodes[j]);
                    if (d < minDist && d <= threshold)
                    {
                        minDist = d;
                        closestPair = (oddNodes[i], oddNodes[j]);
                    }
                }
            }

            if (closestPair.HasValue)
            {
                graph.Add(closestPair.Value);
                oddNodes.Remove(closestPair.Value.Item1);
                oddNodes.Remove(closestPair.Value.Item2);
            }
            else
            {
                break;
            }
        }
    }

    // Merges points that are basically the same (tiny floating point gaps) into one.
    static List<(Pt, Pt)> MergeClosePoints(List<(Pt, Pt)> graph, double tolerance = 1e-6)
    {
        var allPoints = new List<Pt>();
        var seen = new HashSet<Pt>();
        foreach (var e in graph)
        {
            if (seen.Add(e.Item1)) allPoints.Add(e.Item1);
            if (seen.Add(e.Item2)) allPoints.Add(e.Item2);
        }

        var mapping = new Dictionary<Pt, Pt>();
        for (int i = 0; i < allPoints.Count; i++)
        {
            if (!mapping.ContainsKey(allPoints[i])) mapping[allPoints[i]] = allPoints[i];
            for (int j = i + 1; j < allPoints.Count; j++)
            {
                if (Distance(allPoints[i], allPoints[j]) < tolerance)
                {
                    mapping[allPoints[j]] = mapping[allPoints[i]];
                }
            }
        }

        var newGraph = new List<(Pt, Pt)>();
        var dedupe = new HashSet<(Pt, Pt)>();
        foreach (var e in graph)
        {
            Pt a = mapping[e.Item1];
            Pt b = mapping[e.Item2];
            if (!a.Equals(b) && dedupe.Add((a, b)))
            {
                newGraph.Add((a, b));
            }
        }
        return newGraph;
    }

    // Walks the graph, following edges from node to node, until it loops back to the start.
    // Consumes (removes) the edges it uses.
    static List<Pt> GetATour(List<(Pt, Pt)> graph, int maxSteps = 200000)
    {
        var uniqueNodes = new List<Pt>();
        var seen = new HashSet<Pt>();
        foreach (var e in graph)
        {
            if (seen.Add(e.Item1)) uniqueNodes.Add(e.Item1);
            if (seen.Add(e.Item2)) uniqueNodes.Add(e.Item2);
        }

        var tour = new List<Pt>();
        int idx = 0;
        int steps = 0;

        while (true)
        {
            if (++steps > maxSteps)
            {
                Debug.LogError("EulerianTour: exceeded max steps, graph may be malformed/disconnected.");
                return tour;
            }

            if (uniqueNodes.Count == 0) return tour;
            if (idx >= uniqueNodes.Count) idx = 0;

            Pt node = uniqueNodes[idx];
            idx++;

            if (tour.Count == 0)
            {
                tour.Add(node);
            }
            else
            {
                Pt tail = tour[tour.Count - 1];
                bool hasEdge = graph.Contains((tail, node)) || graph.Contains((node, tail));
                if (hasEdge)
                {
                    tour.Add(node);
                    Pt prev = tour[tour.Count - 2];
                    Pt last = tour[tour.Count - 1];
                    if (!graph.Remove((prev, last)))
                    {
                        graph.Remove((last, prev));
                    }
                }
            }

            if (tour.Count > 2 && tour[0].Equals(tour[tour.Count - 1]))
            {
                return tour;
            }
        }
    }

    // Combines multiple tour fragments (in case one pass doesn't consume every edge) into one.
    static List<Pt> GetEulerianTourInternal(List<(Pt, Pt)> graph)
    {
        List<Pt> tour = GetATour(graph);
        if (graph.Count == 0) return tour;

        int i = 0;
        while (true)
        {
            if (i >= tour.Count - 1)
            {
                Debug.LogWarning("EulerianTour: graph doesn't seem to be connected.");
                return null;
            }

            Pt node = tour[i];
            bool touches = graph.Any(e => e.Item1.Equals(node) || e.Item2.Equals(node));

            if (touches)
            {
                List<Pt> t = GetATour(graph);
                int j = t.IndexOf(node);

                var newTour = new List<Pt>();
                newTour.AddRange(tour.GetRange(0, i));                          // tour[:i]
                newTour.AddRange(t.GetRange(j, t.Count - 1 - j));               // t[j:-1]
                newTour.AddRange(t.GetRange(0, j + 1));                         // t[:j+1]
                newTour.AddRange(tour.GetRange(i + 1, tour.Count - (i + 1)));   // tour[i+1:]
                tour = newTour;

                if (graph.Count == 0) return tour;
                i = 0;
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// Stitches wall-centerline segments into one closed room-outline point loop.
    /// Mirrors eulerian.py's eulerian(graph, tolerance).
    /// </summary>
    public static List<Pt> Eulerian(List<(Pt, Pt)> graph, double tolerance = 1e-6)
    {
        graph = MergeClosePoints(graph, tolerance);
        ConnectClosestPoints(graph);
        CheckAndConnectOddNodes(graph);

        // remove self-loops and duplicate edges
        var dedupe = new HashSet<(Pt, Pt)>();
        var cleaned = new List<(Pt, Pt)>();
        foreach (var e in graph)
        {
            if (!e.Item1.Equals(e.Item2) && dedupe.Add(e)) cleaned.Add(e);
        }
        graph = cleaned;

        var degree = new Dictionary<Pt, int>();
        void Bump(Pt p) { degree[p] = degree.TryGetValue(p, out var d) ? d + 1 : 1; }
        foreach (var e in graph) { Bump(e.Item1); Bump(e.Item2); }

        var oddNodes = degree.Where(kv => kv.Value % 2 != 0).Select(kv => kv.Key).ToList();
        if (oddNodes.Count > 0)
        {
            CheckAndConnectOddNodes(graph, double.PositiveInfinity);

            degree.Clear();
            foreach (var e in graph) { Bump(e.Item1); Bump(e.Item2); }
            oddNodes = degree.Where(kv => kv.Value % 2 != 0).Select(kv => kv.Key).ToList();
            if (oddNodes.Count > 0)
            {
                Debug.LogError("EulerianTour: unable to create tour, odd-degree nodes remain.");
                return null;
            }
        }

        return GetEulerianTourInternal(graph);
    }
}
