"""Fit game atlas pixel coordinates to observed room coordinates."""

import json
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / ".tools/extracted/map-assets.json").read_text(encoding="utf-8"))
SHAPES = {1: "Endroom", 2: "Straight", 3: "Curve", 4: "TShape", 5: "XShape"}
COLORS = {tuple(pair["color"][:3]): pair for pair in DATA["interpreter"]["glyphShapePairs"]}


def fit(zone, atlas_index, rooms):
    pixels = DATA[zone]["atlases"][atlas_index]["pixels"]
    by_shape = defaultdict(list)
    for y in range(32):
        for x in range(32):
            pair = COLORS.get(tuple(pixels[y * 32 + x]))
            if pair:
                by_shape[SHAPES[pair["shape"]]].append((x, y))

    fits = []
    for x_field, y_field in (("x", "z"), ("z", "x")):
        for sx in (-1, 1):
            for sy in (-1, 1):
                votes = Counter()
                for room in rooms:
                    for x, y in by_shape[room["shape"]]:
                        votes[x - sx * int(room[x_field] / 5),
                              y - sy * int(room[y_field] / 5)] += 1
                for (ox, oy), _ in votes.most_common(30):
                    matches = 0
                    for room in rooms:
                        x = ox + sx * int(room[x_field] / 5)
                        y = oy + sy * int(room[y_field] / 5)
                        if 0 <= x < 32 and 0 <= y < 32:
                            pair = COLORS.get(tuple(pixels[y * 32 + x]))
                            if pair and SHAPES[pair["shape"]] == room["shape"]:
                                matches += 1
                    fits.append((matches, x_field, y_field, sx, sy, ox, oy))
    return sorted(fits, reverse=True)[:3]


def main():
    sample_paths = sorted((ROOT / ".tools/samples").glob("*.json"), key=lambda p: int(p.stem))
    first = {}
    for path in sample_paths:
        sample = json.loads(path.read_text(encoding="utf-8-sig"))
        for zone, rooms in sample["zones"].items():
            index = sample["atlasIndex"][zone]
            if (zone, index) not in first:
                first[zone, index] = (sample["seed"], rooms)
    for (zone, index), (seed, rooms) in sorted(first.items()):
        print(zone, index, "seed", seed, "count", len(rooms), "fits", fit(zone, index, rooms))


if __name__ == "__main__":
    main()
