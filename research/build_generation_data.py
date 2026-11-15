"""Build runtime data directly from Atlas glyphs; historical API samples are optional."""

import argparse
import json
from pathlib import Path

from build_display_data import attach_display_data

ROOT = Path(__file__).resolve().parents[1]
ZONE_ORDER = ("LightContainment", "HeavyContainment", "Entrance")
BASE_CONNECTIONS = {
    1: [(-1, 0)],
    2: [(-1, 0), (1, 0)],
    3: [(0, -1), (1, 0)],
    4: [(-1, 0), (0, -1), (0, 1)],
    5: [(-1, 0), (0, -1), (0, 1), (1, 0)],
}


def connections(shape, rotation, specifics):
    # The Class-D spawn faces the opposite side to generic end rooms.
    pattern = [(1, 0)] if specifics == [1] else BASE_CONNECTIONS[shape]
    for _ in range(rotation % 360 // 90):
        pattern = [(dz, -dx) for dx, dz in pattern]
    return [list(p) for p in sorted(pattern)]


def interpret_atlas(atlas, glyphs, rotation_offset=0):
    """MapAtlasInterpreter.Interpret scans Unity pixels from bottom left, then steps by 3."""
    width, height = atlas["width"], atlas["height"]
    if len(atlas["pixels"]) != width * height:
        raise ValueError("Incomplete atlas pixels")
    colors = {tuple(p["color"][:3]): p for p in glyphs}

    def scan(x, y):
        return colors.get(tuple(atlas["pixels"][(height - 1 - y) * width + x]))

    first = next(((x, y, scan(x, y)) for y in range(height) for x in range(width) if scan(x, y)), None)
    if first is None:
        raise ValueError(f"Atlas has no known glyphs: {atlas['name']}")
    x, y, pair = first
    x += pair["center"][0]
    y += pair["center"][1]
    cells = []
    for py in range(y, height, 3):
        for px in range(x if py == y else x % 3, width, 3):
            pair = scan(px, py)
            if pair is None:
                continue
            cx, cy = px // 3, py // 3
            rotations = [int(r) for r in pair["rotations"]]
            specifics = pair["specificRooms"]
            cells.append({"x": cx * 15, "z": cy * 15, "cx": cx, "cy": cy,
                          "shapeId": pair["shape"], "rotations": rotations, "specificRooms": specifics,
                          "connections": {str((r + rotation_offset) % 360): connections(pair["shape"], r, specifics)
                                          for r in rotations}})
    return cells


def inspect_atlas(zone, cells):
    positions = {(c["x"], c["z"]): c for c in cells}
    if len(positions) != len(cells) or not cells:
        raise ValueError(f"Invalid atlas coordinates: {zone}")
    graph = {p: set() for p in positions}
    unresolved = []
    for cell in cells:
        patterns = list(cell["connections"].values())
        if any(p != patterns[0] for p in patterns):
            raise ValueError("Allowed rotations change fixed atlas connections")
        for dx, dz in patterns[0]:
            pos = cell["x"], cell["z"]
            target = pos[0] + dx * 15, pos[1] + dz * 15
            if target not in positions:
                if cell["specificRooms"] == [13] and (dx, dz) == (
                        (1, 0) if zone == "HeavyContainment" else (-1, 0)):
                    continue  # HCZ/EZ checkpoint joins the other zone after alignment.
                unresolved.append({"x": pos[0], "z": pos[1], "dx": dx, "dz": dz, "reason": "missing-cell"})
                continue
            reverse = next(iter(positions[target]["connections"].values()))
            if [-dx, -dz] not in reverse:
                unresolved.append({"x": pos[0], "z": pos[1], "dx": dx, "dz": dz, "reason": "no-return-port"})
                continue
            graph[pos].add(target)
    remaining = set(positions)
    sizes = []
    while remaining:
        visited = set()
        queue = [next(iter(remaining))]
        while queue:
            pos = queue.pop()
            if pos in visited:
                continue
            visited.add(pos)
            queue.extend(graph[pos] - visited)
        remaining -= visited
        sizes.append(len(visited))
    return {"unresolvedPorts": unresolved, "componentSizes": sorted(sizes, reverse=True)}


def filter_unmatched_connections(cells):
    positions = {(cell["x"], cell["z"]): cell for cell in cells}
    for cell in cells:
        for pattern in cell["connections"].values():
            matched = []
            for dx, dz in pattern:
                neighbor = positions.get((cell["x"] + dx * 15, cell["z"] + dz * 15))
                if cell["specificRooms"] == [13] or (
                        neighbor and [-dx, -dz] in next(iter(neighbor["connections"].values()))):
                    matched.append([dx, dz])
            pattern[:] = matched


def build(source):
    output = {"schemaVersion": 2, "gameVersion": "15.0 Beta", "source": source["source"], "zones": {}}
    findings = json.loads((Path(__file__).with_name("atlas-topology-findings.json")).read_text(encoding="utf-8"))
    if source["generationOrder"][:3] != ["LCZ Zone Generator", "HCZ Zone Generator", "EZ Zone Generator"]:
        raise ValueError(f"Generation order changed: {source['generationOrder']}")
    for zone in ZONE_ORDER:
        section = source[zone]
        alignment = section.get("alignment", {"positionOffset": [0, 0, 0], "rotationOffset": 0})
        atlases = [interpret_atlas(atlas, source["interpreter"]["glyphShapePairs"], int(alignment["rotationOffset"]))
                   for atlas in section["atlases"]]
        topology = []
        for atlas, cells in zip(section["atlases"], atlases):
            actual = inspect_atlas(zone, cells)
            if actual["unresolvedPorts"] or len(actual["componentSizes"]) != 1:
                expected = findings["atlases"].get(zone + "/" + atlas["name"])
                if source["source"]["sha256"] != findings["sourceSha256"] or actual != expected:
                    raise ValueError(f"Unreviewed atlas topology: {zone}/{atlas['name']}: {actual}")
            filter_unmatched_connections(cells)
            topology.append(actual)
        output["zones"][zone] = {
            "height": section["height"], "alignment": alignment,
            "atlasNames": [a["name"] for a in section["atlases"]],
            "topologyFindings": topology,
            "templates": [{key: room[key] for key in (
                "name", "shapeId", "roomNameId", "minAmount", "maxAmount", "chanceMultiplier",
                "adjacentChanceMultiplier", "specialRoom", "connectorPorts")} for room in section["rooms"]],
            "atlases": atlases,
        }
    attach_display_data(output)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=ROOT / ".tools/extracted-beta/map-assets.json")
    parser.add_argument("--output", type=Path, default=ROOT / "map-generation-data.json")
    args = parser.parse_args()
    output = build(json.loads(args.assets.read_text(encoding="utf-8")))
    with args.output.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(output, ensure_ascii=False, separators=(",", ":")) + "\n")
    print(args.output, args.output.stat().st_size, "bytes")
    for zone, section in output["zones"].items():
        print(zone, "atlases", len(section["atlases"]), "cells", [len(a) for a in section["atlases"]])
        for name, finding in zip(section["atlasNames"], section["topologyFindings"]):
            if finding["unresolvedPorts"]:
                print("REQUIRES GAME COMPARISON:", name, len(finding["unresolvedPorts"]),
                      "unresolved ports; component sizes", finding["componentSizes"])


if __name__ == "__main__":
    main()
