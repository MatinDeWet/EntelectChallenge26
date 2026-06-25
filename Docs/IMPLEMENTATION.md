# Entelect Grand Prix — .NET Implementation Plan

> Companion to [`SPECIFICATION.md`](./SPECIFICATION.md). This document describes **how** we
> build a .NET solution that produces a high-scoring, fully deterministic race plan for
> each level.

---

## 1. Design philosophy

The problem cleanly splits into a **simulator** and an **optimizer** (see Spec §1). Our
entire architecture follows from one principle:

> **The simulator is the single source of truth.** Every optimizer is just a search loop
> whose objective is "score, as computed by our simulator". So the simulator must be a
> faithful, well-tested re-implementation of the official physics — get this right first,
> and the rest is search.

Secondary principles:

- **Determinism is a hard requirement** (the grader recompiles our code). No `DateTime`,
  no unseeded RNG, no parallelism that affects output ordering, stable sorting only,
  invariant (culture-independent) number formatting.
- **Pluggable strategy per level.** Each level adds rules; we use one engine and a
  per-level optimizer so we can iterate level-by-level.
- **Test against ground truth.** Encode the PDF's worked examples and hand-computed
  segment cases as unit tests so the physics can't silently drift.

---

## 2. Tech stack

| Choice | Decision | Rationale |
|--------|----------|-----------|
| Runtime | **.NET 10** (matches the installed SDK) | Confirmed target. Keep to in-box libraries so the source ZIP recompiles cleanly on the grader. |
| Language | C# 12 | First-class on .NET. |
| JSON | `System.Text.Json` | In-box, fast, deterministic, no external dep. |
| Tests | **xUnit** + FluentAssertions (optional) | Standard, fast. |
| Math | `double` throughout, `System.Math` | Matches SI formulas. See §9 on numeric determinism. |
| External deps | **None** for core logic | Keeps the source ZIP self-contained and reproducible. |

> Note: the level files use non-CLR-identifier JSON keys (`max_speed_m/s`, `accel_m/se2`,
> `brake_m/se2`, `target_m/s`, `pit_refuel_rate_l/s`, …). Map these with
> `[JsonPropertyName("...")]` on DTO properties.

---

## 3. Solution layout

```
EntelectGrandPrix.sln
├── src/
│   ├── GrandPrix.Domain/          # Pure data models + level/plan DTOs + parsing
│   ├── GrandPrix.Simulation/      # Physics engine + scorer (the source of truth)
│   ├── GrandPrix.Optimization/    # Strategy representation + per-level optimizers
│   └── GrandPrix.Cli/             # Console entry point: level in → output .txt out
├── tests/
│   └── GrandPrix.Tests/           # xUnit: physics vectors, scoring, regression, determinism
├── Levels/                        # (existing) 1.txt … 4.txt
├── output/                        # Generated submission .txt files (git-ignored)
└── Docs/
```

Project dependencies (acyclic): `Cli → Optimization → Simulation → Domain`, `Tests → all`.

---

## 4. `GrandPrix.Domain`

### 4.1 Level DTOs (parse target)

Mirror Spec §3 exactly. Sketch:

```csharp
public sealed record Level(Car Car, Race Race, Track Track, Tyres Tyres,
                           IReadOnlyList<AvailableSet> AvailableSets, Weather Weather);

public sealed record Car(
    [property: JsonPropertyName("max_speed_m/s")]      double MaxSpeed,
    [property: JsonPropertyName("accel_m/se2")]        double Accel,
    [property: JsonPropertyName("brake_m/se2")]        double Brake,
    [property: JsonPropertyName("limp_constant_m/s")]  double LimpSpeed,
    [property: JsonPropertyName("crawl_constant_m/s")] double CrawlSpeed,
    [property: JsonPropertyName("fuel_tank_capacity_l")] double FuelCapacity,
    [property: JsonPropertyName("initial_fuel_l")]     double InitialFuel,
    [property: JsonPropertyName("fuel_consumption_l/m")] double FuelKBase);

public enum SegmentType { Straight, Corner }
public enum WeatherKind { Dry, Cold, LightRain, HeavyRain }
public enum Compound { Soft, Medium, Hard, Intermediate, Wet }
// Race, Track, Segment, TyreProperties, AvailableSet, WeatherCondition: per Spec §3.
```

