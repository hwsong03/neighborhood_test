using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

// Port of the scoring functions in differential.py: how "good" a candidate
// house arrangement is. Lower score = better. The optimizer (Step 6) searches
// for the arrangement that makes this as small as possible.
public static class ObjectiveFunction
{
    static readonly GeometryFactory Factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

    public struct DistanceConfig
    {
        public double DStar, Eps, Beta, PClose, PFar;
        public static DistanceConfig Default => new DistanceConfig
        {
            DStar = 0.0,
            Eps = 0.1,
            Beta = 0.1,
            PClose = 1.0,
            PFar = 1.5,
        };
    }

    /// <summary>
    /// Turns a signed distance between two circles into a penalty score:
    /// 0 if within a comfortable band around the target distance, growing
    /// smoothly the further outside that band. Mirrors distance_score_function().
    /// </summary>
    public static double DistanceScoreFunction(double d, DistanceConfig config)
    {
        double Softplus(double x)
        {
            double z = config.Beta * x;
            return z > 20 ? z / config.Beta : System.Math.Log(1 + System.Math.Exp(z)) / config.Beta;
        }

        double x = d - config.DStar;
        if (x < -config.Eps) return System.Math.Pow(Softplus(-x - config.Eps), config.PClose);
        if (x > config.Eps) return System.Math.Pow(Softplus(x - config.Eps), config.PFar);
        return 0.0;
    }

    /// <summary>
    /// Average spacing score across every pair of boundary circles.
    /// Mirrors simple_distance_arrangement().
    /// </summary>
    public static double SimpleDistanceArrangement(IReadOnlyList<CircleShape?> boundaries, DistanceConfig config)
    {
        int n = boundaries.Count;
        if (n < 2) return 0.0;

        var scores = new List<double>();
        for (int i = 0; i < n - 1; i++)
        {
            if (boundaries[i] == null) continue;
            for (int j = i + 1; j < n; j++)
            {
                if (boundaries[j] == null) continue;
                double dij = boundaries[i].Value.SignedDistanceTo(boundaries[j].Value);
                scores.Add(DistanceScoreFunction(dij, config));
            }
        }

        return scores.Count > 0 ? scores.Average() : 0.0;
    }

    /// <summary>
    /// Same as SimpleDistanceArrangement, but only scores each circle against
    /// its k nearest neighbors (by centroid distance) instead of every other
    /// circle. Kept general (not hardcoded to 3 houses) in case the house
    /// count ever changes. Mirrors knn_distance_arrangement().
    /// </summary>
    public static double KnnDistanceArrangement(IReadOnlyList<CircleShape?> boundaries, int k, DistanceConfig config)
    {
        int n = boundaries.Count;
        if (n < 2) return 0.0;

        var validIndices = new List<int>();
        var centroids = new List<(double x, double y)>();
        for (int i = 0; i < n; i++)
        {
            if (boundaries[i] != null)
            {
                validIndices.Add(i);
                centroids.Add((boundaries[i].Value.CenterX, boundaries[i].Value.CenterY));
            }
        }
        if (centroids.Count < 2) return 0.0;

        int m = centroids.Count;
        var distances = new double[m, m];
        for (int a = 0; a < m; a++)
        {
            for (int b = 0; b < m; b++)
            {
                double dx = centroids[a].x - centroids[b].x;
                double dy = centroids[a].y - centroids[b].y;
                distances[a, b] = System.Math.Sqrt(dx * dx + dy * dy);
            }
        }

        var scores = new List<double>();
        var consideredPairs = new HashSet<(int, int)>();

        for (int i = 0; i < m; i++)
        {
            int origI = validIndices[i];
            var nearest = System.Linq.Enumerable.Range(0, m)
                .OrderBy(idx => distances[i, idx])
                .Skip(1)
                .Take(k);

            foreach (int jLocal in nearest)
            {
                if (jLocal >= validIndices.Count) continue;
                int origJ = validIndices[jLocal];
                var pair = origI < origJ ? (origI, origJ) : (origJ, origI);
                if (!consideredPairs.Add(pair)) continue;

                double dij = boundaries[origI].Value.SignedDistanceTo(boundaries[origJ].Value);
                scores.Add(DistanceScoreFunction(dij, config));
            }
        }

        return scores.Count > 0 ? scores.Average() : 0.0;
    }

