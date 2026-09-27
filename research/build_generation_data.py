"""Build compact runtime data from extracted Unity assets and atlas calibration."""

import json
from pathlib import Path

from calibrate_atlases import COLORS, DATA, fit


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "map-generation-data.json"
SAMPLES = ROOT / ".tools/samples"


def first_samples():
    selected = {}
    for path in sorted(SAMPLES.glob("*.json"), key=lambda item: int(item.stem)):
        sample = json.loads(path.read_text(encoding="utf-8-sig"))
        for zone, rooms in sample["zones"].items():
            selected.setdefault((zone, sample["atlasIndex"][zone]), rooms)
    return selected


def atlas_cells(zone, atlas, reference):
    score, x_field, y_field, sx, sy, ox, oy = fit(zone, atlas["index"], reference)[0]
    assert score == len(reference), (zone, atlas["index"], score, len(reference))
    pixels = atlas["pixels"]
    by_position = {(room["x"], room["z"]): room for room in reference}
    xs = {(ox + sx * int(room[x_field] / 5)) % 3 for room in reference}
    ys = {(oy + sy * int(room[y_field] / 5)) % 3 for room in reference}
    assert len(xs) == len(ys) == 1
    cells = []
    for y in range(31, -1, -1):
        if y % 3 not in ys:
            continue
        for x in range(32):
            if x % 3 not in xs:
                continue
            pair = COLORS.get(tuple(pixels[y * 32 + x]))
            if pair is None:
                continue
            world_x = (x - ox) * sx * 5
            world_z = (y - oy) * sy * 5
            if x_field == "z":
                world_x, world_z = world_z, world_x
            if world_x % 15 or world_z % 15:
                continue
            cells.append({
                "x": world_x,
                "z": world_z,
                "cx": by_position[(world_x, world_z)]["cx"],
                "cy": by_position[(world_x, world_z)]["cy"],
                "shapeId": pair["shape"],
                "rotations": [int(rotation) for rotation in pair["rotations"]],
                "specificRooms": pair["specificRooms"],
            })
    assert len(cells) == len(reference), (zone, atlas["index"], len(cells), len(reference))
    return cells


def main():
    references = first_samples()
    output = {"source": "SCP:SL 2026-08-01 Unity assets", "zones": {}}
    for zone in ("LightContainment", "HeavyContainment", "Entrance"):
        source = DATA[zone]
        output["zones"][zone] = {
            "templates": [{key: room[key] for key in (
                "name", "shapeId", "roomNameId", "minAmount", "maxAmount",
                "chanceMultiplier", "adjacentChanceMultiplier")}
                for room in source["rooms"]],
            "atlases": [atlas_cells(zone, atlas, references[zone, atlas["index"]])
                        for atlas in source["atlases"]],
        }
    OUTPUT.write_text(json.dumps(output, separators=(",", ":")), encoding="utf-8")
    print(OUTPUT, OUTPUT.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