- Parse `type`/`condition`/`compound` strings into enums (case-insensitive).
- Keep `time_reference_s` even though its scoring role is unconfirmed (Spec Q1).

### 4.2 Plan DTOs (serialize target)

Mirror Spec §12 (`RacePlan`, `LapPlan`, `SegmentAction`, `PitAction`) with the exact
JSON keys from the example, including the `target_m/s` / `brake_start_m_before_next`
spelling.

### 4.3 Derived/static constants

A `PhysicsConstants` static class: `G = 9.8`, `KStraight`, `KBraking`, `KCorner`,
`KDrag = 1.5e-9`, `CrashTyrePenalty = 0.1`. (`KBase` comes from the car DTO.)

---

## 5. `GrandPrix.Simulation` — the engine

### 5.1 Weather timeline

A `WeatherSchedule` built from the level: given a cumulative race time `t`, return the
active `WeatherCondition`. Because conditions cycle, precompute the cycle's total duration
and use modulo. Resolve weather **at the start of each segment** (Spec Q4) — keep the
lookup centralized so the rule is easy to change if the grader differs.

### 5.2 Car/race state

```csharp
public struct RaceState {
    public double Time;          // cumulative seconds
    public double Fuel;          // litres remaining
    public int    ActiveTyreId;
    public double TyreDegradation;   // cumulative on the active set
    public bool   InLimp;
    public bool   InCrawl;
    public double EntrySpeed;    // speed entering the next segment
    // per-set wear ledger so a swapped-out set keeps its wear (Level 4)
}
```

### 5.3 Segment simulation (the core state machine)

`ISegmentSimulator` with a method that, given `(state, segment, action, weather, level)`,
returns the updated state plus per-segment metrics (time, fuel used, degradation added,
crashed?, blewOut?). Implement as a phase decomposition:

- **Straight**: accelerate → cruise → brake, honoring follow-through (Spec §4.2), capping
  at `max_speed`, flooring at `crawl_constant`. Compute time, fuel (§6) and wear (§5.2)
  per phase. Detect over-speed arrival at the next corner.
- **Corner**: constant speed; compute safe max (§4.3); if exceeded → crash branch
  (penalty + 0.1 wear + crawl). Time = `length / speed`; add fuel + corner wear.
- **Limp / crawl overrides**: when `InLimp`, force `limp_constant` and skip accel logic
  until a pit; when `InCrawl`, force `crawl_constant` through corners until a straight.
- **Blowout / out-of-fuel** mid-segment → set the limp flag and recompute the remainder.

Keep each formula in a tiny pure helper (`Kinematics`, `FuelModel`, `TyreModel`) so each
is unit-testable in isolation against Spec worked examples.

### 5.4 Race simulation

`RaceSimulator.Simulate(level, plan) → RaceResult` iterates laps × segments, applies pit
stops at lap end (Spec §7, exit at `pit_exit_speed`), and accumulates totals: total time,
total fuel used, Σ degradation, blowout count, crash count, per-lap breakdown.

`RaceResult` must expose everything the scorer and the optimizer's diagnostics need.

### 5.5 Scorer

`Scorer.Score(level, result, levelNumber) → double` implementing Spec §10 exactly,
with the level number selecting which bonus terms apply. Keep `base_score` behind a single
method so swapping in the `time_reference_s` variant (Q1) is a one-line change.

---

## 6. `GrandPrix.Optimization`

### 6.1 Strategy representation (decision variables)

```
- initial_tyre_id                              (discrete)
- per straight (per lap): target_speed, brake_distance   (continuous)
- per lap: pit? + tyre_change_id + refuel_amount         (discrete + continuous)
```

A `Strategy` object converts to a `RacePlan` for the serializer and is what optimizers
mutate. Because most laps are identical, represent a **canonical lap template** plus
per-lap overrides (pit laps, weather-transition laps) to keep the search space small.

### 6.2 Per-segment building block (used by all levels)

For a single straight with known entry speed and required corner-exit speed, there is a
near-analytic "fastest legal traversal": accelerate as high as feasible, then brake exactly
to the corner's safe speed. The braking distance to go `v_peak → v_exit` is
`(v_peak² − v_exit²) / (2·brake_eff)`. This gives the time-optimal `target` and
`brake_start` directly. Fuel/wear-aware variants lower the target until a constraint binds.

