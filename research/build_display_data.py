"""Attach room presentation and connection metadata to extracted game data."""

import json
from collections import Counter, defaultdict

from verify_generation import DATA, ROOT, generate, load_truth


OUTPUT = ROOT / "map-generation-data.json"


def main():
    raw = json.loads(OUTPUT.read_text(encoding="utf-8"))
    presentations = defaultdict(list)
    atlas_connections = defaultdict(set)
    codes = defaultdict(list)
    for seed in range(1, 51):
        generated = generate(seed)
        truth = load_truth(seed)
        expected = truth["zones"]
        for zone in DATA:
            atlas_index = truth["atlasIndex"][zone]
            source_cells = DATA[zone]["atlases"][atlas_index]
            if zone == "Entrance":
                checkpoint_cells = [cell for cell in source_cells if 13 in cell["specificRooms"]]
                generated_checkpoints = [cell for cell in generated[zone] if 13 in cell["specificRooms"]]
                offset_x = sum(cell["x"] for cell in generated_checkpoints) // 2 - sum(
                    cell["x"] for cell in checkpoint_cells) // 2
                offset_z = sum(cell["z"] for cell in generated_checkpoints) // 2 - sum(
                    cell["z"] for cell in checkpoint_cells) // 2
            else:
                offset_x = offset_z = 0
            for actual, observed in zip(generated[zone], expected[zone]):
                variant = actual["variant"]
                presentations[zone, variant].append(observed)
                pattern = tuple(sorted((conn["dx"], conn["dz"]) for conn in observed["conn"]))
                atlas_connections[zone, atlas_index, actual["x"] - offset_x,
                                  actual["z"] - offset_z, actual["rotY"]].add(pattern)
                if zone == "LightContainment" and observed["code"]:
                    codes[truth["atlasIndex"][zone], observed["x"], observed["z"]].append(observed["code"])

    for zone, section in raw["zones"].items():
        for template in section["templates"]:
            variant = template["name"]
            samples = presentations[zone, variant]
            assert samples, (zone, variant, "no observed room")
            metadata = {}
            for key in ("name", "group", "label", "category", "short", "glyph"):
                values = Counter(sample[key] for sample in samples)
                metadata[key] = values.most_common(1)[0][0]
            if variant == "HCZ_MicroHID_New":
                metadata["label"] = "HID"
            template["display"] = metadata
            template.pop("connections", None)

        if zone == "LightContainment":
            for index, atlas in enumerate(section["atlases"]):
                for cell in atlas:
                    values = codes.get((index, cell["x"], cell["z"]))
                    if values and len(set(values)) == 1:
                        cell["code"] = values[0]

        for atlas_index, atlas in enumerate(section["atlases"]):
            for cell in atlas:
                cell["connections"] = {}
                rotations = cell["rotations"]
                if zone == "Entrance":
                    rotations = [(rotation - 90) % 360 for rotation in rotations]
                observed_rotations = {}
                for rotation in rotations:
                    patterns = atlas_connections[zone, atlas_index, cell["x"], cell["z"], rotation]
                    assert len(patterns) <= 1, (zone, atlas_index, cell["x"], cell["z"], rotation, patterns)
                    if patterns:
                        observed_rotations[rotation] = next(iter(patterns))
                assert observed_rotations, (zone, atlas_index, cell["x"], cell["z"], "no observed rotation")
                base_rotation, base_pattern = next(iter(observed_rotations.items()))
                for rotation in rotations:
                    turns = (rotation - base_rotation) % 360 // 90
                    pattern = base_pattern
                    for _ in range(turns):
                        pattern = tuple(sorted((dz, -dx) for dx, dz in pattern))
                    if rotation in observed_rotations:
                        assert pattern == observed_rotations[rotation], (
                            zone, atlas_index, cell["x"], cell["z"], rotation)
                    cell["connections"][str(rotation)] = [list(item) for item in pattern]

    OUTPUT.write_text(json.dumps(raw, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    unstable_codes = [(key, set(values)) for key, values in codes.items() if len(set(values)) != 1]
    print("templates", sum(len(section["templates"]) for section in raw["zones"].values()),
          "unstable LCZ codes", len(unstable_codes), "examples", unstable_codes[:5])


if __name__ == "__main__":
    main()
