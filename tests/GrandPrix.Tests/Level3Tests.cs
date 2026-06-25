using GrandPrix.Domain;
using GrandPrix.Optimization;
using GrandPrix.Simulation;
using Xunit;

namespace GrandPrix.Tests;

public class Level3Tests
{
    private static (Level level, RacePlan plan, RaceResult result) Run()
    {
        var level = LevelLoader.Load(TestPaths.Level(3));
        var plan = new Level3Optimizer().Optimize(level);
        var sim = new RaceSimulator(level, SimulationOptions.ForLevel(3));
        var result = sim.Simulate(plan);
        return (level, plan, result);
    }

    [Fact]
    public void No_crash_despite_weather_transitions()
    {
        var (_, _, result) = Run();
        Assert.Equal(0, result.CrashCount);
        Assert.Equal(0, result.BlowoutCount);
        Assert.False(result.EverLimp);
        Assert.False(result.EverCrawl);
    }

    [Fact]
    public void Runs_soft_throughout()
    {
        var (level, plan, _) = Run();
        Assert.Equal(Compound.Soft, level.CompoundOf(plan.InitialTyreId));
        // No tyre-change pits (only fuel-only pits).
        Assert.DoesNotContain(plan.Laps, l => l.Pit.Enter && l.Pit.TyreChangeSetId is int id && id > 0);
    }

    [Fact]
    public void Refuels_enough_to_finish()
    {
        var (_, _, result) = Run();
        Assert.True(result.FuelRemaining >= 0);
    }

    [Fact]
    public void Braking_adapts_to_weather()
    {
        // Segment 1's braking point should take several distinct values — one per weather
        // condition — since friction (and thus the safe corner speed) changes with weather.
        var (_, plan, _) = Run();
        var brakeStarts = plan.Laps
            .Select(l => l.Segments.First(s => s.Id == 1).BrakeStartBeforeNext!.Value)
            .Select(v => Math.Round(v, 1))
            .Distinct()
            .Count();
        Assert.True(brakeStarts >= 3, $"Expected braking to vary with weather, saw {brakeStarts} distinct values.");
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var level = LevelLoader.Load(TestPaths.Level(3));
        var a = new Level3Optimizer().Optimize(level).ToJson();
        var b = new Level3Optimizer().Optimize(level).ToJson();
        Assert.Equal(a, b);
    }
}
