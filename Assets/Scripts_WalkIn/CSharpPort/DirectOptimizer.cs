using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

// DIRECT (DIviding RECTangles) global optimization -- Jones, Perttunen & Stuckman 1993,
// "Lipschitzian Optimization Without the Lipschitz Constant". Alternative to
// DifferentialEvolutionOptimizer.cs for the same (dx, dy, angle)-per-house search.
//
// Deliberately fully independent from DifferentialEvolutionOptimizer.cs -- some of the
// per-candidate evaluation logic below is copy-pasted rather than shared, so the existing
// DE path stays byte-for-byte unchanged as a stable baseline (LocalOptimizationRunner can
// switch between the two for direct comparison; see useDirectAlgorithm there).
//
// Why DIRECT for this problem specifically: this search is only 6-dimensional (2 moving
// houses x (dx, dy, angle)) with simple box bounds -- exactly DIRECT's sweet spot. It's
// deterministic (no RNG/seed needed) and, unlike DE's population-based search, doesn't need
// any population-size/mutation-rate tuning -- just an evaluation budget and the bounds.
// DIRECT tends to need far fewer objective evaluations than DE to reach a comparable or
// better result in this few-dimension regime, since it systematically balances global
// coverage (splitting large, unexplored regions) against local refinement (splitting near
// the best point found so far) every round, instead of DE's population slowly drifting there.
public static class DirectOptimizer
{
    class Rectangle
    {
        public double[] CenterNorm; // center in normalized [0,1]^n space
        public double[] SideLength; // per-dimension side length in normalized space (all start at 1, shrink by /3 on each split of that dimension)
        public double Value;
    }

