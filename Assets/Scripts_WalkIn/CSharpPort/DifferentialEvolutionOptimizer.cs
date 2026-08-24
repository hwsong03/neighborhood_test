using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

// Hand-rolled replacement for scipy.optimize.differential_evolution, since
// scipy isn't available in Unity/C#. Searches for the (dx, dy, angle) per
// moving house that minimizes ObjectiveFunction.ObjectiveRoiPrimary's loss.
// House 0 is always fixed at its own (already re-centered) position; every
// other house gets a candidate rigid transform applied to its freespace +
// boundary + ROI together before scoring.
public static class DifferentialEvolutionOptimizer
{
    public struct TransformState
    {
        public double Dx, Dy, AngleDeg;
    }

    public class Result
    {
        public double BestLoss;
        public TransformState[] MovingStates; // one per house after house 0
        public Polygon[] FinalFreespaces;
        public CircleShape[] FinalBoundaries;
        public CircleShape[] FinalRois;
        public List<double> LossHistory; // best loss at each generation, for sanity-checking convergence
    }

    /// <summary>
    /// Runs the optimizer. Mirrors differential_evolution_optimization() minus
    /// the "polish" step (confirmed via direct A/B testing against the Python
    /// pipeline to make no difference for this objective function).
    /// </summary>
    public static Result Optimize(
        IReadOnlyList<Polygon> freespaces,
        IReadOnlyList<CircleShape> boundaries,
        IReadOnlyList<CircleShape> rois,
        ObjectiveFunction.ObjectiveParams objectiveParams,
        int maxIter = 50,
        int popSizeMultiplier = 15,
        double translationBound = 5.0,
        double rotationBound = 30.0,
        int? seed = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();

        int numPolygons = freespaces.Count;
        int numMoving = numPolygons - 1;
        int dimensions = numMoving * 3;
        int popSize = popSizeMultiplier * dimensions;

        var lower = new double[dimensions];
        var upper = new double[dimensions];
        for (int i = 0; i < numMoving; i++)
        {
            lower[i * 3 + 0] = -translationBound; upper[i * 3 + 0] = translationBound;
            lower[i * 3 + 1] = -translationBound; upper[i * 3 + 1] = translationBound;
            lower[i * 3 + 2] = -rotationBound; upper[i * 3 + 2] = rotationBound;
        }

        double Evaluate(double[] x)
        {
            var tFreespaces = new Polygon[numPolygons];
            var tBoundaries = new CircleShape?[numPolygons];
            var tRois = new CircleShape?[numPolygons];

            // House 0 never moves (it's the fixed reference frame), so rotating/
            // translating it by (0,0,0) every single evaluation was reallocating an
            // identical copy of its freespace polygon (and re-deriving its boundary/
            // ROI) 4500+ times for nothing -- reuse the original reference instead.
            // Identity rotation+translation is exact (cos 0=1, sin 0=0), so this is
            // byte-for-byte the same geometry, not an approximation.
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

        double[][] population = LatinHypercubeInit(popSize, dimensions, lower, upper, rng);
        var fitness = new double[popSize];
        // The initial population's fitness evaluations are independent of each other
        // (unlike the generation loop below, which immediately applies each result
        // before generating the next trial) -- safe to run across cores.
        System.Threading.Tasks.Parallel.For(0, popSize, i => fitness[i] = Evaluate(population[i]));

        int bestIdx = ArgMin(fitness);
        var lossHistory = new List<double> { fitness[bestIdx] };

        for (int gen = 0; gen < maxIter; gen++)
        {
            for (int i = 0; i < popSize; i++)
            {
                int r1, r2;
                do { r1 = rng.Next(popSize); } while (r1 == i);
                do { r2 = rng.Next(popSize); } while (r2 == i || r2 == r1);

                double f = 0.5 + rng.NextDouble() * 0.5; // dithered mutation factor, matches mutation=(0.5, 1.0)

                var mutant = new double[dimensions];
                for (int d = 0; d < dimensions; d++)
                {
                    double v = population[bestIdx][d] + f * (population[r1][d] - population[r2][d]);
                    if (v < lower[d] || v > upper[d])
                        v = lower[d] + rng.NextDouble() * (upper[d] - lower[d]); // out-of-bounds -> resample that dimension
                    mutant[d] = v;
                }

                var trial = new double[dimensions];
                int forcedIndex = rng.Next(dimensions); // guarantees at least one dimension changes
                for (int d = 0; d < dimensions; d++)
                {
                    trial[d] = (d == forcedIndex || rng.NextDouble() < 0.7) ? mutant[d] : population[i][d];
                }

                double trialFitness = Evaluate(trial);
                if (trialFitness <= fitness[i])
                {
                    population[i] = trial;
                    fitness[i] = trialFitness;
                    if (trialFitness < fitness[bestIdx]) bestIdx = i; // "immediate" updating, matches workers=1 in the Python version
                }
            }

            lossHistory.Add(fitness[bestIdx]);
        }

        double[] best = population[bestIdx];
        var movingStates = new TransformState[numMoving];
        for (int i = 0; i < numMoving; i++)
        {
            movingStates[i] = new TransformState
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

        // Port of differential.py's align_rotations_to_first(), called in main()
        // right after the DE search returns and before anything is saved/used --
        // this step was missing entirely from this port. It rotates every moving
        // house (not just the first) around house 0's centroid (the origin here)
        // by -[the first moving house's own found angle], so that house 1 ends
        // up with exactly zero net rotation in the shared optimized frame. This
        // changes both position AND orientation for houses 2+ (rotating around
        // an external pivot moves a point, not just its facing), so it changes
        // the arrangement's actual loss relative to what the DE search itself
        // reported -- that is expected; DE optimizes before this canonicalization
        // is applied, matching the live Python pipeline exactly.
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

        return new Result
        {
            BestLoss = fitness[bestIdx],
            MovingStates = movingStates,
            FinalFreespaces = finalFreespaces,
            FinalBoundaries = finalBoundaries,
            FinalRois = finalRois,
            LossHistory = lossHistory,
        };
    }

    static int ArgMin(double[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] < values[best]) best = i;
        return best;
    }

    /// <summary>
    /// Latin Hypercube Sampling: for each dimension independently, divides the
    /// range into popSize equal bins, assigns each individual a shuffled bin,
    /// and samples uniformly within it. Gives a more evenly-spread initial
    /// population than plain uniform random -- matches scipy's default init
    /// strategy for differential_evolution.
    /// </summary>
    static double[][] LatinHypercubeInit(int popSize, int dimensions, double[] lower, double[] upper, Random rng)
    {
        var samples = new double[popSize][];
        for (int i = 0; i < popSize; i++) samples[i] = new double[dimensions];

        for (int d = 0; d < dimensions; d++)
        {
            var bins = Enumerable.Range(0, popSize).OrderBy(_ => rng.Next()).ToArray();
            for (int i = 0; i < popSize; i++)
            {
                double u = (bins[i] + rng.NextDouble()) / popSize;
                samples[i][d] = lower[d] + u * (upper[d] - lower[d]);
            }
        }

        return samples;
    }
}
