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
    [Fact]
    public void Level1_total_time_matches_official_log()
    {
        var level = LevelLoader.Load(TestPaths.Level(1));
        var plan = new Level1Optimizer().Optimize(level);
        var result = new RaceSimulator(level, SimulationOptions.ForLevel(1)).Simulate(plan);
        Assert.Equal(4962.476924633342, result.TotalTime, 3); // matches Final time in submission_log_level_1
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
        Assert.InRange(result.TotalFuelUsed, 255.0, 270.0);
    }
}