    /// <summary>
    /// Penalizes house arrangements where a freespace ends up split apart from
    /// (not touching) the shared "personal bubble" zone -- i.e. an isolated,
    /// unreachable pocket of walkable space. Mirrors morphological_connectivity_check().
    /// </summary>
    public static double MorphologicalConnectivityCheck(
        IReadOnlyList<Polygon> freespaces,
        IReadOnlyList<Polygon> boundaryPolys,
        double bufferSize,
        double penaltyWeight)
    {
        var bufferedBoundaries = new List<Geometry>();
        foreach (var b in boundaryPolys)
        {
            if (b == null) continue;
            Geometry buffered = b.Buffer(bufferSize);
            if (!buffered.IsEmpty) bufferedBoundaries.Add(buffered);
        }

        Geometry boundariesUnion = bufferedBoundaries.Count > 0
            ? UnaryUnionOp.Union(bufferedBoundaries)
            : Factory.CreatePolygon();

        int disconnectedCount = 0;
        foreach (var freespace in freespaces)
        {
            if (freespace == null) continue;

            Geometry intersectionResult = boundariesUnion.Intersection(freespace);
            bool isConnected = true;

            if (intersectionResult.IsEmpty)
            {
                isConnected = false;
            }
            else if (intersectionResult.GeometryType == "MultiPolygon")
            {
                isConnected = false;
            }
            else if (intersectionResult.GeometryType == "GeometryCollection")
            {
                int polyCount = 0;
                for (int i = 0; i < intersectionResult.NumGeometries; i++)
                {
                    var g = intersectionResult.GetGeometryN(i);
                    if (g.GeometryType == "Polygon" || g.GeometryType == "MultiPolygon") polyCount++;
                }
                if (polyCount > 1) isConnected = false;
            }

            if (!isConnected) disconnectedCount++;
        }

        return penaltyWeight * disconnectedCount;
    }

    public struct ObjectiveParams
    {
        public double RoiMinThreshold, RoiTarget, RoiWeight, MaxfreeWeight, AdjacencyWeight, SecondaryWeight, LambdaCovEq;
        public string AdjacencyMode; // "original" or "knn"
        public int K;
        public DistanceConfig DistanceConfig;

        public static ObjectiveParams Default => new ObjectiveParams
        {
            RoiMinThreshold = 0.5,
            RoiTarget = 0.85,
            RoiWeight = 10.0,
            MaxfreeWeight = 1.0,
            AdjacencyWeight = 1.0,
            SecondaryWeight = 0.1,
            LambdaCovEq = 2.0,
            AdjacencyMode = "knn",
            K = 2,
            DistanceConfig = DistanceConfig.Default,
        };
    }

    public struct ObjectiveResult
    {
        public double Loss;
        public double RoiScore, RoiMean, RoiMin;
        public double RoiOwnershipMean, RoiOwnershipMin;
        public double ConnectivityCost;
        public double CoverageScore; // maxfree_score
        public double AdjacencyScore;
    }

    static double Softplus(double x, double beta = 1.0)
    {
        double z = beta * x;
        return z > 20 ? z / beta : System.Math.Log(1 + System.Math.Exp(z)) / beta;
    }

    static IEnumerable<List<T>> Combinations<T>(IReadOnlyList<T> items, int k)
    {
        if (k == 0) { yield return new List<T>(); yield break; }
        for (int i = 0; i <= items.Count - k; i++)
        {
            var rest = new List<T>();
            for (int r = i + 1; r < items.Count; r++) rest.Add(items[r]);
            foreach (var tail in Combinations(rest, k - 1))
            {
                var combo = new List<T> { items[i] };
                combo.AddRange(tail);
                yield return combo;
            }
        }
    }

