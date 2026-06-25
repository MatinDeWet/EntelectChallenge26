import json
import math
import sys
from typing import Dict, List, Optional, Tuple

GRAVITY = 9.8
KBASE = 0.0005
KDRAG = 0.0000000015
SAFETY_FUEL_L = 2.0


def fuel_used(vi: float, vf: float, distance_m: float) -> float:
    avg_speed = (vi + vf) / 2.0
    return (KBASE + KDRAG * avg_speed ** 2) * distance_m


def clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def solve(level_file: str, output_file: str) -> None:
    with open(level_file, "r", encoding="utf-8") as f:
        data = json.load(f)

    car = data["car"]
    race = data["race"]
    track = data["track"]["segments"]

    max_speed = float(car["max_speed_m/s"])
    accel = float(car["accel_m/se2"])
    brake = float(car["brake_m/se2"])
    crawl = float(car["crawl_constant_m/s"])
    tank_capacity = float(car["fuel_tank_capacity_l"])
    initial_fuel = float(car["initial_fuel_l"])
    laps = int(race["laps"])
    pit_exit_speed = float(race["pit_exit_speed_m/s"])

    # Level 2 has dry weather throughout, but this keeps the code data-driven.
    start_weather_id = race.get("starting_weather_condition_id")
    weather = next(
        (w for w in data["weather"]["conditions"] if w["id"] == start_weather_id),
        data["weather"]["conditions"][0],
    )
    condition = weather["condition"]
    friction_key = f"{condition}_friction_multiplier"

    # In dry weather Soft has the best friction, which allows the highest safe corner speed.
    best_tyre_id = None
    best_compound = None
    best_friction = -1.0
    for tyre_set in data["available_sets"]:
        compound = tyre_set["compound"]
        props = data["tyres"]["properties"][compound]
        friction = float(props["base_friction"]) * float(props[friction_key])
        if friction > best_friction:
            best_friction = friction
            best_compound = compound
            best_tyre_id = tyre_set["ids"][0]

    # The statement includes crawl_constant in the car corner-speed formula.
    # The logs confirm this: some corners are safe at sqrt(friction*g*r)+crawl, not only sqrt(...).
    safe_corner_speed: Dict[int, float] = {}
    for seg in track:
        if seg["type"] == "corner":
            safe_corner_speed[seg["id"]] = math.sqrt(best_friction * GRAVITY * float(seg["radius_m"])) + crawl

    def immediate_corner_block_min_speed(index: int) -> Optional[float]:
        """Return the slowest safe speed in the corner block directly after a straight.

        If the next segment is another straight, do not brake yet. Let the following straight handle
        the next corner block, which avoids unnecessary early braking.
        """
        next_index = (index + 1) % len(track)
        if track[next_index]["type"] != "corner":
            return None

        speeds: List[float] = []
        j = next_index
        while track[j]["type"] == "corner":
            speeds.append(safe_corner_speed[track[j]["id"]])
            j = (j + 1) % len(track)
        return min(speeds)

    # For Level 2, the fuel bonus is highest near the soft cap, but the unavoidable base fuel
    # consumption for this track is already above the cap. The best trade-off is therefore still
    # to run at max speed and minimise race time while avoiding crashes.
    target_speed = max_speed

    straight_plan: Dict[int, Dict[str, float]] = {}
    for idx, seg in enumerate(track):
        if seg["type"] != "straight":
            continue

        desired_exit = immediate_corner_block_min_speed(idx)
        if desired_exit is None:
            brake_before_next = 0.0
        else:
            brake_before_next = (target_speed ** 2 - desired_exit ** 2) / (2.0 * brake)
            brake_before_next = clamp(brake_before_next, 0.0, float(seg["length_m"]) - 1.0)

        straight_plan[seg["id"]] = {
            "target": round(target_speed, 3),
            "brake": round(brake_before_next, 3),
        }

    def simulate_lap(entry_speed: float) -> Tuple[float, float]:
        """Estimate one lap fuel and exit speed for pit planning."""
        fuel = 0.0
        speed = entry_speed

        for idx, seg in enumerate(track):
            length = float(seg["length_m"])
            if seg["type"] == "corner":
                # We have planned every corner entry to be within the safe speed.
                speed = min(speed, safe_corner_speed[seg["id"]])
                fuel += fuel_used(speed, speed, length)
                continue

            plan = straight_plan[seg["id"]]
            target = max(speed, plan["target"])
            brake_distance = plan["brake"]
            drive_distance = max(0.0, length - brake_distance)

            if target > speed:
                accel_distance_needed = (target ** 2 - speed ** 2) / (2.0 * accel)
            else:
                accel_distance_needed = 0.0

            if accel_distance_needed >= drive_distance:
                exit_before_brake = math.sqrt(speed ** 2 + 2.0 * accel * drive_distance)
                fuel += fuel_used(speed, exit_before_brake, drive_distance)
                speed = exit_before_brake
            else:
                fuel += fuel_used(speed, target, accel_distance_needed)
                cruise_distance = drive_distance - accel_distance_needed
                fuel += fuel_used(target, target, cruise_distance)
                speed = target

            if brake_distance > 0:
                exit_speed = math.sqrt(max(0.0, speed ** 2 - 2.0 * brake * brake_distance))
                fuel += fuel_used(speed, exit_speed, brake_distance)
                speed = exit_speed

        return fuel, speed

    # Estimate per-lap fuel. Pit exit speed only matters for the lap after a pit stop, so we use a
    # conservative rolling estimate when scheduling stops.
    normal_lap_fuel, normal_exit_speed = simulate_lap(0.0)
    repeat_lap_fuel, _ = simulate_lap(normal_exit_speed)
    pit_exit_lap_fuel, pit_exit_lap_exit_speed = simulate_lap(pit_exit_speed)

    # Schedule the minimum number of refuel stops, with just enough fuel to finish safely.
    pit_refuels: Dict[int, float] = {}
    fuel_remaining = initial_fuel
    entry_speed_next_lap = normal_exit_speed

    estimated_lap_fuels: List[float] = []
    speed_for_lap = 0.0
    for lap in range(1, laps + 1):
        lap_fuel, exit_speed = simulate_lap(speed_for_lap)
        estimated_lap_fuels.append(lap_fuel)
        speed_for_lap = exit_speed

    lap = 1
    while lap <= laps:
        lap_fuel = estimated_lap_fuels[lap - 1]
        fuel_remaining -= lap_fuel

        if lap == laps:
            break

        next_lap_fuel = estimated_lap_fuels[lap]
        if fuel_remaining < next_lap_fuel + SAFETY_FUEL_L:
            future_need = sum(estimated_lap_fuels[lap:])
            shortfall = future_need + SAFETY_FUEL_L - fuel_remaining
            refuel_amount = clamp(shortfall, 0.0, tank_capacity - fuel_remaining)

            # Round up slightly so JSON decimals do not leave us stranded by a tiny margin.
            refuel_amount = math.ceil(refuel_amount * 100.0) / 100.0
            if refuel_amount > 0:
                pit_refuels[lap] = refuel_amount
                fuel_remaining += refuel_amount

            # After a pit stop, the next lap starts at pit lane exit speed. Refresh the estimate
            # from the next lap onwards to avoid under-fuelling.
            if lap < laps:
                estimated_lap_fuels[lap], new_exit = simulate_lap(pit_exit_speed)
                for k in range(lap + 1, laps):
                    estimated_lap_fuels[k], new_exit = simulate_lap(new_exit)

        lap += 1

    laps_out = []
    for lap_no in range(1, laps + 1):
        segments_out = []
        for seg in track:
            if seg["type"] == "straight":
                plan = straight_plan[seg["id"]]
                segments_out.append({
                    "id": seg["id"],
                    "type": "straight",
                    "target_m/s": plan["target"],
                    "brake_start_m_before_next": plan["brake"],
                })
            else:
                segments_out.append({
                    "id": seg["id"],
                    "type": "corner",
                })

        pit = {"enter": False}
        if lap_no in pit_refuels:
            pit = {
                "enter": True,
                "fuel_refuel_amount_l": pit_refuels[lap_no],
            }

        laps_out.append({
            "lap": lap_no,
            "segments": segments_out,
            "pit": pit,
        })

    submission = {
        "initial_tyre_id": best_tyre_id,
        "laps": laps_out,
    }

    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(submission, f, indent=2)

    total_estimated_fuel = sum(estimated_lap_fuels)
    print(f"Tyre: {best_compound} id={best_tyre_id}")
    print(f"Target speed: {target_speed:.1f} m/s")
    print(f"Estimated fuel: {total_estimated_fuel:.2f} L")
    print(f"Pit stops: {pit_refuels}")
    print(f"Wrote: {output_file}")


if __name__ == "__main__":
    solve(
        sys.argv[1] if len(sys.argv) > 1 else "2.txt",
        sys.argv[2] if len(sys.argv) > 2 else "submission_level2_optimised.txt",
    )
