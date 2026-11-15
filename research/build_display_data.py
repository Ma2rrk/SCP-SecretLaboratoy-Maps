"""Attach maintained display fields without learning topology or labels from old API maps."""

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DISPLAY_SOURCE = Path(__file__).with_name("room-display-data.json")


def attach_display_data(data):
    displays = json.loads(DISPLAY_SOURCE.read_text(encoding="utf-8"))
    for section in data["zones"].values():
        for template in section["templates"]:
            if template["name"] not in displays:
                raise ValueError(f"Add display metadata for new prefab: {template['name']}")
            template["display"] = dict(displays[template["name"]])
            if template["name"] == "HCZ_MicroHID_New":
                template["display"]["label"] = "HID"
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, default=ROOT / "map-generation-data.json")
    args = parser.parse_args()
    data = attach_display_data(json.loads(args.data.read_text(encoding="utf-8")))
    with args.data.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n")
    print("Display templates:", sum(len(z["templates"]) for z in data["zones"].values()))


if __name__ == "__main__":
    main()