    public static DifferentialEvolutionOptimizer.Result Optimize(
        IReadOnlyList<Polygon> freespaces,
        IReadOnlyList<CircleShape> boundaries,
        IReadOnlyList<CircleShape> rois,
        ObjectiveFunction.ObjectiveParams objectiveParams,
        int maxEvaluations = 3000,
        int noImprovementRoundLimit = 25,
        double translationBound = 5.0,
        double rotationBound = 30.0)
    {
        int numPolygons = freespaces.Count;
        int numMoving = numPolygons - 1;
        int dimensions = numMoving * 3;

        var lossHistoryEmpty = new List<double>();
        if (dimensions <= 0)
        {
            // Nothing to move (mirrors DifferentialEvolutionOptimizer's natural behavior
            // in this edge case: empty MovingStates, house 0 unchanged).
            return new DifferentialEvolutionOptimizer.Result
            {
                BestLoss = numPolygons > 0
                    ? ObjectiveFunction.ObjectiveRoiPrimary(freespaces, boundaries.Select(b => (CircleShape?)b).ToArray(), rois.Select(r => (CircleShape?)r).ToArray(), objectiveParams).Loss
                    : 0.0,
                MovingStates = new DifferentialEvolutionOptimizer.TransformState[0],
                FinalFreespaces = freespaces.ToArray(),
                FinalBoundaries = boundaries.ToArray(),
                FinalRois = rois.ToArray(),
                LossHistory = lossHistoryEmpty,
            };
        }

        var lower = new double[dimensions];
        var upper = new double[dimensions];
        for (int i = 0; i < numMoving; i++)
        {
            lower[i * 3 + 0] = -translationBound; upper[i * 3 + 0] = translationBound;
            lower[i * 3 + 1] = -translationBound; upper[i * 3 + 1] = translationBound;
            lower[i * 3 + 2] = -rotationBound; upper[i * 3 + 2] = rotationBound;
        }

        double[] Denormalize(double[] norm)
        {
            var x = new double[dimensions];
            for (int d = 0; d < dimensions; d++) x[d] = lower[d] + norm[d] * (upper[d] - lower[d]);
            return x;
        }

        // Same per-candidate evaluation as DifferentialEvolutionOptimizer.Evaluate --
        // transform every moving house's freespace/boundary/ROI by its candidate
        // (dx, dy, angle) and score the arrangement. House 0 never moves (fixed reference
        // frame), so its geometry is reused as-is rather than re-transformed by identity.
        double Evaluate(double[] normCenter)
        {
            double[] x = Denormalize(normCenter);

            var tFreespaces = new Polygon[numPolygons];
            var tBoundaries = new CircleShape?[numPolygons];
            var tRois = new CircleShape?[numPolygons];

            tFreespaces[0] = freespaces[0];
            tBoundaries[0] = boundaries[0];
            tRois[0] = rois[0];

            for (int i = 1; i < numPolygons; i++)
            {
                double dx = x[(i - 1) * 3 + 0];
                double dy = x[(i - 1) * 3 + 1];
                double angle = x[(i - 1) * 3 + 2];

                tFreespaces[i] = PolygonUtils.RigidTransform(freespaces[i], angle, dx, dy);
                tBoundaries[i] = boundaries[i].Transform(0, 0, angle, dx, dy);
                tRois[i] = rois[i].Transform(0, 0, angle, dx, dy);
            }

            return ObjectiveFunction.ObjectiveRoiPrimary(tFreespaces, tBoundaries, tRois, objectiveParams).Loss;
        }

        // "Size" measure per Jones et al.: distance from the rectangle's center to one of
        // its vertices. Used both to pick which rectangles to split next (bigger = more
        // under-explored) and to detect ties for "longest dimension".
        double RectSize(double[] side)
        {
            double sumSq = 0;
            for (int d = 0; d < dimensions; d++)
            {
                double half = side[d] * 0.5;
                sumSq += half * half;
            }
            return Math.Sqrt(sumSq);
        }

        var initialCenter = new double[dimensions];
        var initialSide = new double[dimensions];
        for (int d = 0; d < dimensions; d++) { initialCenter[d] = 0.5; initialSide[d] = 1.0; }

        var root = new Rectangle { CenterNorm = initialCenter, SideLength = initialSide, Value = Evaluate(initialCenter) };
        var rectangles = new List<Rectangle> { root };
        int evaluations = 1;

        double globalBest = root.Value;
        double[] globalBestNorm = initialCenter;
        var lossHistory = new List<double> { globalBest };

        const double epsilon = 1e-4; // standard DIRECT "sufficient improvement" slack
        int noImprovementRounds = 0;

        while (evaluations < maxEvaluations && noImprovementRounds < noImprovementRoundLimit)
        {
            var potentiallyOptimal = SelectPotentiallyOptimal(rectangles, RectSize, globalBest, epsilon);
            if (potentiallyOptimal.Count == 0) break;

            // Collect every (rectangle, dimension, sign) evaluation this round needs up
            // front, so they can all run in parallel -- same idea as
            // DifferentialEvolutionOptimizer's Parallel.For over its initial population.
            var tasks = new List<(Rectangle rect, int dim, int sign)>();
            var longDimsByRect = new Dictionary<Rectangle, List<int>>();
            foreach (var rect in potentiallyOptimal)
            {
                double maxSide = rect.SideLength.Max();
                var longDims = Enumerable.Range(0, dimensions)
                    .Where(d => Math.Abs(rect.SideLength[d] - maxSide) < 1e-12)
                    .ToList();
                longDimsByRect[rect] = longDims;
                foreach (int d in longDims)
                {
                    tasks.Add((rect, d, -1));
                    tasks.Add((rect, d, 1));
                }
            }

            if (evaluations + tasks.Count > maxEvaluations)
            {
                // Trim to the remaining budget -- still processed in the same
                // (rect, dim, sign) pairs so no rectangle gets a lone +/- point.
                int keepPairs = Math.Max(0, (maxEvaluations - evaluations) / 2);
                tasks = tasks.Take(keepPairs * 2).ToList();
            }
            if (tasks.Count == 0) break;

            var results = new double[tasks.Count];
            System.Threading.Tasks.Parallel.For(0, tasks.Count, i =>
            {
                var (rect, d, sign) = tasks[i];
                double delta = rect.SideLength[d] / 3.0;
                var c = (double[])rect.CenterNorm.Clone();
                c[d] += sign * delta;
                results[i] = Evaluate(c);
            });
            evaluations += tasks.Count;

            var valueOf = new Dictionary<(Rectangle, int, int), double>();
            for (int i = 0; i < tasks.Count; i++) valueOf[tasks[i]] = results[i];

            var newRectangles = new List<Rectangle>();
            foreach (var rect in potentiallyOptimal)
            {
                if (!longDimsByRect.TryGetValue(rect, out var longDims) || longDims.Count == 0) continue;
                // Only dims actually evaluated this round (may be fewer than longDims if
                // the budget ran out partway through) get split.
                longDims = longDims.Where(d => valueOf.ContainsKey((rect, d, -1)) && valueOf.ContainsKey((rect, d, 1))).ToList();
                if (longDims.Count == 0) continue;

                // Split dimensions in increasing order of their best (min of +/-) value --
                // keeps the sub-rectangle that still holds the original center (and thus
                // the best-so-far point, if it was here) as large as possible for as long
                // as possible, per the paper's rule.
                var order = longDims
                    .Select(d => (dim: d, minVal: Math.Min(valueOf[(rect, d, -1)], valueOf[(rect, d, 1)])))
                    .OrderBy(t => t.minVal)
                    .Select(t => t.dim)
                    .ToList();

                var currentSide = (double[])rect.SideLength.Clone();
                foreach (int d in order)
                {
                    double delta = rect.SideLength[d] / 3.0; // uses the ORIGINAL side length -- other long dims haven't shrunk this one
                    double vMinus = valueOf[(rect, d, -1)];
                    double vPlus = valueOf[(rect, d, 1)];

                    var sideSplit = (double[])currentSide.Clone();
                    sideSplit[d] = delta;

                    var centerMinus = (double[])rect.CenterNorm.Clone(); centerMinus[d] -= delta;
                    var centerPlus = (double[])rect.CenterNorm.Clone(); centerPlus[d] += delta;

                    newRectangles.Add(new Rectangle { CenterNorm = centerMinus, SideLength = sideSplit, Value = vMinus });
                    newRectangles.Add(new Rectangle { CenterNorm = centerPlus, SideLength = sideSplit, Value = vPlus });

                    if (vMinus < globalBest) { globalBest = vMinus; globalBestNorm = centerMinus; }
                    if (vPlus < globalBest) { globalBest = vPlus; globalBestNorm = centerPlus; }

                    currentSide[d] = delta;
                }

                // The piece keeping the original center, now shrunk in every dimension
                // that was split above.
                newRectangles.Add(new Rectangle { CenterNorm = rect.CenterNorm, SideLength = currentSide, Value = rect.Value });

                rectangles.Remove(rect);
            }
            rectangles.AddRange(newRectangles);

            double bestThisRound = lossHistory[lossHistory.Count - 1];
            if (globalBest < bestThisRound - epsilon * Math.Max(1.0, Math.Abs(bestThisRound)))
                noImprovementRounds = 0;
            else
                noImprovementRounds++;
            lossHistory.Add(globalBest);
        }

        double[] best = Denormalize(globalBestNorm);
        var movingStates = new DifferentialEvolutionOptimizer.TransformState[numMoving];
        for (int i = 0; i < numMoving; i++)
        {
            movingStates[i] = new DifferentialEvolutionOptimizer.TransformState
            {
                Dx = best[i * 3 + 0],
                Dy = best[i * 3 + 1],
                AngleDeg = best[i * 3 + 2],
            };
        }

        var finalFreespaces = new Polygon[numPolygons];
        var finalBoundaries = new CircleShape[numPolygons];
        var finalRois = new CircleShape[numPolygons];
        finalFreespaces[0] = freespaces[0];
        finalBoundaries[0] = boundaries[0];
        finalRois[0] = rois[0];
        for (int i = 1; i < numPolygons; i++)
        {
            double dx = movingStates[i - 1].Dx;
            double dy = movingStates[i - 1].Dy;
            double angle = movingStates[i - 1].AngleDeg;
            finalFreespaces[i] = PolygonUtils.RigidTransform(freespaces[i], angle, dx, dy);
            finalBoundaries[i] = boundaries[i].Transform(0, 0, angle, dx, dy);
            finalRois[i] = rois[i].Transform(0, 0, angle, dx, dy);
        }

        // Same align_rotations_to_first() canonicalization DifferentialEvolutionOptimizer
        // applies -- required for a fair/consistent comparison between the two (otherwise
        // one could show a spurious extra net rotation the other doesn't).
        if (numMoving > 0)
        {
            double firstRotation = movingStates[0].AngleDeg;
            for (int i = 1; i < numPolygons; i++)
            {
                finalFreespaces[i] = PolygonUtils.RigidTransform(finalFreespaces[i], -firstRotation, 0, 0);
                finalBoundaries[i] = finalBoundaries[i].Transform(0, 0, -firstRotation, 0, 0);
                finalRois[i] = finalRois[i].Transform(0, 0, -firstRotation, 0, 0);
                movingStates[i - 1].AngleDeg -= firstRotation;
            }
        }

        UnityEngine.Debug.Log($"[DirectOptimizer] Converged after {evaluations} evaluations (cap was {maxEvaluations}), best loss={globalBest:F4}.");

        return new DifferentialEvolutionOptimizer.Result
        {
            BestLoss = globalBest,
            MovingStates = movingStates,
            FinalFreespaces = finalFreespaces,
            FinalBoundaries = finalBoundaries,
            FinalRois = finalRois,
            LossHistory = lossHistory,
        };
    }

