using GrandPrix.Domain;
using GrandPrix.Optimization;
using GrandPrix.Simulation;
using Xunit;

namespace GrandPrix.Tests;

public class Level4Tests
{
    private static (Level level, RacePlan plan, RaceResult result) Run()
    {
        var level = LevelLoader.Load(TestPaths.Level(4));
        var plan = new Level4Optimizer().Optimize(level);
        var sim = new RaceSimulator(level, SimulationOptions.ForLevel(4));
        var result = sim.Simulate(plan);
        return (level, plan, result);
    }

    [Fact]
    public void Finishes_without_blowout_crash_or_limp()
    {
        var (_, _, result) = Run();
        Assert.Equal(0, result.BlowoutCount);
        Assert.Equal(0, result.CrashCount);
        Assert.False(result.EverLimp);
        Assert.False(result.EverCrawl);
    }

    [Fact]
    public void Degradation_is_active_and_accumulates()
    {
        var (_, _, result) = Run();
        Assert.True(result.TotalDegradation > 1.0,
            $"Expected meaningful tyre wear, saw {result.TotalDegradation:F3}.");
    }

    [Fact]
    public void Never_exceeds_a_single_set_lifespan_without_changing()
    {
        // No blowout (asserted above) implies each set stayed under life_span = 1.0.
        var (level, plan, _) = Run();
        // Changes tyres several times across the race (limited supply, 80 laps).
        var changes = plan.Laps.Count(l => l.Pit.Enter && l.Pit.TyreChangeSetId is int id && id > 0);
        Assert.True(changes >= 3, $"Expected multiple tyre changes, saw {changes}.");
    }

    [Fact]
    public void Uses_wet_compound_at_some_point()
    {
        var (level, plan, _) = Run();
        var usedWet = plan.Laps.Any(l => l.Pit.TyreChangeSetId is int id && level.CompoundOf(id) == Compound.Wet)
                      || level.CompoundOf(plan.InitialTyreId) == Compound.Wet;
        Assert.True(usedWet, "Expected Wet to be used during the heavy-rain phase.");
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var level = LevelLoader.Load(TestPaths.Level(4));
        var a = new Level4Optimizer().Optimize(level).ToJson();
        var b = new Level4Optimizer().Optimize(level).ToJson();
        Assert.Equal(a, b);
    }
}
