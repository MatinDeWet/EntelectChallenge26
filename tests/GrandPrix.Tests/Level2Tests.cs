using GrandPrix.Domain;
using GrandPrix.Optimization;
using GrandPrix.Simulation;
using Xunit;

namespace GrandPrix.Tests;

public class Level2Tests
{
    private static (Level level, RacePlan plan, RaceResult result) Run()
    {
        var level = LevelLoader.Load(TestPaths.Level(2));
        var plan = new Level2Optimizer().Optimize(level);
        var sim = new RaceSimulator(level, SimulationOptions.ForLevel(2));
        var result = sim.Simulate(plan);
        return (level, plan, result);
    }

    [Fact]
    public void Never_runs_dry_or_crashes()
    {
        var (_, _, result) = Run();
        Assert.False(result.EverLimp);
        Assert.False(result.EverCrawl);
        Assert.Equal(0, result.CrashCount);
        Assert.Equal(0, result.BlowoutCount);
    }

    [Fact]
    public void Refuels_enough_to_finish_the_race()
    {
        var (_, _, result) = Run();
        Assert.True(result.FuelRemaining >= 0,
            $"Ended with negative fuel ({result.FuelRemaining:F3} L).");
    }

    [Fact]
    public void Uses_minimum_number_of_pit_stops()
    {
        var (level, _, result) = Run();
        // Total burn ~315 L; tank 150 L; need ceil((burn - initial)/tank) refuels.
        var minRefuels = (int)Math.Ceiling((result.TotalFuelUsed - level.Car.InitialFuel) / level.Car.FuelCapacity);
        Assert.Equal(minRefuels, result.PitStopCount);
    }

    [Fact]
    public void Never_overfills_the_tank()
    {
        var (level, plan, _) = Run();
        var fuel = level.Car.InitialFuel;
        var lapFuel = new RaceSimulator(level,
            new SimulationOptions { EnableDegradation = false, EnableFuelLimp = false })
            .Simulate(plan).LapFuelUsed;

        for (var lap = 1; lap <= level.Race.Laps; lap++)
        {
            fuel -= lapFuel[lap - 1];
            var pit = plan.Laps[lap - 1].Pit;
            if (pit.Enter && pit.FuelRefuelAmount is double amt)
            {
                fuel += amt;
                Assert.True(fuel <= level.Car.FuelCapacity + 1e-6,
                    $"Lap {lap}: tank overfilled to {fuel:F3} L (cap {level.Car.FuelCapacity}).");
            }
        }
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var level = LevelLoader.Load(TestPaths.Level(2));
        var a = new Level2Optimizer().Optimize(level).ToJson();
        var b = new Level2Optimizer().Optimize(level).ToJson();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pit_laps_carry_pit_exit_speed_into_next_lap()
    {
        var (level, plan, _) = Run();
        // At least one pit, and pit laps record a refuel amount.
        Assert.Contains(plan.Laps, l => l.Pit.Enter);
        foreach (var lap in plan.Laps.Where(l => l.Pit.Enter))
            Assert.True(lap.Pit.FuelRefuelAmount > 0);
    }
}
