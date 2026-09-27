"""Prototype the game's atlas, RNG and room-selection stages."""

import json
from collections import Counter
from pathlib import Path

from calibrate_atlases import COLORS, DATA, fit
from trace_rng import LegacyRandom


ROOT = Path(__file__).resolve().parents[1]
SHAPES = {1: "Endroom", 2: "Straight", 3: "Curve", 4: "TShape", 5: "XShape"}


def load_truth(seed):
    return json.loads((ROOT / f".tools/samples/{seed}.json").read_text(encoding="utf-8-sig"))


def cells_for(zone, index, truth_rooms):
    score, x_field, y_field, sx, sy, ox, oy = fit(zone, index, truth_rooms)[0]
    assert score == len(truth_rooms), (zone, index, score, len(truth_rooms))
    pixels = DATA[zone]["atlases"][index]["pixels"]
    # Read in the same order as MapAtlasInterpreter: high image Y first.
    cells = []
    for y in range(31, -1, -1):
        for x in range(32):
            pair = COLORS.get(tuple(pixels[y * 32 + x]))
            if pair is None:
                continue
            world_x = (x - ox) * sx * 5
            world_z = (y - oy) * sy * 5
            if x_field == "z":
                world_x, world_z = world_z, world_x
            if world_x % 15 or world_z % 15:
                continue
            if not any(r["x"] == world_x and r["z"] == world_z for r in truth_rooms):
                continue
            cells.append({"x": world_x, "z": world_z, "pair": pair,
                          "shape": SHAPES[pair["shape"]]})
    assert len(cells) == len(truth_rooms)
    return cells


def select_rooms(cells, templates, rng, mode, truth):
    generated = []
    amounts = Counter()
    reserved_names = {name for cell in cells for name in cell["pair"]["specificRooms"]}
    for cell in cells:
        shape_id = cell["pair"]["shape"]
        specifics = cell["pair"]["specificRooms"]
        if specifics:
            candidates = [t for t in templates if t["roomNameId"] in specifics]
        else:
            candidates = [t for t in templates if t["shapeId"] == shape_id
                          and t["roomNameId"] not in reserved_names
                          and amounts[t["index"]] < t["maxAmount"]]
        if not candidates:
            generated.append(None)
            continue
        forced = [t for t in candidates if amounts[t["index"]] < t["minAmount"]]
        if forced:
            chosen = forced[0]
        elif len(candidates) == 1 and mode == "single_skip":
            chosen = candidates[0]
        else:
            weights = []
            for candidate in candidates:
                adjacent = sum(1 for previous in generated if previous and
                               previous["index"] == candidate["index"] and
                               abs(previous["x"] - cell["x"]) + abs(previous["z"] - cell["z"]) == 15)
                weights.append(candidate["chanceMultiplier"] *
                               candidate["adjacentChanceMultiplier"] ** adjacent)
            value = rng.sample() * sum(weights)
            chosen = candidates[-1]
            for candidate, weight in zip(candidates, weights):
                if value < weight:
                    chosen = candidate
                    break
                value -= weight
        amounts[chosen["index"]] += 1
        generated.append({**chosen, "x": cell["x"], "z": cell["z"]})
    actual = {(r["x"], r["z"]): r for r in truth}
    aliases = {
        "LCZ_Cafe": "LczComputerRoom", "LCZ_ChkpA": "LczCheckpointA",
        "LCZ_ChkpB": "LczCheckpointB", "LCZ_372": "LczGlassroom",
        "LCZ_Plants": "LczGreenhouse", "LCZ_TCross": "Unnamed",
        "LCZ_Crossing": "Unnamed", "LCZ_Straight": "Unnamed",
        "LCZ_Curve": "Unnamed", "LCZ_ClassDSpawn": "LczClassDSpawn",
        "LCZ_330": "Lcz330", "LCZ_173": "Lcz173", "LCZ_914": "Lcz914",
        "LCZ_Toilets": "LczToilets", "LCZ_Armory": "LczArmory",
        "LCZ_Airlock": "LczAirlock",
        "HCZ_049": "Hcz049", "HCZ_079": "Hcz079", "HCZ_096": "Hcz096",
        "HCZ_106_Rework": "Hcz106", "HCZ_939": "Hcz939",
        "HCZ_ChkpA": "HczCheckpointA", "HCZ_ChkpB": "HczCheckpointB",
        "HCZ_Corner_Deep": "Unnamed", "HCZ_Crossing": "Unnamed",
        "HCZ_Crossroom_Water": "HczAcroamaticAbatement", "HCZ_Curve": "Unnamed",
        "HCZ_127": "Hcz127", "HCZ_ServerRoom": "HczServers",
        "HCZ_Intersection": "Unnamed", "HCZ_Intersection_Junk": "Unnamed",
        "HCZ_MicroHID_New": "HczMicroHID", "HCZ_Nuke": "HczWarhead",
        "HCZ_Straight": "Unnamed", "HCZ_Straight_C": "Unnamed",
        "HCZ_Straight_PipeRoom": "Unnamed", "HCZ_TArmory": "HczArmory",
        "HCZ_Tesla_Rework": "HczTesla", "HCZ_Testroom": "HczTestroom",
        "HCZ_EZ_Checkpoint Part": "HczCheckpointToEntranceZone",
        "HCZ_IncineratorWayside": "HczWaysideIncinerator",
        "HCZ_Intersection_Ramp": "HczRampTunnel",
    }
    matches = sum(actual.get((r["x"], r["z"]), {}).get("name") == aliases.get(r["name"], r["name"])
                  for r in generated if r)
    return matches, generated


def main():
    overall = Counter()
    for seed in range(1, 51):
        truth = load_truth(seed)
        rng = LegacyRandom(seed)
        for zone in ("LightContainment", "HeavyContainment"):
            index = rng.next(len(DATA[zone]["atlases"]))
            actual_index = truth["atlasIndex"][zone]
            if index != actual_index:
                print("INDEX MISMATCH", seed, zone, index, actual_index)
                break
            current_cells = cells_for(zone, index, truth["zones"][zone])
            for cell in current_cells:
                cell["rotY"] = cell["pair"]["rotations"][rng.next(len(cell["pair"]["rotations"]))]
            rotation_matches = sum(cell["rotY"] == next(r["rotY"] for r in truth["zones"][zone]
                                              if r["x"] == cell["x"] and r["z"] == cell["z"])
                                   for cell in current_cells)
            for i in range(len(current_cells) - 1, 0, -1):
                k = rng.next(i + 1)
                current_cells[i], current_cells[k] = current_cells[k], current_cells[i]
            correct_order = sum((cell["x"], cell["z"]) ==
                                (truth["zones"][zone][i]["x"], truth["zones"][zone][i]["z"])
                                for i, cell in enumerate(current_cells))
            matches, generated = select_rooms(current_cells, DATA[zone]["rooms"], rng,
                                              "single_draw", truth["zones"][zone])
            print(seed, zone, "rot", rotation_matches, "order", correct_order,
                  "names", matches, "/", len(current_cells), "draws", rng.draws)
            overall[zone] += matches == len(current_cells) and correct_order == len(current_cells)
        else:
            next_ez = rng.next(5)
            print(seed, "EZ", next_ez, "truth", truth["atlasIndex"]["Entrance"])
            overall["EZ index"] += next_ez == truth["atlasIndex"]["Entrance"]
    print("overall", overall)


if __name__ == "__main__":
    main()
