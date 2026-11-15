"""Read installed Unity map assets without version-specific object IDs or game execution."""

import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tools/python"))
import UnityPy

GENERATORS = {
    "HeavyContainment": "AtlasZoneGenerator",
    "LightContainment": "LightContainmentZoneGenerator",
    "Entrance": "EntranceZoneGenerator",
}


def decode_interpreter(raw: bytes) -> list[dict]:
    count = struct.unpack_from("<i", raw, 32)[0]
    offset = 36
    pairs = []
    for _ in range(count):
        color = list(raw[offset:offset + 4])
        center_x, center_y, shape, specific_count = struct.unpack_from("<iiii", raw, offset + 4)
        offset += 20
        specifics = list(struct.unpack_from(f"<{specific_count}i", raw, offset))
        offset += 4 * specific_count
        rotation_count = struct.unpack_from("<i", raw, offset)[0]
        offset += 4
        rotations = list(struct.unpack_from(f"<{rotation_count}f", raw, offset))
        offset += 4 * rotation_count
        if shape not in range(1, 6) or not rotations or any(r % 90 for r in rotations):
            raise ValueError("Unsupported atlas glyph definition")
        pairs.append({"color": color, "center": [center_x, center_y], "shape": shape,
                      "specificRooms": specifics, "rotations": rotations})
    if offset != len(raw):
        raise ValueError(f"Interpreter schema changed: {offset} != {len(raw)}")
    return pairs


def pointer(raw, offset, owner):
    file_id, path_id = struct.unpack_from("<iq", raw, offset)
    return UnityPy.classes.PPtr(m_FileID=file_id, m_PathID=path_id, assetsfile=owner)


def pointer_array(raw, offset, owner):
    count = struct.unpack_from("<i", raw, offset)[0]
    if not 0 < count < 100 or offset + 4 + count * 12 > len(raw):
        raise ValueError(f"Unsupported pointer array at {offset}")
    return [pointer(raw, offset + 4 + i * 12, owner) for i in range(count)], offset + 4 + count * 12


def script_name(obj):
    script = obj.read(check_read=False).m_Script
    return "" if script.path_id == 0 else script.read().m_ClassName


def unique_component(scene, name):
    matches = [obj for obj in scene.objects.values()
               if obj.type.name == "MonoBehaviour" and script_name(obj) == name]
    if len(matches) != 1:
        raise ValueError(f"Expected one {name}, found {len(matches)}")
    return matches[0]


CONNECTOR_TYPES = ("SimpleRoomConnectorSpawnpoint", "AdvancedRoomConnectorSpawnpoint",
                   "SpawnableRoomConnector", "WallableSmallNodeRoomConnector")


def connector_ports(room_component):
    game_object = room_component.read(check_read=False).m_GameObject.read()
    root = next(ref.deref() for ref in game_object.m_Components if ref.deref().type.name == "Transform")
    counts = {name: 0 for name in CONNECTOR_TYPES}
    pending = [root]
    while pending:
        transform = pending.pop().read(check_read=False)
        for ref in transform.m_GameObject.read().m_Components:
            component = ref.deref()
            if component.type.name == "MonoBehaviour":
                name = script_name(component)
                if name in counts:
                    counts[name] += 1
        pending.extend(ref.deref() for ref in transform.m_Children)
    return {name: count for name, count in counts.items() if count}


