"""Verify the 15.0 Beta runtime data against read-only extracted game resources."""

import json
from pathlib import Path

from build_generation_data import build, inspect_atlas, interpret_atlas

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / ".tools/extracted-beta/map-assets.json"
RUNTIME = ROOT / "map-generation-data.json"


def main():
    source = json.loads(ASSETS.read_text(encoding="utf-8"))
    runtime = json.loads(RUNTIME.read_text(encoding="utf-8"))
    expected = build(source)
    assert runtime == expected, "Runtime data is stale relative to the extracted Beta resources"
    cells = 0
    issues = 0
    for zone, section in runtime["zones"].items():
        assert len(section["atlases"]) == len(source[zone]["atlases"])
        for index, (atlas, extracted) in enumerate(zip(section["atlases"], source[zone]["atlases"])):
            interpreted = interpret_atlas(extracted, source["interpreter"]["glyphShapePairs"],
                                           int(section["alignment"]["rotationOffset"]))
            assert len(atlas) == len(interpreted), (zone, index, "cell count")
            findings = inspect_atlas(zone, interpreted)
            assert findings == section["topologyFindings"][index], (zone, index, "topology findings")
            issues += len(findings["unresolvedPorts"])
            cells += len(atlas)
            positions = {(cell["x"], cell["z"]): cell for cell in atlas}
            for cell in atlas:
                for dx, dz in next(iter(cell["connections"].values())):
                    if cell["specificRooms"] == [13]:
                        continue
                    target = cell["x"] + dx * 15, cell["z"] + dz * 15
                    assert target in positions, (zone, index, cell["x"], cell["z"], dx, dz)
                    reverse = next(iter(positions[target]["connections"].values()))
                    assert [-dx, -dz] in reverse, (zone, index, cell["x"], cell["z"], dx, dz)

    templates = {template["name"]: template for section in runtime["zones"].values()
                 for template in section["templates"]}
    assert len(templates) == 72
    assert templates["EZ_GateA"]["connectorPorts"] == {
        "SimpleRoomConnectorSpawnpoint": 1, "WallableSmallNodeRoomConnector": 7}
    assert templates["EZ_GateB"]["connectorPorts"] == {
        "SimpleRoomConnectorSpawnpoint": 1, "SpawnableRoomConnector": 3}
    assert templates["LCZ_ChkpC"]["roomNameId"] == 42
    assert templates["HCZ_ChkpC"]["roomNameId"] == 43
    assert templates["LCZ_Plants_2"]["roomNameId"] == 44
    print(f"Verified Beta data: {cells} cells, {len(templates)} prefabs, {issues} reviewed EZ ports.")
    print("Historical API fixtures remain regression-only; they are not 15.0 Beta map evidence.")


if __name__ == "__main__":
    main()
