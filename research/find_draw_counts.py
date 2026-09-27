"""Locate RNG positions of downstream atlas choices after LCZ shuffle."""

from collections import Counter

from simulate_map import cells_for, load_truth
from trace_rng import LegacyRandom


def main():
    samples = []
    for seed in range(1, 51):
        truth = load_truth(seed)
        zone = "LightContainment"
        cells = cells_for(zone, truth["atlasIndex"][zone], truth["zones"][zone])
        rng = LegacyRandom(seed)
        assert rng.next(5) == truth["atlasIndex"][zone]
        for cell in cells:
            rng.next(len(cell["pair"]["rotations"]))
        for i in range(len(cells) - 1, 0, -1):
            rng.next(i + 1)
        samples.append((seed, truth, rng, len(cells)))
    offsets = Counter()
    for offset in range(0, 70):
        matches = 0
        for seed, truth, _, count in samples:
            rng = LegacyRandom(seed)
            for _ in range(2 * count + offset):
                rng.sample()
            matches += rng.next(10) == truth["atlasIndex"]["HeavyContainment"]
        offsets[offset] = matches
    print("HCZ offset after 2 * LCZ rooms:", offsets.most_common(20))


if __name__ == "__main__":
    main()
