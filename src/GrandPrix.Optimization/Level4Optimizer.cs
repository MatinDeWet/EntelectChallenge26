using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 4: full problem — tyre degradation, limited multi-set tyres, weather and fuel.
///
/// Strategy is set by paired-submission evidence: time dominates the score (~−150/s), so we
/// minimise race time. A greedy weather-/wear-aware plan is the starting point, then a
/// deterministic randomised-restart search perturbs the tyre-stint schedule across many seeds,
/// evaluates each plan with the (physics-exact) simulator, and keeps the fastest one that is
/// crash-free, blowout-free and finishes. Deterministic (fixed seeds) so the source reproduces
/// the submitted output exactly.
/// </summary>
public sealed class Level4Optimizer : ILevelOptimizer
{
    public int Level => 4;

    public double CornerSafetyMargin { get; init; } = 0.005;
    public double TyreChangeThreshold { get; init; } = 0.92;
    public double WearWeight { get; init; } = 0.0;

    /// <summary>Number of randomised-restart plans to try. 0 = greedy only. (Search plateaus by ~800.)</summary>
    public int SearchIterations { get; init; } = 800;

    /// <summary>Probability that a tyre change picks among the top-3 alternatives instead of the best.</summary>
    public double ExploreProb { get; init; } = 0.35;

    public RacePlan Optimize(Level level)
    {
        var sim = new RaceSimulator(level, SimulationOptions.ForLevel(4));

        var best = Greedy(level);
        var bestTime = Evaluate(sim, best);

        for (var seed = 0; seed < SearchIterations; seed++)
        {
            var rng = new Random(seed);
            // Vary the change threshold per restart as well as the tyre picks.
            var threshold = 0.89 + rng.NextDouble() * 0.06; // [0.89, 0.95)
            var plan = new DegradationAwarePlanner(
                level, CornerSafetyMargin, threshold, WearWeight, rng, ExploreProb).Build();

            var time = Evaluate(sim, plan);
            if (time < bestTime) { bestTime = time; best = plan; }
        }

        return best;
    }

    private RacePlan Greedy(Level level)
        => new DegradationAwarePlanner(level, CornerSafetyMargin, TyreChangeThreshold, WearWeight).Build();

    /// <summary>Race time of a plan, or +∞ if it crashes, blows a tyre, or goes into limp mode.</summary>
    private static double Evaluate(RaceSimulator sim, RacePlan plan)
    {
        var r = sim.Simulate(plan);
        if (r.CrashCount > 0 || r.BlowoutCount > 0 || r.EverLimp) return double.PositiveInfinity;
        return r.TotalTime;
    }
}
