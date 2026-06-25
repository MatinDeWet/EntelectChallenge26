# Entelect Grand Prix — Problem Specification

> Derived from `Entelect F1 Hacakathon Problem Statement.pdf` (25 pages) and the four
> real level files in `Levels/` (`1.txt`–`4.txt`). Where the level files and the PDF
> disagree, **the level files win** and the difference is called out explicitly.

---

## 1. Nature of the problem

This is an **offline, deterministic, single-agent optimization** problem — *not* a
real-time game or a problem with hidden information.

- The entire race (car, track, tyres, weather schedule, fuel) is fully known up front
  from a static level JSON file.
- We must output a **complete race plan** (a JSON document) describing every action the
  car takes for every segment of every lap.
- There is **no opponent** and **no randomness**. The same plan against the same level
  always produces the same score.
- The grader re-runs our submitted source code and requires it to reproduce the exact
  output file byte-for-byte → **the program must be deterministic** (no wall-clock seeds,
  no unordered iteration affecting output, no parallel nondeterminism).

Therefore the task reduces to two cleanly separable pieces:

1. A **forward simulator** that, given a level + a plan, computes lap time, fuel used,
   tyre wear, blowouts, crashes and the final score — *exactly* matching the official
   physics. (This is the highest-risk component: if our physics drifts from the grader's,
   our optimizer optimizes the wrong thing.)
2. An **optimizer** that searches the decision variables to maximize the level's score
   function, using the simulator as its evaluation oracle.

---

## 2. Glossary

| Term | Meaning |
|------|---------|
| Segment | One ordered piece of track: a **straight** or a **corner**. |
| Straight | Straight section. We choose a *target speed* and a *braking point*. |
| Corner | Curved section with a `radius_m`. Speed is **constant** through it; exceeding the safe max causes a crash. |
| Braking point | Distance (m) before the *next* segment at which braking begins on a straight. |
| Pit lane | Accessible **only at the end of a lap**. Used to change tyres and/or refuel. Not a track segment. |
| Limp mode | Forced slow constant speed after running out of fuel **or** a tyre blowout. Persists until a pit stop. |
| Crawl mode | Forced slow constant speed after crashing in a corner (taken too fast). Persists across subsequent corners until a straight is reached. |
| Compound | Tyre type: Soft, Medium, Hard, Intermediate, Wet. |
| Tyre set | A physical set with a unique `id`. A compound may have several sets (Level 4). |

---

## 3. Input: level JSON schema

A level file is a single JSON object. Below is the **authoritative** schema as seen in
the real level files (which is a superset of the PDF appendix).

### 3.1 `car`

| Field | Unit | Meaning |
|-------|------|---------|
| `max_speed_m/s` | m/s | Hard cap on speed anywhere on track. |
| `accel_m/se2` | m/s² | Constant acceleration on straights (× weather accel multiplier). |
| `brake_m/se2` | m/s² | Constant deceleration when braking (× weather decel multiplier). |
| `limp_constant_m/s` | m/s | Constant speed while in limp mode. |
| `crawl_constant_m/s` | m/s | Constant speed while in crawl mode; also the global minimum speed. |
| `fuel_tank_capacity_l` | L | Max fuel the tank holds. |
| `initial_fuel_l` | L | Fuel at race start. |
| `fuel_consumption_l/m` | L/m | Base fuel rate `K_base` (matches the global `0.0005`). |

### 3.2 `race`

| Field | Unit | Meaning |
|-------|------|---------|
| `name` | — | Race name. |
| `laps` | — | Number of laps. |
| `base_pit_stop_time_s` | s | Fixed time cost of any pit stop. |
| `pit_tyre_swap_time_s` | s | Added time when changing tyres. |
| `pit_refuel_rate_l/s` | L/s | Refuel rate. |
| `corner_crash_penalty_s` | s | Time penalty added on a corner crash. |
| `pit_exit_speed_m/s` | m/s | Speed when exiting the pit lane (start of next lap). |
| `fuel_soft_cap_limit_l` | L | Soft cap used by the fuel-bonus score. May be exceeded with penalty. |
| `starting_weather_condition_id` | — | `id` of the weather condition active at t=0. |
| `time_reference_s` | s | **Not in PDF.** Per-level reference time. See §11 / Open Question Q1. |