    /// <summary>
    /// The main scoring function: combines ROI coverage, ROI ownership, freespace
    /// maximization, connectivity, and adjacency into one loss value (lower = better).
    /// Mirrors differential.py's objective_roi_primary().
    /// </summary>
    public static ObjectiveResult ObjectiveRoiPrimary(
        IReadOnlyList<Polygon> freespaces,
        IReadOnlyList<CircleShape?> boundaries,
        IReadOnlyList<CircleShape?> rois,
        ObjectiveParams p)
    {
        int n = freespaces.Count;
        var boundaryPolys = boundaries.Select(b => b?.ToPolygon()).ToList();
        var roiPolys = rois.Select(r => r?.ToPolygon()).ToList();
        var roiAreas = roiPolys.Select(r => r != null && !r.IsEmpty ? r.Area : 0.0).ToList();

        var roiCoverages = new List<double>();
        var roiOwnerships = new List<double>();
        var maxPerPoly = new List<double>();

        for (int i = 0; i < n; i++)
        {
            Polygon f = freespaces[i];
            Polygon roi = roiPolys[i];
            double roiArea = roiAreas[i];

            if (roi != null && roiArea > 0)
            {
                var otherRois = new List<Polygon>();
                for (int j = 0; j < n; j++)
                    if (j != i && roiPolys[j] != null && roiAreas[j] > 0) otherRois.Add(roiPolys[j]);

                Geometry otherRoisUnion = otherRois.Count > 0 ? UnaryUnionOp.Union(otherRois.Cast<Geometry>()) : null;

                // coverage: how much of my ROI falls inside everyone ELSE's freespace (worst case)
                var coverages = new List<double>();
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    try
                    {
                        double ratio = roi.Intersection(freespaces[j]).Area / roiArea;
                        coverages.Add(ratio);
                    }
                    catch { coverages.Add(0.0); }
                }
                if (coverages.Count > 0) roiCoverages.Add(coverages.Min());

                // ownership: exclusive area + fractional credit for shared area
                if (otherRois.Count > 0 && otherRoisUnion != null && !otherRoisUnion.IsEmpty)
                {
                    try
                    {
                        int nOthers = otherRois.Count;
                        Geometry exclusive = roi.Difference(otherRoisUnion);
                        double ownedArea = exclusive.Area;

                        var atLeast = new double[nOthers + 2];
                        for (int ck = 1; ck <= nOthers; ck++)
                        {
                            var regions = new List<Geometry>();
                            foreach (var combo in Combinations(otherRois, ck))
                            {
                                Geometry inter = roi;
                                bool empty = false;
                                foreach (var r in combo)
                                {
                                    inter = inter.Intersection(r);
                                    if (inter.IsEmpty) { empty = true; break; }
                                }
                                if (!empty && !inter.IsEmpty) regions.Add(inter);
                            }
                            atLeast[ck] = regions.Count > 0 ? UnaryUnionOp.Union(regions).Area : 0.0;
                        }
                        for (int ck = 1; ck <= nOthers; ck++)
                        {
                            double exactlyK = atLeast[ck] - atLeast[ck + 1];
                            ownedArea += exactlyK / (ck + 1);
                        }
                        roiOwnerships.Add(ownedArea / roiArea);
                    }
                    catch
                    {
                        roiOwnerships.Add(1.0);
                    }
                }
                else
                {
                    roiOwnerships.Add(1.0);
                }
            }

            // freespace maximization contribution: overlap with everyone ELSE's boundary
            var contrib = new List<double>();
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                try
                {
                    double a = boundaryPolys[j] != null ? f.Intersection(boundaryPolys[j]).Area : 0.0;
                    contrib.Add(a);
                }
                catch { contrib.Add(0.0); }
            }
            maxPerPoly.Add(contrib.Count > 0 ? System.Math.Sqrt(contrib.Average()) : 0.0);
        }

        double roiScore = 0, meanRoi = 0, minRoi = 0;
        const double kParam = 20.0, center = 0.8;

        if (roiCoverages.Count > 0)
        {
            meanRoi = roiCoverages.Average();
            minRoi = roiCoverages.Min();

            double meanPenalty = Softplus(-kParam * (meanRoi - center));
            double mask = 0.5 * (1 - System.Math.Tanh(30 * (meanRoi - 0.85)));
            roiScore = p.RoiWeight * meanPenalty * mask;

            if (minRoi < p.RoiMinThreshold)
            {
                roiScore += 30 * (p.RoiMinThreshold - minRoi);
            }
        }

        double meanOwnership = 1.0, minOwnership = 1.0;
        if (roiOwnerships.Count > 0)
        {
            meanOwnership = roiOwnerships.Average();
            minOwnership = roiOwnerships.Min();

            double ownPenalty = Softplus(-kParam * (meanOwnership - center));
            double ownMask = 0.5 * (1 - System.Math.Tanh(30 * (meanOwnership - 0.85)));
            roiScore += p.RoiWeight * ownPenalty * ownMask;

            if (minOwnership < p.RoiMinThreshold)
            {
                roiScore += 30 * (p.RoiMinThreshold - minOwnership);
            }
        }

        double maxfreeScore;
        if (maxPerPoly.Count > 0)
        {
            double cMean = maxPerPoly.Average();
            double cVar = maxPerPoly.Select(v => (v - cMean) * (v - cMean)).Average(); // population variance (ddof=0), matches np.var default
            double cv2 = cVar / (cMean * cMean + 1e-12);
            maxfreeScore = maxPerPoly.Sum() - p.LambdaCovEq * cv2;
        }
        else
        {
            maxfreeScore = 0.0;
        }

        double connectivityCost = MorphologicalConnectivityCheck(freespaces, boundaryPolys, 0.05, 100);

        double adjacencyScore = p.AdjacencyMode == "knn"
            ? KnnDistanceArrangement(boundaries, p.K, p.DistanceConfig)
            : SimpleDistanceArrangement(boundaries, p.DistanceConfig);

        double effectiveSecondaryWeight;
        if (roiCoverages.Count > 0)
        {
            if (minRoi >= p.RoiMinThreshold)
            {
                if (meanRoi >= p.RoiTarget)
                {
                    effectiveSecondaryWeight = 2.0;
                }
                else
                {
                    double progress = (meanRoi - p.RoiMinThreshold) / (p.RoiTarget - p.RoiMinThreshold);
                    effectiveSecondaryWeight = p.SecondaryWeight + (2.0 - p.SecondaryWeight) * progress;
                }
            }
            else
            {
                effectiveSecondaryWeight = p.SecondaryWeight;
            }
        }
        else
        {
            effectiveSecondaryWeight = 2.0;
        }

        double secondaryScore = -p.MaxfreeWeight * maxfreeScore + p.AdjacencyWeight * adjacencyScore;
        double loss = roiScore + connectivityCost + effectiveSecondaryWeight * secondaryScore;

        return new ObjectiveResult
        {
            Loss = loss,
            RoiScore = roiScore,
            RoiMean = meanRoi,
            RoiMin = minRoi,
            RoiOwnershipMean = meanOwnership,
            RoiOwnershipMin = minOwnership,
            ConnectivityCost = connectivityCost,
            CoverageScore = maxfreeScore,
            AdjacencyScore = adjacencyScore,
        };
    }
}