def extract(game_data, output):
    scene_path = game_data / "level2"
    assets_path = game_data / "sharedassets2.assets"
    environment = UnityPy.load(str(scene_path), str(assets_path))
    scene = environment.files[str(scene_path)]
    (output / "atlases").mkdir(parents=True, exist_ok=True)
    fingerprints = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in (scene_path, assets_path)}
    manifest = game_data.parents[2] / "appmanifest_700330.acf"
    build_match = re.search(r'"buildid"\s+"(\d+)"', manifest.read_text(encoding="utf-8")) if manifest.exists() else None
    result = {"source": {"unityVersion": scene.unity_version,
                         "steamBuildId": build_match[1] if build_match else None,
                         "sha256": fingerprints}}

    for zone, class_name in GENERATORS.items():
        generator = unique_component(scene, class_name)
        raw = generator.get_raw_data()
        zone_id, height = struct.unpack_from("<if", raw, 32)
        room_refs, atlas_offset = pointer_array(raw, 40, scene)
        atlas_refs, tail_offset = pointer_array(raw, atlas_offset, scene)
        rooms = []
        for index, ref in enumerate(room_refs):
            obj = ref.deref()
            if script_name(obj) != "SpawnableRoom":
                raise ValueError(f"Unexpected room component {ref.path_id}")
            game_object = obj.read(check_read=False).m_GameObject.read()
            identifiers = [p.deref() for p in game_object.m_Components
                           if p.deref().type.name == "MonoBehaviour"
                           and script_name(p.deref()) in ("RoomIdentifier", "MultiLevelRoomIdentifier")]
            if len(identifiers) != 1:
                raise ValueError(f"Missing/ambiguous RoomIdentifier: {game_object.m_Name}")
            identifier_raw = identifiers[0].get_raw_data()
            shape, room_name, room_zone = struct.unpack_from("<iii", identifier_raw, 32)
            room_raw = obj.get_raw_data()
            minimum, maximum, chance, adjacent, first = struct.unpack_from("<iifff", room_raw, 32)
            if shape not in range(1, 6) or room_zone != zone_id or not 0 <= minimum <= maximum:
                raise ValueError(f"Unsupported room schema: {game_object.m_Name}")
            rooms.append({"index": index, "name": game_object.m_Name, "pathId": obj.path_id,
                          "shapeId": shape, "roomNameId": room_name, "zoneId": room_zone,
                          "minAmount": minimum, "maxAmount": maximum, "chanceMultiplier": chance,
                          "adjacentChanceMultiplier": adjacent, "firstChanceMultiplier": first,
                          "specialRoom": bool(room_raw[52]), "connectorPorts": connector_ports(obj)})

        atlases = []
        for index, ref in enumerate(atlas_refs):
            texture = ref.read()
            image = texture.image
            image.save(output / "atlases" / f"{zone}-{index}.png")
            atlases.append({"index": index, "name": texture.m_Name, "pathId": ref.path_id,
                            "width": texture.m_Width, "height": texture.m_Height,
                            "pixels": [list(pixel) for pixel in image.convert("RGB").get_flattened_data()]})
        section = {"generatorPathId": generator.path_id, "height": height,
                   "rooms": rooms, "atlases": atlases}
        if zone == "Entrance":
            # RoomSpawnParent and _hcz are PPtrs preceding the alignment fields.
            x, y, z, rotation = struct.unpack_from("<4f", raw, tail_offset + 24)
            if tail_offset + 40 != len(raw):
                raise ValueError("Entrance alignment schema changed")
            section["alignment"] = {"positionOffset": [x, y, z], "rotationOffset": rotation}
        result[zone] = section
        print(f"{zone}: {len(rooms)} prefabs, {len(atlases)} atlases, generator={generator.path_id}")

    interpreter = unique_component(scene, "MapAtlasInterpreter")
    result["interpreter"] = {"pathId": interpreter.path_id,
                             "glyphShapePairs": decode_interpreter(interpreter.get_raw_data())}
    synchronizer = unique_component(scene, "SeedSynchronizer")
    raw = synchronizer.get_raw_data()
    refs, _ = pointer_array(raw, 32, scene)
    result["generationOrder"] = [ref.deref().read(check_read=False).m_GameObject.read().m_Name for ref in refs]
    (output / "map-assets.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", type=Path, default=Path(r"D:\steam\steamapps\common\SCP Secret Laboratory"))
    parser.add_argument("--output", type=Path, default=ROOT / ".tools/extracted-beta")
    args = parser.parse_args()
    data = extract(args.game_dir / "SCPSL_Data", args.output)
    print("Generation order:", data["generationOrder"])


if __name__ == "__main__":
    main()