### 3.3 `track`

`segments` is an **ordered** list. Each element:

| Field | Applies to | Meaning |
|-------|-----------|---------|
| `id` | all | Segment id, in race order (1-based). |
| `type` | all | `"straight"` or `"corner"`. |
| `length_m` | all | Segment length in metres (corners *do* have length). |
| `radius_m` | corner | Corner radius — drives the safe max corner speed. |

The pit lane is entered conceptually after the **last** segment of a lap.

### 3.4 `tyres.properties` (per compound)

| Field | Meaning |
|-------|---------|
| `life_span` | Wear budget. Blowout when cumulative degradation ≥ `life_span`. (All real levels: `1`.) |
| `base_friction` | **Base friction coefficient** (PDF table column). Soft 1.8 … Wet 1.1. |
| `dry/cold/light_rain/heavy_rain_friction_multiplier` | Weather friction multiplier. |
| `dry/cold/light_rain/heavy_rain_degradation` | Per-weather degradation *rate* used in wear formulas. |

> **Reconciliation with the PDF.** The PDF's friction example `(1.8 − 0.5) × 1` uses the
> base coefficient `1.8`, which the PDF appendix JSON omitted. The real level files **add
> `base_friction`** explicitly, confirming the model:
> `friction = (base_friction − total_degradation) × weather_multiplier`.
> `life_span` is a *separate* wear budget (the blowout threshold), not the friction base.
> ⚠️ Note Level 4 overrides Wet `base_friction` to `1.6` (vs `1.1` elsewhere) — never
> hard-code tyre constants; always read them from the level file.

### 3.5 `available_sets`

List of `{ "ids": [int...], "compound": string }`. Each id is one physical set of that
compound. Levels 1–3 give one set per compound; **Level 4 gives multiple** (e.g. Soft
`[1,2]`) and the supply is limited — this is the "limited set of tyres" rule.

### 3.6 `weather.conditions`

Ordered list. Each `{ id, condition, duration_s, acceleration_multiplier,
deceleration_multiplier }`.

- `condition` ∈ `dry | cold | light_rain | heavy_rain` and selects which tyre
  friction-multiplier / degradation column applies.
- The race starts in `starting_weather_condition_id`, runs that for `duration_s`, then
  advances to the **next** condition in list order, and so on, **cycling back to the
  first** when the list is exhausted (race time can exceed the total cycle).
- `acceleration_multiplier` / `deceleration_multiplier` scale the car's accel/brake.

---

## 4. Physics model

All quantities are SI (metres, seconds). Gravity `g = 9.8` (from the PDF example).

### 4.1 Effective accel / decel

```
accel_eff = accel_m/se2 × weather.acceleration_multiplier
brake_eff = brake_m/se2 × weather.deceleration_multiplier
```

### 4.2 Straight traversal

A straight of length `L` is entered at speed `v_in`. We choose a **target speed**
`v_t` (≤ `max_speed`) and a **braking point** `b` = metres before the next segment at
which braking begins. The corner that follows requires an **entry speed** `v_exit`
(the safe corner speed). Phases over the straight:

1. **Accelerate** from `v_in` toward `v_t` at `accel_eff` (only if `v_t > v_in`).
   - Time to change speed: `t = (v_final − v_initial) / accel_eff`.
   - Distance for a speed change: `d = (v_final² − v_initial²) / (2·a)`.
2. **Cruise** at `v_t` until the braking point.
3. **Brake** over the last `b` metres at `brake_eff` down to `v_exit`.

