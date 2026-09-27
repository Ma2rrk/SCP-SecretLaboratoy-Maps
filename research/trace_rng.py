"""Check candidate RNG consumption orders against observed LCZ rotations."""

import json
from collections import Counter
from pathlib import Path

from calibrate_atlases import COLORS, DATA, fit


ROOT = Path(__file__).resolve().parents[1]


class LegacyRandom:
    MBIG = 2147483647
    MSEED = 161803398

    def __init__(self, seed):
        seed = (seed + 2**31) % 2**32 - 2**31
        subtraction = self.MBIG if seed == -2**31 else abs(seed)
        mj = (self.MSEED - subtraction + 2**31) % 2**32 - 2**31
        self.array = [0] * 56
        self.array[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = 21 * i % 55
            self.array[ii] = mk
            mk = mj - mk
            if mk < 0:
                mk += self.MBIG
            mj = self.array[ii]
        for _ in range(4):
            for i in range(1, 56):
                n = self.array[i] - self.array[1 + (i + 30) % 55]
                self.array[i] = n + self.MBIG if n < 0 else n
        self.inext = 0
        self.inextp = 21
        self.draws = 0

    def sample(self):
        self.inext = 1 if self.inext + 1 >= 56 else self.inext + 1
        self.inextp = 1 if self.inextp + 1 >= 56 else self.inextp + 1
        value = self.array[self.inext] - self.array[self.inextp]
        if value == self.MBIG:
            value -= 1
        if value < 0:
            value += self.MBIG
        self.array[self.inext] = value
        self.draws += 1
        return value * (1.0 / self.MBIG)

    def next(self, max_value):
        return int(self.sample() * max_value)


def observed_cells(seed):
    sample = json.loads((ROOT / f".tools/samples/{seed}.json").read_text(encoding="utf-8-sig"))
    zone = "LightContainment"
    rooms = sample["zones"][zone]
    index = sample["atlasIndex"][zone]
    match, x_field, y_field, sx, sy, ox, oy = fit(zone, index, rooms)[0]
    assert match == len(rooms)
    pixels = DATA[zone]["atlases"][index]["pixels"]
    cells = []
    for room in rooms:
        x = ox + sx * int(room[x_field] / 5)
        y = oy + sy * int(room[y_field] / 5)
        pair = COLORS[tuple(pixels[y * 32 + x])]
        cells.append({"room": room, "pixel": (x, y), "pair": pair})
    return index, cells


def main():
    for seed in (1, 2, 4, 9, 14, 224593721):
        if seed == 224593721:
            continue
        index, cells = observed_cells(seed)
        fixed_bad = [c for c in cells if len(c["pair"]["rotations"]) == 1
                     and c["room"]["rotY"] != c["pair"]["rotations"][0]]
        print("seed", seed, "atlas", index, "cells", len(cells),
              "multi", Counter(len(c["pair"]["rotations"]) for c in cells),
              "fixed_rot_mismatch", len(fixed_bad))
        ordered = sorted(cells, key=lambda c: (-c["pixel"][1], c["pixel"][0]))
        rng = LegacyRandom(seed)
        assert rng.next(5) == index
        for cell in ordered:
            expected = cell["pair"]["rotations"][rng.next(len(cell["pair"]["rotations"]))]
            assert expected == cell["room"]["rotY"]
        for i in range(len(ordered) - 1, 0, -1):
            k = rng.next(i + 1)
            ordered[i], ordered[k] = ordered[k], ordered[i]
        order_match = sum(cell["room"]["id"] == i for i, cell in enumerate(ordered))
        print("shuffle match", order_match, "/", len(cells), "draws", rng.draws)
        for order in ("y+x+", "y-x+", "x+y+", "x-y+"):
            sorted_cells = sorted(cells, key=(lambda c: (c["pixel"][1], c["pixel"][0]))
                                  if order == "y+x+" else
                                  (lambda c: (-c["pixel"][1], c["pixel"][0]))
                                  if order == "y-x+" else
                                  (lambda c: (c["pixel"][0], c["pixel"][1]))
                                  if order == "x+y+" else
                                  (lambda c: (c["pixel"][0], -c["pixel"][1])))
            for all_cells in (False, True):
                for skip in range(0, 10):
                    rng = LegacyRandom(seed)
                    rng.next(5)
                    for _ in range(skip):
                        rng.sample()
                    correct = 0
                    total = 0
                    for cell in sorted_cells:
                        rotations = cell["pair"]["rotations"]
                        if len(rotations) > 1 or all_cells:
                            got = rotations[rng.next(len(rotations))]
                            if len(rotations) > 1:
                                total += 1
                                correct += got == cell["room"]["rotY"]
                    if total and correct == total:
                        print("ROTATION MATCH", order, "all", all_cells, "skip", skip, "draws", rng.draws)


if __name__ == "__main__":
    main()
