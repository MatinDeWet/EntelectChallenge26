using System.Text.Json.Serialization;

namespace GrandPrix.Domain;

/// <summary>
/// Root of a level file. Mirrors SPECIFICATION.md §3 exactly. The JSON keys are the
/// quirky level-file keys (e.g. "accel_m/se2") mapped via <see cref="JsonPropertyName"/>.
/// </summary>
public sealed class Level
{
    [JsonPropertyName("car")] public Car Car { get; init; } = default!;
    [JsonPropertyName("race")] public Race Race { get; init; } = default!;
    [JsonPropertyName("track")] public Track Track { get; init; } = default!;
    [JsonPropertyName("tyres")] public Tyres Tyres { get; init; } = default!;
    [JsonPropertyName("available_sets")] public List<AvailableSet> AvailableSets { get; init; } = new();
    [JsonPropertyName("weather")] public Weather Weather { get; init; } = default!;
}

public sealed class Car
{
    [JsonPropertyName("max_speed_m/s")] public double MaxSpeed { get; init; }
    [JsonPropertyName("accel_m/se2")] public double Accel { get; init; }
    [JsonPropertyName("brake_m/se2")] public double Brake { get; init; }
    [JsonPropertyName("limp_constant_m/s")] public double LimpSpeed { get; init; }
    [JsonPropertyName("crawl_constant_m/s")] public double CrawlSpeed { get; init; }
    [JsonPropertyName("fuel_tank_capacity_l")] public double FuelCapacity { get; init; }
    [JsonPropertyName("initial_fuel_l")] public double InitialFuel { get; init; }

    /// <summary>Base fuel consumption rate K_base (L/m).</summary>
    [JsonPropertyName("fuel_consumption_l/m")] public double FuelKBase { get; init; }
}

public sealed class Race
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("laps")] public int Laps { get; init; }
    [JsonPropertyName("base_pit_stop_time_s")] public double BasePitStopTime { get; init; }
    [JsonPropertyName("pit_tyre_swap_time_s")] public double PitTyreSwapTime { get; init; }
    [JsonPropertyName("pit_refuel_rate_l/s")] public double PitRefuelRate { get; init; }
    [JsonPropertyName("corner_crash_penalty_s")] public double CornerCrashPenalty { get; init; }
    [JsonPropertyName("pit_exit_speed_m/s")] public double PitExitSpeed { get; init; }
    [JsonPropertyName("fuel_soft_cap_limit_l")] public double FuelSoftCapLimit { get; init; }
    [JsonPropertyName("starting_weather_condition_id")] public int StartingWeatherConditionId { get; init; }

    /// <summary>Per-level reference time. Role in scoring unconfirmed (SPEC Q1).</summary>
    [JsonPropertyName("time_reference_s")] public double TimeReferenceS { get; init; }
}

public sealed class Track
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("segments")] public List<Segment> Segments { get; init; } = new();
}

public sealed class Segment
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("type")] public string TypeRaw { get; init; } = "";
    [JsonPropertyName("length_m")] public double Length { get; init; }

    /// <summary>Only present for corners.</summary>
    [JsonPropertyName("radius_m")] public double? Radius { get; init; }

    [JsonIgnore]
    public SegmentType Type => TypeRaw.Equals("corner", StringComparison.OrdinalIgnoreCase)
        ? SegmentType.Corner
        : SegmentType.Straight;
}

public sealed class Tyres
{
    [JsonPropertyName("properties")] public Dictionary<string, TyreProperties> Properties { get; init; } = new();
}

public sealed class TyreProperties
{
    [JsonPropertyName("life_span")] public double LifeSpan { get; init; }
    [JsonPropertyName("base_friction")] public double BaseFriction { get; init; }
    [JsonPropertyName("dry_friction_multiplier")] public double DryFriction { get; init; }
    [JsonPropertyName("cold_friction_multiplier")] public double ColdFriction { get; init; }
    [JsonPropertyName("light_rain_friction_multiplier")] public double LightRainFriction { get; init; }
    [JsonPropertyName("heavy_rain_friction_multiplier")] public double HeavyRainFriction { get; init; }
    [JsonPropertyName("dry_degradation")] public double DryDegradation { get; init; }
    [JsonPropertyName("cold_degradation")] public double ColdDegradation { get; init; }
    [JsonPropertyName("light_rain_degradation")] public double LightRainDegradation { get; init; }
    [JsonPropertyName("heavy_rain_degradation")] public double HeavyRainDegradation { get; init; }

    public double FrictionMultiplier(WeatherKind weather) => weather switch
    {
        WeatherKind.Dry => DryFriction,
        WeatherKind.Cold => ColdFriction,
        WeatherKind.LightRain => LightRainFriction,
        WeatherKind.HeavyRain => HeavyRainFriction,
        _ => DryFriction,
    };

    public double DegradationRate(WeatherKind weather) => weather switch
    {
        WeatherKind.Dry => DryDegradation,
        WeatherKind.Cold => ColdDegradation,
        WeatherKind.LightRain => LightRainDegradation,
        WeatherKind.HeavyRain => HeavyRainDegradation,
        _ => DryDegradation,
    };
}

public sealed class AvailableSet
{
    [JsonPropertyName("ids")] public List<int> Ids { get; init; } = new();
    [JsonPropertyName("compound")] public string CompoundRaw { get; init; } = "";

    [JsonIgnore]
    public Compound Compound => CompoundRaw.ToLowerInvariant() switch
    {
        "soft" => Compound.Soft,
        "medium" => Compound.Medium,
        "hard" => Compound.Hard,
        "intermediate" => Compound.Intermediate,
        "wet" => Compound.Wet,
        _ => throw new FormatException($"Unknown compound '{CompoundRaw}'."),
    };
}

public sealed class Weather
{
    [JsonPropertyName("conditions")] public List<WeatherCondition> Conditions { get; init; } = new();
}

public sealed class WeatherCondition
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("condition")] public string ConditionRaw { get; init; } = "";
    [JsonPropertyName("duration_s")] public double DurationS { get; init; }
    [JsonPropertyName("acceleration_multiplier")] public double AccelerationMultiplier { get; init; }
    [JsonPropertyName("deceleration_multiplier")] public double DecelerationMultiplier { get; init; }

    [JsonIgnore]
    public WeatherKind Kind => ConditionRaw.ToLowerInvariant() switch
    {
        "dry" => WeatherKind.Dry,
        "cold" => WeatherKind.Cold,
        "light_rain" => WeatherKind.LightRain,
        "heavy_rain" => WeatherKind.HeavyRain,
        _ => throw new FormatException($"Unknown weather condition '{ConditionRaw}'."),
    };
}