Rules / edge behaviour:
- **Speed follow-through** (PDF assumption 11): if `v_t ≤ v_in`, the car does **not**
  decelerate to the target; it simply holds `v_in` (no accel) until the braking phase.
- The car may be unable to reach `v_t` before braking must begin — it then brakes from
  whatever speed it actually reached.
- The braking distance must be sufficient to reach `v_exit`; if `b` is too small the car
  arrives at the corner **too fast** → crash (see §4.4). The optimizer must size `b`.
- The car never exceeds `max_speed` and never drops below `crawl_constant_m/s`.

### 4.3 Corner traversal

- The car holds its **entry speed** for the entire corner (no accel/decel).
- Corner time = `length_m / corner_speed`.
- **Safe maximum corner speed:**

  ```
  max_corner_speed = sqrt( tyre_friction × g × radius_m )
  ```

  where `tyre_friction` is evaluated for the *current* tyre, its accumulated
  degradation, and the *current* weather (§5.1).

  ⚠️ **Open Question Q2:** the PDF *Car* section writes
  `sqrt(friction·g·radius) + crawl_constant_m/s`, while the PDF *Track* section and
  worked example use plain `sqrt(friction·g·radius)`. We adopt the plain form as primary
  and treat `crawl_constant` as the global speed floor. Confirm against the grader.

### 4.4 Crashing (taking a corner too fast)

If the actual corner entry speed exceeds `max_corner_speed`:
- Add `corner_crash_penalty_s` to the running time.
- Apply a flat **+0.1** degradation to the current tyre set.
- Enter **crawl mode**: travel at `crawl_constant_m/s` through this and subsequent
  corners until a straight is reached (where acceleration can resume).

### 4.5 Limp mode

Triggered when fuel hits 0 **or** a tyre blows out (degradation ≥ `life_span`) mid-segment.
- Speed becomes `limp_constant_m/s`, no accel/decel.
- Applies to the rest of the current segment and **all following segments** until a pit
  stop fixes the cause (refuel and/or tyre change).

### 4.6 Useful kinematics (PDF appendix)

```
time   = (v_final − v_initial) / a
length = (v_final² − v_initial²) / (2·a)
length = v_initial·t + 0.5·a·t²
speed  = length / time
km/h   = m/s × 3.6
```

---

## 5. Tyre model

### 5.1 Friction at a point

```
tyre_friction = (base_friction − total_degradation) × weather_friction_multiplier
```

- `total_degradation` is the cumulative wear on the current set so far.
- `weather_friction_multiplier` is the compound's multiplier for the active weather.
- In **Level 1 degradation is disabled**, so friction is constant per compound/weather.

### 5.2 Degradation accumulation

`deg_rate` below is the compound's degradation value for the **current weather**
(`dry_degradation`, etc.). Global constants:

| Constant | Value |
|----------|-------|
| `K_STRAIGHT` | 0.0000166 |
| `K_BRAKING` | 0.0398 |
| `K_CORNER` | 0.000265 |

**Straight (cruise/accelerate portion):**
```
straight_deg = deg_rate × segment_length × K_STRAIGHT
```

**Braking portion of a straight** (`v_i` = speed at brake start, `v_f` = corner entry):
```
brake_deg = ( (v_i/100)² − (v_f/100)² ) × K_BRAKING × deg_rate
```

**Corner:**
```
corner_deg = K_CORNER × (speed² / radius) × deg_rate
```

**Crash:** flat `+0.1` to current set (§4.4).

A set **blows out** the instant `total_degradation ≥ life_span` → limp mode (§4.5).

---

## 6. Fuel model

Constants: `K_base = 0.0005 L/m`, `K_drag = 0.0000000015 L/m` (1.5e-9).

**Fuel used over a sub-segment** (`v_i` → `v_f` over `distance`):
```
F_used = ( K_base + K_drag × ((v_i + v_f) / 2)² ) × distance
```
(Worked example: 50→70 m/s over 800 m → 0.40432 L.)

