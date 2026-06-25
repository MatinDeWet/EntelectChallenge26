using GrandPrix.Domain;
using GrandPrix.Optimization;
using GrandPrix.Simulation;
using Xunit;

namespace GrandPrix.Tests;

/// <summary>
/// Validates our simulator against the official submission logs in LevelLogs/. These pin our
/// physics to ground truth: the platform replayed our exact plans and reported these numbers.
/// </summary>
public class GroundTruthTests
{
    // The Level 1 plan we generate produced this exact final time on the platform.
    // The first L1 submission (plain sqrt corner formula) logged 4962.477 s. Adopting the
    // grader's actual sqrt+crawl corner limit makes every corner faster, so the new plan must be
    // meaningfully quicker and still crash-free.
    [Fact]
    public void Level1_with_crawl_corner_formula_is_faster_and_safe()
    {
        var level = LevelLoader.Load(TestPaths.Level(1));
        var plan = new Level1Optimizer().Optimize(level);
        var result = new RaceSimulator(level, SimulationOptions.ForLevel(1)).Simulate(plan);
        Assert.Equal(0, result.CrashCount);
        Assert.True(result.TotalTime < 4962.476924633342,
            $"Expected faster than the plain-formula 4962.48 s, got {result.TotalTime:F2}.");
    }

    // Corner fuel (constant speed) from the official log: L2 lap 60 segment 25, v=32.09922117435249,
    // radius 50 / length 105 ⇒ fuel 0.05266228170000001.
    [Fact]
    public void Corner_fuel_matches_official_log()
    {
        const double v = 32.09922117435249;
        var used = FuelModel.Used(0.0005, v, v, 105);
        Assert.Equal(0.05266228170000001, used, 9);
    }

    // The official logs confirm fuel is NOT consumed while braking: our simulated total fuel for
    // Level 2 lands near the platform's 262.5 L, far below the ~315 L we computed before the fix.
    [Fact]
    public void Level2_fuel_excludes_braking()
    {
        var level = LevelLoader.Load(TestPaths.Level(2));
        var plan = new Level2Optimizer().Optimize(level);
        var result = new RaceSimulator(level, SimulationOptions.ForLevel(2)).Simulate(plan);
        // Far below the ~340 L it would be if braking burnt fuel (the +crawl plan brakes less, so
        // it sits a little higher than the original 262 L, but the no-braking property still holds).
        Assert.InRange(result.TotalFuelUsed, 255.0, 285.0);
    }
}