    /// <summary>
    /// Selects the "potentially optimal" rectangles: those for which some trade-off
    /// between rectangle size (bigger = less explored) and objective value (lower =
    /// better) could make them the best choice to split next. Geometrically, these are
    /// exactly the rectangles on the lower convex hull of the (size, value) scatter plot
    /// (grouping same-size rectangles down to their best value first, since only that one
    /// could ever be on the hull), filtered by requiring a non-trivial improvement over
    /// the best value found so far (Jones et al.'s epsilon slack, prevents wasting the
    /// evaluation budget refining a rectangle whose potential gain is negligible).
    /// </summary>
    static List<Rectangle> SelectPotentiallyOptimal(List<Rectangle> rectangles, Func<double[], double> sizeOf, double globalBest, double epsilon)
    {
        var bySize = new Dictionary<double, Rectangle>();
        foreach (var r in rectangles)
        {
            double size = Math.Round(sizeOf(r.SideLength), 10); // guards against float noise splitting one true size into two hull points
            if (!bySize.TryGetValue(size, out var existing) || r.Value < existing.Value)
                bySize[size] = r;
        }

        var points = bySize.Select(kv => (size: kv.Key, rect: kv.Value)).OrderBy(p => p.size).ToList();
        if (points.Count == 0) return new List<Rectangle>();

        double Cross((double size, double val) o, (double size, double val) a, (double size, double val) b)
        {
            double ax = a.size - o.size, ay = a.val - o.val;
            double bx = b.size - o.size, by = b.val - o.val;
            return ax * by - ay * bx;
        }

        var hull = new List<(double size, Rectangle rect)>();
        foreach (var p in points)
        {
            while (hull.Count >= 2 &&
                   Cross((hull[hull.Count - 2].size, hull[hull.Count - 2].rect.Value),
                         (hull[hull.Count - 1].size, hull[hull.Count - 1].rect.Value),
                         (p.size, p.rect.Value)) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(p);
        }

        double threshold = globalBest - epsilon * Math.Max(1.0, Math.Abs(globalBest));
        var result = hull.Where(p => p.rect.Value <= threshold || p.rect.Value <= globalBest).Select(p => p.rect).ToList();

        if (result.Count == 0)
        {
            // Never let the search stall entirely -- always allow splitting the current
            // best-known rectangle even if the epsilon slack check above rejected it.
            result.Add(rectangles.OrderBy(r => r.Value).First());
        }
        return result;
    }
}