- Fuel must be tracked across accel / cruise / brake / corner phases (use the relevant
  `v_i, v_f, distance` for each).
- Hitting 0 fuel mid-segment → limp mode (§4.5).
- **Refuel time** = `amount_to_refuel / pit_refuel_rate_l/s`.
- Tank cannot exceed `fuel_tank_capacity_l`.
- Level 1: fuel is effectively unlimited (`fuel_soft_cap_limit_l = 9999`).

---

## 7. Pit stops

Accessible only at the **end of a lap**. Options: change tyres, refuel, or both.

```
pit_stop_time = refuel_time + (tyre changed ? pit_tyre_swap_time_s : 0) + base_pit_stop_time_s
```

- Specify the target tyre `id` (must be an available set) and/or refuel amount. Zero /
  omitted means "no change of that kind".
- After a pit stop the car **exits at `pit_exit_speed_m/s`** into the first segment of the
  next lap.
- Changing tyres resets the *active* set to the referenced id with **its** current wear
  (you may switch to a partially-used set by id).

---

## 8. Penalties (summary)

| Cause | Effect |
|-------|--------|
| Corner too fast (crash) | `+corner_crash_penalty_s` time, `+0.1` tyre degradation, crawl mode. |
| Out of fuel | Limp mode until pit. |
| Tyre blowout (`deg ≥ life_span`) | Limp mode until pit. |

---

## 9. Per-level rules (cumulative — each level adds to the previous)

| Level | Adds | Key levers | Real-file facts |
|-------|------|-----------|-----------------|
| **1** | Base race, no degradation, ~unlimited fuel | Target speeds, braking points, starting tyre | 50 laps, 15 segs, single dry weather, 1 set/compound, soft-cap 9999 |
| **2** | Fuel management + pit stops | + refuel planning, speed↓ to save fuel | 60 laps, 25 segs, dry, soft-cap 219 |
| **3** | Weather (changes friction, accel, wear) | + weather-aware tyre choice & pit timing | 70 laps, 4-condition cycle, soft-cap 370 |
| **4** | Tyre degradation focus + limited tyre supply | + wear management, multi-set planning | 80 laps, 8-condition cycle, multi-set compounds, soft-cap 611 |

---

## 10. Scoring

```
base_score = 1 000 000 000 / time          (PDF — but see Q1)
```

**Levels 2 & 3** add a fuel bonus:
```
fuel_bonus  = −1 000 000 × (1 − fuel_used / fuel_soft_cap_limit_l)² + 1 000 000
final_score = base_score + fuel_bonus
```

**Level 4** also adds a tyre bonus:
```
tyre_bonus  = 100 000 × Σ(tyre_degradation) − 50 000 × (number_of_blowouts)
final_score = base_score + tyre_bonus + fuel_bonus
```

Scoring intuition:
- **Time dominates** via `base_score` — finish as fast as possible.
- **Fuel bonus** is maximized when `fuel_used` is as close to the soft cap as possible
  (the parabola peaks at `fuel_used = soft_cap`); going far under *or* over loses points.
- **Tyre bonus** rewards *using up* tyre life (more total degradation) but punishes
  blowouts heavily → run tyres close to the edge without popping them.

---

## 11. ⚠️ Open questions

> **There is no local grader.** The only way to verify physics or scoring is to **submit**
> (output JSON saved as `.txt` + a source ZIP) and read the returned platform score. So the
> questions below are resolved **empirically through calibration submissions**, not by
> consulting a reference implementation. Keep each assumption behind a single toggle so one
> hypothesis can be probed per submission (see `IMPLEMENTATION.md` §8a).

