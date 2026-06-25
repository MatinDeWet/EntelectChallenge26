using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandPrix.Domain;

/// <summary>Output submission plan. Mirrors SPECIFICATION.md §12.</summary>
public sealed class RacePlan
{
    [JsonPropertyName("initial_tyre_id")] public int InitialTyreId { get; set; }
    [JsonPropertyName("laps")] public List<LapPlan> Laps { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, PlanJson.Options);
}

public sealed class LapPlan
{
    [JsonPropertyName("lap")] public int Lap { get; set; }
    [JsonPropertyName("segments")] public List<SegmentAction> Segments { get; set; } = new();
    [JsonPropertyName("pit")] public PitAction Pit { get; set; } = new();
}

public sealed class SegmentAction
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("target_m/s")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TargetSpeed { get; set; }

    [JsonPropertyName("brake_start_m_before_next")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BrakeStartBeforeNext { get; set; }

    public static SegmentAction Straight(int id, double target, double brakeStart) => new()
    {
        Id = id,
        Type = "straight",
        TargetSpeed = target,
        BrakeStartBeforeNext = brakeStart,
    };

    public static SegmentAction Corner(int id) => new() { Id = id, Type = "corner" };
}

public sealed class PitAction
{
    [JsonPropertyName("enter")] public bool Enter { get; set; }

    [JsonPropertyName("tyre_change_set_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TyreChangeSetId { get; set; }

    [JsonPropertyName("fuel_refuel_amount_l")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FuelRefuelAmount { get; set; }

    public static PitAction None() => new() { Enter = false };
}

internal static class PlanJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Invariant by default in STJ; explicit for determinism intent.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