### 6.3 Optimizer per level (incremental delivery)

| Level | Approach | Notes |
|-------|----------|-------|
| **1** | **Analytic / greedy fast-lap.** Pick the compound giving the highest corner speeds (friction); for each straight set `target = max_speed` (clamped to feasibility) and brake exactly to each corner's safe speed. No fuel/wear trade-offs. | Essentially closed-form; one plan, validate via simulator. Establishes the harness end-to-end. |
| **2** | Level-1 plan + **fuel tuning**: globally scale targets / insert refuel pit(s) so `fuel_used` lands near `fuel_soft_cap_limit_l` (peak of the bonus parabola) without going limp. 1-D search on a speed/fuel knob, evaluated by the simulator. | Pit count is tiny → enumerate pit-lap choices; line-search the speed scale. |
| **3** | + **weather-aware tyre & pit scheduling**: choose tyre per weather window; place pit stops near weather transitions. Search over the small set of (transition lap → compound) decisions; reuse Level-2 fuel tuning inside. | Weather schedule is known → transitions are few; DP or enumerate pit plans. |
| **4** | + **degradation management** with limited tyre supply: schedule which physical set runs which stint to maximize Σ degradation (run tyres to the edge) without blowouts, within supply. | Stint-planning DP / local search over (set → lap range); coordinate-descent on speeds; simulator-in-the-loop. |

Cross-cutting optimizer: a generic **coordinate-descent / local-search** refiner
(`LocalSearchOptimizer`) that perturbs strategy parameters and keeps improvements per the
simulator score. Use a **fixed seed** if any randomization is introduced, so output stays
deterministic.

### 6.4 Optimizer interface

```csharp
public interface ILevelOptimizer {
    int Level { get; }
    RacePlan Optimize(Level level);   // pure: same level → same plan
}
```

A registry maps level number → optimizer. Start every higher level by inheriting the
lower level's optimizer and layering the new concern.

---

## 7. `GrandPrix.Cli`

```
dotnet run --project src/GrandPrix.Cli -- --level Levels/1.txt --out output/level1.txt
            [--level-number 1]   # else infer from filename
            [--report]           # print time/fuel/wear/score breakdown to stdout
```

Responsibilities: read level JSON → pick optimizer → run → serialize plan → write `.txt`.
Print a diagnostic report (final score, total time, fuel used vs cap, Σ degradation,
blowouts, crashes) so we can compare runs. Writing must be deterministic and
culture-invariant (`CultureInfo.InvariantCulture`, fixed `JsonSerializerOptions`).

---

## 8. Testing strategy (`GrandPrix.Tests`)

The simulator is the risk; tests are how we de-risk it.

1. **Physics unit tests (golden vectors).** Encode every PDF worked example:
   - Fuel: 50→70 m/s over 800 m ⇒ `0.40432 L` (Spec §6).
   - Corner: friction 0.9, r 50 ⇒ `21 m/s` (Spec §4.3).
   - Tyre friction: `(1.8 − 0.5) × 1 = 1.3` (Spec §5.1).
   - Pit time: refuel 30 L @ 10 L/s + swap 5 + base 20 ⇒ `28 s` (Spec §7).
   - Kinematics identities (Spec §4.6).
2. **Parsing tests.** Round-trip each real `Levels/*.txt` into DTOs; assert key fields and
   the odd-key mappings; assert Level 4 multi-set + Wet `base_friction = 1.6` override.
3. **Scorer tests.** Verify each level's score formula on synthetic results, incl. the
   fuel-bonus parabola peaking at `fuel_used = soft_cap`.
4. **Determinism test.** Run each level optimizer twice; assert byte-identical output.
5. **Regression/characterization tests.** Snapshot each level's produced score; fail if a
   change regresses it (lets us refactor the optimizer safely).
6. **Sanity/invariant tests.** No segment speed < crawl or > max_speed; fuel never < 0
   without limp; degradation never ≥ life_span without a blowout flag.

> When Open Questions Q1–Q7 are answered, add a test that pins the resolved behaviour.

---