| # | Question | Current assumption | Why it matters |
|---|----------|--------------------|----------------|
| **Q1** | What is `time_reference_s` (7300 / 15400 / 27200 / 50800) and is `base_score` really `1e9/time`, or `1e9 × time_reference/time` (or similar)? It is per-level and roughly tracks a "par" race time. | Use PDF `1e9/time`; keep `time_reference_s` available in case scoring normalizes by it. | Changes the absolute score scale and the time-vs-fuel/tyre trade-off weighting. |
| **Q2** | Corner max speed: `sqrt(f·g·r)` or `sqrt(f·g·r) + crawl_constant`? | Plain `sqrt(f·g·r)`; `crawl_constant` is the floor. | Directly sets every corner's safe speed → huge effect on lap time and crash risk. |
| **Q3** | When braking but `v_t ≤ v_in` (follow-through), does braking still begin at the braking point down to `v_exit`? | Yes: follow-through governs the *cruise* target; braking to the corner still happens. | Affects every straight's exit speed. |
| **Q4** | Is the weather timer measured in cumulative *race time* from t=0, evaluated per segment (or per sub-phase)? | Cumulative race time; weather looked up at the start of each segment. | Determines which weather applies where; mid-segment changes need a rule. |
| **Q5** | Exact output number formatting the grader expects (integers vs decimals, key for `brake_start_m_before_next`, the typo'd key in the PDF). | Follow the PDF example keys exactly; emit numbers plainly. | Byte-exact reproduction / parse validity. |
| **Q6** | Does corner length consume fuel/wear at the (constant) corner speed? | Yes — fuel & wear use corner speed over `length_m`. | Fuel/wear totals. |
| **Q7** | On crash, is crawl speed the corner speed for *this* corner too, and does the penalty apply once per crash? | Crawl applies from the crashed corner onward; penalty once per crash. | Time/wear accounting. |

> The PDF submission example also contains two JSON typos (`brake_start_m_before_next"`
> missing an opening quote, and a stray comma). Treat the *intended* schema in §12 as
> canonical, not the literal typo'd snippet.

---

## 12. Output: submission format

A `.txt` file whose contents are a single valid JSON object:

```json
{
  "initial_tyre_id": 1,
  "laps": [
    {
      "lap": 1,
      "segments": [
        { "id": 1, "type": "straight", "target_m/s": 70, "brake_start_m_before_next": 800 },
        { "id": 2, "type": "corner" },
        { "id": 3, "type": "straight", "target_m/s": 50, "brake_start_m_before_next": 500 }
      ],
      "pit": { "enter": false }
    },
    {
      "lap": 2,
      "segments": [ "... same shape ..." ],
      "pit": { "enter": true, "tyre_change_set_id": 3, "fuel_refuel_amount_l": 20 }
    }
  ]
}
```

- `initial_tyre_id` — id of the starting set.
- One `laps[]` entry per lap, each listing **every** segment in order.
- **Straight** segments carry `target_m/s` and `brake_start_m_before_next`.
- **Corner** segments carry only `id` + `type`.
- `pit.enter` = false → no pit. When true, include `tyre_change_set_id` and/or
  `fuel_refuel_amount_l` (omit/zero = not doing that part).

**Each attempt** submits two artefacts to the Entelect Hackathon platform:
1. the plan above saved as a **`.txt`** file, and
2. a **ZIP of the source code** that recompiles to reproduce that exact `.txt`.

The grader recompiles the source and must reproduce the `.txt` exactly (hence the
determinism requirement in §1). There is no local validator — submitting is the only way
to see a real score.

---

## 13. Constants quick reference

| Constant | Value | Source |
|----------|-------|--------|
| `g` | 9.8 | PDF example |
| `K_STRAIGHT` | 0.0000166 | PDF |
| `K_BRAKING` | 0.0398 | PDF |
| `K_CORNER` | 0.000265 | PDF |
| `K_base` (fuel) | 0.0005 L/m | PDF / level `fuel_consumption_l/m` |
| `K_drag` (fuel) | 0.0000000015 L/m | PDF |
| Crash tyre penalty | +0.1 degradation | PDF |
