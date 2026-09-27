"""Extract map generation inputs from the installed SCP:SL Unity assets.

Requires UnityPy on PYTHONPATH. Output is written under .tools/extracted.
The game installation is read only.
"""

import json
import struct
from pathlib import Path

import UnityPy


GAME_DATA = Path(r"D:\steam\steamapps\common\SCP Secret Laboratory\SCPSL_Data")
OUTPUT = Path(__file__).resolve().parents[1] / ".tools" / "extracted"
GENERATORS = {
    "HeavyContainment": 15795,
    "LightContainment": 16038,
    "Entrance": 18943,
}


def pointer(raw: bytes, offset: int) -> tuple[int, int]:
    return struct.unpack_from("<iq", raw, offset)


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
        pairs.append({
            "color": color,
            "center": [center_x, center_y],
            "shape": shape,
            "specificRooms": specifics,
            "rotations": rotations,
        })
    assert offset == len(raw), (offset, len(raw))
    return pairs


def main() -> None:
    output_atlases = OUTPUT / "atlases"
    output_atlases.mkdir(parents=True, exist_ok=True)
    scene_path = str(GAME_DATA / "level2")
    assets_path = str(GAME_DATA / "sharedassets2.assets")
    environment = UnityPy.load(scene_path, assets_path)
    scene = environment.files[scene_path]
    assets = environment.files[assets_path]
    result = {}

    for zone, generator_id in GENERATORS.items():
        raw = scene.objects[generator_id].get_raw_data()
        room_count = struct.unpack_from("<i", raw, 40)[0]
        rooms = []
        for index in range(room_count):
            file_id, path_id = pointer(raw, 44 + index * 12)
            assert file_id == 2
            obj = assets.objects[path_id]
            component = obj.read(check_read=False)
            game_object = component.m_GameObject.read()
            identifier = next((
                assets.objects[part.path_id]
                for part in game_object.m_Components
                if assets.objects[part.path_id].type.name == "MonoBehaviour"
                and assets.objects[part.path_id].read(check_read=False).m_Script.path_id in (4208, 373)
            ), None)
            identifier_raw = identifier.get_raw_data() if identifier else None
            rooms.append({
                "index": index,
                "name": game_object.m_Name,
                "pathId": path_id,
                "shapeId": struct.unpack_from("<i", identifier_raw, 32)[0] if identifier_raw else None,
                "roomNameId": struct.unpack_from("<i", identifier_raw, 36)[0] if identifier_raw else None,
                "zoneId": struct.unpack_from("<i", identifier_raw, 40)[0] if identifier_raw else None,
                "minAmount": struct.unpack_from("<i", obj.get_raw_data(), 32)[0],
                "maxAmount": struct.unpack_from("<i", obj.get_raw_data(), 36)[0],
                "chanceMultiplier": struct.unpack_from("<f", obj.get_raw_data(), 40)[0],
                "adjacentChanceMultiplier": struct.unpack_from("<f", obj.get_raw_data(), 44)[0],
                "raw": obj.get_raw_data().hex(),
            })

        atlas_offset = 44 + room_count * 12
        atlas_count = struct.unpack_from("<i", raw, atlas_offset)[0]
        atlases = []
        for index in range(atlas_count):
            file_id, path_id = pointer(raw, atlas_offset + 4 + index * 12)
            assert file_id == 2
            texture = assets.objects[path_id].read()
            texture.image.save(output_atlases / f"{zone}-{index}.png")
            atlases.append({
                "index": index,
                "name": texture.m_Name,
                "pathId": path_id,
                "width": texture.m_Width,
                "height": texture.m_Height,
                "pixels": [list(pixel) for pixel in texture.image.convert("RGB").getdata()],
            })

        result[zone] = {
            "generatorPathId": generator_id,
            "generatorRaw": raw.hex(),
            "rooms": rooms,
            "atlases": atlases,
        }

    interpreter = scene.objects[19162]
    result["interpreter"] = {
        "pathId": 19162,
        "glyphShapePairs": decode_interpreter(interpreter.get_raw_data()),
        "raw": interpreter.get_raw_data().hex(),
    }
    synchronizer = scene.objects[19773]
    result["synchronizer"] = {"pathId": 19773, "raw": synchronizer.get_raw_data().hex()}
    (OUTPUT / "map-assets.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    for zone, data in result.items():
        if zone in GENERATORS:
            print(f"{zone}: {len(data['rooms'])} room prefabs, {len(data['atlases'])} atlases")


if __name__ == "__main__":
    main()
