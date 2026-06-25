using System.Globalization;
using System.Text.RegularExpressions;
using GrandPrix.Domain;
using GrandPrix.Optimization;
using GrandPrix.Simulation;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = ParseArgs(args);
if (options is null)
{
    Console.Error.WriteLine(
        "Usage: GrandPrix.Cli --level <path> [--out <path>] [--level-number <n>] [--report]");
    return 1;
}

var (levelPath, outPath, levelNumber, report) = options.Value;

var level = LevelLoader.Load(levelPath);
levelNumber ??= InferLevelNumber(levelPath);
outPath ??= $"output/level{levelNumber}.txt";

var optimizer = OptimizerRegistry.For(levelNumber.Value);
var plan = optimizer.Optimize(level);

var simOptions = SimulationOptions.ForLevel(levelNumber.Value);
var simulator = new RaceSimulator(level, simOptions);
var result = simulator.Simulate(plan);
var score = Scorer.Score(level, result, levelNumber.Value);

var json = plan.ToJson();
var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
File.WriteAllText(outPath, json);

Console.WriteLine($"Level {levelNumber}: {level.Race.Name} ({level.Track.Name})");
Console.WriteLine($"  Output written to: {outPath}");

if (report)
{
    Console.WriteLine();
    Console.WriteLine("  --- Simulation report (our engine — estimate, not the official grader) ---");
    Console.WriteLine($"  Initial tyre id : {plan.InitialTyreId} ({level.CompoundOf(plan.InitialTyreId)})");
    Console.WriteLine($"  Total time      : {result.TotalTime:F3} s");
    Console.WriteLine($"  Total fuel used : {result.TotalFuelUsed:F3} L (remaining {result.FuelRemaining:F3} / cap {level.Car.FuelCapacity:F0})");
    Console.WriteLine($"  Soft cap        : {level.Race.FuelSoftCapLimit:F0} L");
    Console.WriteLine($"  Total tyre wear : {result.TotalDegradation:F4}");
    Console.WriteLine($"  Crashes         : {result.CrashCount}");
    Console.WriteLine($"  Blowouts        : {result.BlowoutCount}");
    Console.WriteLine($"  Limp / Crawl    : {result.EverLimp} / {result.EverCrawl}");
    if (result.LapTimes.Count > 0)
        Console.WriteLine($"  Lap 1 / fastest : {result.LapTimes[0]:F3} s / {result.LapTimes.Min():F3} s");
    Console.WriteLine($"  Estimated score : {score:N0}");
    foreach (var warning in result.Warnings) Console.WriteLine($"  WARN: {warning}");
}

return 0;

static (string levelPath, string? outPath, int? levelNumber, bool report)? ParseArgs(string[] args)
{
    string? levelPath = null, outPath = null;
    int? levelNumber = null;
    var report = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--level": levelPath = Next(args, ref i); break;
            case "--out": outPath = Next(args, ref i); break;
            case "--level-number": levelNumber = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
            case "--report": report = true; break;
            default:
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                return null;
        }
    }

    if (levelPath is null) return null;
    return (levelPath, outPath, levelNumber, report);

    static string Next(string[] a, ref int i)
    {
        if (i + 1 >= a.Length) throw new ArgumentException($"Missing value after {a[i]}");
        return a[++i];
    }
}

static int InferLevelNumber(string path)
{
    var name = Path.GetFileNameWithoutExtension(path);
    var m = Regex.Match(name, @"(\d+)");
    return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 1;
}
