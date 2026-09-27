"""Compare extracted atlas glyphs with public API room layouts."""

import json
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / ".tools/extracted/map-assets.json").read_text(encoding="utf-8"))
SHAPES = {1: "Endroom", 2: "Straight", 3: "Curve", 4: "TShape", 5: "XShape"}
COLORS = {tuple(pair["color"][:3]): pair for pair in DATA["interpreter"]["glyphShapePairs"]}


def cells(zone: str, index: int) -> list[dict]:
    pixels = DATA[zone]["atlases"][index]["pixels"]
    result = []
    for y in range(2, 30, 3):
        for x in range(0, 31, 3):
            pair = COLORS.get(tuple(pixels[y * 32 + x]))
            if pair is not None:
                result.append({
                    "x": x * 5,
                    "z": (29 - y) * 5,
                    "shape": SHAPES[pair["shape"]],
                    "rotations": pair["rotations"],
                    "specificRooms": pair["specificRooms"],
                    "color": pair["color"][:3],
                })
    return result


def main() -> None:
    for zone in ("LightContainment", "HeavyContainment", "Entrance"):
        for atlas in DATA[zone]["atlases"]:
            room_cells = cells(zone, atlas["index"])
            shapes = Counter(c["shape"] for c in room_cells)
            print(zone, atlas["index"], len(room_cells), dict(shapes))
    groups = {}
    for path in (ROOT / ".tools/samples").glob("*.json"):
        truth = json.loads(path.read_text(encoding="utf-8-sig"))
        for zone, rooms in truth["zones"].items():
            key = zone, truth["atlasIndex"][zone]
            layout = {(r["x"], r["z"], r["shape"]) for r in rooms}
            if key in groups and groups[key][1] != layout:
                print("INCONSISTENT", key, "seeds", groups[key][0], truth["seed"])
            else:
                groups[key] = truth["seed"], layout
    for key, (seed, layout) in sorted(groups.items()):
        print("stable", key, "seed", seed, "rooms", len(layout))

    for path in ROOT.glob("compare-primary-*.json"):
        truth = json.loads(path.read_text(encoding="utf-8-sig"))
        print("truth", truth["seed"], truth["atlasIndex"])
        for zone, rooms in truth["zones"].items():
            atlas_cells = cells(zone, truth["atlasIndex"][zone])
            if zone == "Entrance":
                continue
            atlas_keys = {(c["x"], c["z"], c["shape"]) for c in atlas_cells}
            room_keys = {(r["x"], r["z"], r["shape"]) for r in rooms}
            print(zone, "atlas", len(atlas_keys), "truth", len(room_keys),
                  "missing", sorted(room_keys - atlas_keys)[:10],
                  "extra", sorted(atlas_keys - room_keys)[:10])


if __name__ == "__main__":
    main()