## 8a. Verification & calibration workflow (no local grader)

There is **no official simulator or validator we can run locally**. The *only* way to
verify our physics and scoring is to **submit** (the output JSON saved as a `.txt`
alongside a ZIP of the source) and read the platform's returned score. This shapes the
build in three concrete ways:

1. **Our simulator is a *relative* oracle, not an absolute one.** Even if our absolute
   score differs from the platform's, the optimizer only needs the simulator to *rank*
   plans consistently for search to work. Treat the in-app `--report` score as an estimate
   and the platform score as ground truth.
2. **Make every unverified assumption a single toggle.** Put the Q1 (`time_reference_s`
   scoring) and Q2 (corner-speed `+crawl_constant`) choices behind named options/flags
   (e.g. `--score-model`, `--corner-model`) with sensible defaults, so a submission can
   probe one hypothesis without code surgery. Keep a short log of "submission → settings →
   returned score" to triangulate the real formula.
3. **Use early submissions as calibration experiments**, not just attempts: e.g. submit a
   plan whose internal time we know exactly to back out how `base_score`/`time_reference_s`
   actually combine, before spending effort tuning fuel/tyre trade-offs.

Because each attempt costs a submission, prioritize getting a *correct, conservative*
Level-1 plan in early (crashes and blowouts are catastrophic for score), then refine.

## 9. Determinism & numeric reproducibility

- No `DateTime.Now`, no `Random` without a fixed seed, no `Parallel.*`/PLINQ affecting
  results, no reliance on hash-set iteration order in output paths.
- Format all numbers with `CultureInfo.InvariantCulture`; pin `JsonSerializerOptions`
  (encoder, number handling, no indentation drift) once and reuse.
- Keep all `double` math on a single code path; avoid reordering floating-point sums
  between runs. (If the grader's reproduction is exact-byte, prefer integer-ish or rounded
  output values per Q5 once confirmed.)

---

## 10. Build, run, package

```bash
# build & test
dotnet build EntelectGrandPrix.sln
dotnet test

# generate all four submissions
for n in 1 2 3 4; do
  dotnet run --project src/GrandPrix.Cli -- --level Levels/$n.txt --out output/level$n.txt --report
done
```

**Per attempt**, the platform requires **two artefacts** for the level:
- the output plan JSON saved as a **`.txt`** file (`output/levelN.txt`), and
- a **ZIP of the source code** (`src/` + `EntelectGrandPrix.sln`) that recompiles to
  reproduce that exact `.txt`.

There is no local grader — submitting is the only verification (see §8a). Keep `output/`
out of source control; produce the ZIP from a clean checkout so the grader's recompile
matches ours.

---

## 11. Delivery roadmap (milestones)

1. **M0 — Scaffold:** solution, projects, CI-less `dotnet test` green with a trivial test.
2. **M1 — Domain + parsing:** all DTOs, parse all four levels, parsing tests pass.
3. **M2 — Simulator + scorer:** physics engine, golden-vector tests green. *(Highest value
   — unlocks measuring any plan.)*
4. **M3 — Level 1 optimizer + CLI:** end-to-end `level1.txt`, reported score, determinism
   test green.
5. **M4 — Level 2:** fuel tuning + pit insertion.
6. **M5 — Level 3:** weather-aware tyre/pit scheduling.
7. **M6 — Level 4:** degradation/stint planning with limited supply.
8. **M7 — Hardening:** resolve Open Questions Q1–Q7 against the grader; tune; finalize
   submission packaging.

---

## 12. Open questions for the build (beyond Spec Q1–Q7)

- **Runtime**: ✅ resolved — target **.NET 10**. Keep to in-box libraries (avoid external
  NuGet) so the source ZIP recompiles cleanly on the grader.
- **Verification**: ✅ resolved — **no local validator**; the only feedback is the platform
  score after submitting `.txt` + source ZIP. Spec Q1–Q7 are therefore resolved
  *empirically via calibration submissions* (see §8a), not by reading a reference impl.
- **Output numeric format** (Spec Q5): exact precision/rounding the parser accepts — derive
  from early submissions; until then emit values matching the PDF example's style.
- **Scoring of `time_reference_s`** (Spec Q1): the single biggest unknown; design the first
  calibration submission specifically to back it out.
