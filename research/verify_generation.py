"""Compare the extracted map generator against observed map API samples."""

import json
from collections import Counter, defaultdict
from pathlib import Path

from simulate_map import SHAPES, load_truth, select_rooms
from trace_rng import LegacyRandom


ROOT = Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / "map-generation-data.json").read_text(encoding="utf-8"))["zones"]
ALIASES = {
    "EZ_CollapsedTunnel": "EzCollapsedTunnel",
    "EZ_Endoof": "EzRedroom",
    "EZ_GateA": "EzGateA",
    "EZ_GateB": "EzGateB",
    "EZ_HCZ_Checkpoint Part": "HczCheckpointToEntranceZone",
    "EZ_Intercom": "EzIntercom",
    "EZ_PCs": "EzOfficeLarge",
    "EZ_PCs_small": "EzOfficeSmall",
    "EZ_Shelter": "EzEvacShelter",
    "EZ_upstairs": "EzOfficeStoried",
}


def checkpoints(rooms):
    return [room for room in rooms if room["name"] == "HczCheckpointToEntranceZone"]


def shift_entrance(cells, heavy_rooms):
    entrances = [cell for cell in cells if 13 in cell["specificRooms"]]
    heavy = [room for room in heavy_rooms if room["variant"] == "HCZ_EZ_Checkpoint Part"]
    assert len(entrances) == len(heavy) == 2
    offset_x = sum(room["x"] for room in heavy) // 2 + 15 - sum(cell["x"] for cell in entrances) // 2
    offset_z = sum(room["z"] for room in heavy) // 2 - sum(cell["z"] for cell in entrances) // 2
    return [{**cell, "x": cell["x"] + offset_x, "z": cell["z"] + offset_z} for cell in cells]


def generate(seed):
    rng = LegacyRandom(seed)
    result = {}
    for zone in ("LightContainment", "HeavyContainment", "Entrance"):
        atlas_index = rng.next(len(DATA[zone]["atlases"]))
        cells = [dict(cell, pair={"shape": cell["shapeId"], "specificRooms": cell["specificRooms"]})
                 for cell in DATA[zone]["atlases"][atlas_index]]
        if zone == "Entrance":
            cells = shift_entrance(cells, result["HeavyContainment"])
        for cell in cells:
            cell["rotY"] = cell["rotations"][rng.next(len(cell["rotations"]))]
            if zone == "Entrance":
                cell["rotY"] = (cell["rotY"] - 90) % 360
        for index in range(len(cells) - 1, 0, -1):
            swap = rng.next(index + 1)
            cells[index], cells[swap] = cells[swap], cells[index]
        _, selected = select_rooms(cells, [dict(index=i, **room) for i, room in enumerate(DATA[zone]["templates"])], rng,
                                   "single_draw", [])
        result[zone] = [{**cell, "name": ALIASES.get(room["name"], room["name"]) if room else "" ,
                         "variant": room["name"] if room else ""} for cell, room in zip(cells, selected)]
    return result


def main():
    totals = Counter()
    connections = defaultdict(set)
    variants = defaultdict(set)
    for seed in range(1, 51):
        truth = load_truth(seed)
        actual = generate(seed)
        for zone, rooms in actual.items():
            atlas_index = truth["atlasIndex"][zone]
            atlas = DATA[zone]["atlases"][atlas_index]
            source_checkpoints = [cell for cell in atlas if 13 in cell["specificRooms"]]
            generated_checkpoints = [cell for cell in rooms if 13 in cell["specificRooms"]]
            if zone == "Entrance":
                offset_x = sum(cell["x"] for cell in generated_checkpoints) // 2 - sum(
                    cell["x"] for cell in source_checkpoints) // 2
                offset_z = sum(cell["z"] for cell in generated_checkpoints) // 2 - sum(
                    cell["z"] for cell in source_checkpoints) // 2
            else:
                offset_x = offset_z = 0
            atlas_by_position = {(cell["x"], cell["z"]): cell for cell in atlas}
            expected = truth["zones"][zone]
            assert len(rooms) == len(expected), (seed, zone, "count")
            for index, (room, reference) in enumerate(zip(rooms, expected)):
                for key in ("x", "z", "rotY"):
                    assert room[key] == reference[key], (seed, zone, index, key, room[key], reference[key])
                assert SHAPES[room["shapeId"]] == reference["shape"], (seed, zone, index, "shape")
                if zone == "Entrance" and room["name"] != reference["name"] and reference["name"] != "Unnamed":
                    print("NAME", seed, zone, index, room["variant"], reference["name"])
                    totals["name mismatch"] += 1
                connections[(zone, reference["shape"], reference["rotY"])].add(
                    tuple(sorted((item["dx"], item["dz"]) for item in reference["conn"])))
                source_cell = atlas_by_position[room["x"] - offset_x, room["z"] - offset_z]
                assert source_cell["connections"][str(room["rotY"])] == [
                    list(item) for item in sorted((conn["dx"], conn["dz"]) for conn in reference["conn"])
                ], (seed, zone, index, "atlas connection")
                variants[(zone, room["variant"], reference["rotY"])].add(
                    tuple(sorted((item["dx"], item["dz"]) for item in reference["conn"])))
                totals[zone] += 1
    inconsistent = [(key, values) for key, values in connections.items() if len(values) != 1]
    print("rooms", dict(totals))
    print("inconsistent connections", inconsistent)
    print("variant connection conflicts", [(key, values) for key, values in variants.items() if len(values) != 1])
    for seed in (224593721, 7654321):
        file = ROOT / f"backup-api-{seed}.json"
        if not file.exists():
            continue
        expected = {(room["zone"], room["x"], room["z"]): room
                    for room in json.loads(file.read_text(encoding="utf-8-sig"))["rooms"]}
        differences = Counter()
        examples = []
        for zone, rooms in generate(seed).items():
            for room in rooms:
                reference = expected.get((zone, room["x"], room["z"]))
                if reference is None:
                    differences["missing"] += 1
                elif room["variant"] + "(Clone)" != reference["variant"]:
                    differences["variant"] += 1
                    examples.append((zone, room["x"], room["z"], room["variant"], reference["variant"]))
                if reference and room["rotY"] != reference["rotY"]:
                    differences["rotation"] += 1
        print("backup", seed, "rooms", len(expected), "differences", dict(differences), "examples", examples[:8])


if __name__ == "__main__":
    main()
