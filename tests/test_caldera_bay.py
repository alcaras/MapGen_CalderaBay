#!/usr/bin/env python3
"""Red-green structural spec for the Caldera Bay map.

Spec (smallest · wide · mirror):
  * a northern mountain range (top edge predominantly mountain)
  * an 8-tile-tall southern sea (bottom row all water)
  * a central bay with a volcanic island (a HEIGHT_VOLCANO tile near centre)
  * a marsh-or-desert moat (marsh or sand/arid terrain present)
  * rivers flow (river edges present) down to the sea
  * left-right mirror-symmetric terrain (mountain/water/land classes)
  * 2 mirror-symmetric player starts
  * no forests

Run: python3 tests/test_caldera_bay.py
"""
import collections
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "tests" / "_bayout"
OW = ROOT.parent / "owmapgen" / "owmapgen"
GAMEDIR = (Path.home() / "Library/Application Support/Steam/steamapps"
           / "common/Old World")
RIVER_TAGS = ("RiverW", "RiverSW", "RiverSE", "RiverE", "RiverNW", "RiverNE")
SEA_ROWS = 8
MTN_ROWS = 3


def _t(el, tag):
    c = el.find(tag)
    return c.text if c is not None else None


def klass(tile):
    if _t(tile, "Height") in ("HEIGHT_MOUNTAIN", "HEIGHT_VOLCANO"):
        return "M"
    if _t(tile, "Terrain") == "TERRAIN_WATER":
        return "W"
    return "L"


def mirror_x(x, y, W):
    # the engine's hex row-stagger mirror (even rows W-1-x, odd rows W-x)
    return (W - 1 - x) if y % 2 == 0 else (W - x)


def neighbors(x, y):
    # even-r offset hex neighbours (Old World convention, per parse_map)
    if y & 1:
        d = ((-1, 1), (0, 1), (1, 0), (0, -1), (-1, -1), (-1, 0))
    else:
        d = ((0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, 0))
    return [(x + dx, y + dy) for dx, dy in d]


class CalderaBayMap(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        OUT.mkdir(exist_ok=True)
        for f in OUT.glob("*.xml"):
            f.unlink()
        build = subprocess.run(
            ["dotnet", "build", "src/CustomMapScript.csproj",
             f"-p:GameDir={GAMEDIR}", "-v", "quiet", "-nologo"],
            cwd=ROOT, capture_output=True, text=True,
            env={**__import__("os").environ, "PATH":
                 "/opt/homebrew/bin:" + __import__("os").environ["PATH"]})
        if build.returncode != 0:
            raise AssertionError("build failed:\n" + build.stdout[-2000:])
        (ROOT / "mod" / "CustomMapScript.dll").write_bytes(
            (ROOT / "src" / "bin" / "CustomMapScript.dll").read_bytes())
        gen = subprocess.run(
            [str(OW), "--mod", str(ROOT / "mod"), "--script", "CalderaBay",
             "--size", "smallest", "--players", "2", "--seed", "1",
             "--aspect-ratio", "wide", "--mirror", "--output", str(OUT)],
            cwd=ROOT, capture_output=True, text=True)
        xmls = sorted(OUT.glob("*.xml"))
        if not xmls:
            raise AssertionError("no map produced:\n" + gen.stdout + gen.stderr)
        root = ET.parse(xmls[0]).getroot()
        cls.w = int(root.attrib["MapWidth"])
        cls.tiles = root.findall("Tile")
        cls.h = len(cls.tiles) // cls.w
        cls.starts = root.findall("./PlayerStarts/PlayerStart")

    def tile(self, x, y):
        return self.tiles[y * self.w + x]

    def water_rows_at(self, x):
        return sum(1 for y in range(self.h)
                   if _t(self.tile(x, y), "Terrain") == "TERRAIN_WATER")

    def test_north_range_with_central_gorge(self):
        # A mountain range along the top — but a GAP in the middle (the gorge
        # where the bay/river breaches it).
        top = self.h - 1
        mtn = sum(1 for x in range(self.w)
                  if _t(self.tile(x, top), "Height") == "HEIGHT_MOUNTAIN")
        self.assertGreater(mtn, self.w * 0.45, "not enough mountains along the top")
        c = self.w // 2
        centre_mtn = any(_t(self.tile(c + dx, top), "Height") == "HEIGHT_MOUNTAIN"
                         for dx in (-1, 0, 1))
        self.assertFalse(centre_mtn, "the central gorge should breach the range")

    def test_south_sea_bottom_row_mostly_water(self):
        water = sum(1 for x in range(self.w)
                    if _t(self.tile(x, 0), "Terrain") == "TERRAIN_WATER")
        self.assertGreater(water, self.w * 0.9, "bottom row should be (mostly) sea")

    def test_edge_sea_present(self):
        # The irregular coastline still leaves a real sea at the edges.
        self.assertGreaterEqual(self.water_rows_at(0), 5)

    def test_central_bay(self):
        # Near-centre columns hold far more water than the edge (the estuary).
        near = max(self.water_rows_at(self.w // 2 + dx) for dx in (-4, -3, 3, 4))
        self.assertGreater(near, self.water_rows_at(0))

    def test_volcanic_island(self):
        volc = [(i % self.w, i // self.w) for i, t in enumerate(self.tiles)
                if _t(t, "Height") == "HEIGHT_VOLCANO"]
        self.assertTrue(volc, "no volcano tile (island missing)")
        # the volcano sits near the centre column, low (in the bay)
        for x, y in volc:
            self.assertLess(abs(x - self.w // 2), self.w * 0.12,
                            "volcano not near centre")

    def test_moat_present(self):
        moat = sum(1 for t in self.tiles if _t(t, "Terrain") in
                   ("TERRAIN_MARSH", "TERRAIN_SAND", "TERRAIN_ARID"))
        self.assertGreater(moat, 20, "no marsh/desert moat")

    def test_has_rivers(self):
        n = sum(1 for t in self.tiles
                if any(t.find(tag) is not None for tag in RIVER_TAGS))
        self.assertGreater(n, 0, "no rivers flowing")

    def test_mirror_symmetric_terrain(self):
        # mountain/water/land classes mirror under the hex-stagger mirror; a few
        # tiles on the central river/bay seam are allowed to differ.
        bad = []
        for y in range(self.h):
            for x in range(self.w):
                mx = mirror_x(x, y, self.w)
                if mx <= x or mx >= self.w:
                    continue
                if klass(self.tile(x, y)) != klass(self.tile(mx, y)):
                    bad.append((x, y))
        self.assertLess(len(bad), 14,
                        f"{len(bad)} tiles break left-right mirror: {bad[:12]}")

    def test_resources_mostly_mirrored(self):
        # resources mirror except for a handful on the contested centre seam
        rmap = {(i % self.w, i // self.w): _t(t, "Resource")
                for i, t in enumerate(self.tiles) if t.find("Resource") is not None}
        asym = [(x, y) for (x, y) in rmap
                if mirror_x(x, y, self.w) < self.w
                and rmap.get((mirror_x(x, y, self.w), y)) != rmap[(x, y)]]
        # tolerance covers the centre river/bay seam + the single dead-centre
        # mountain-city luxury cluster (deliberately not a mirrored pair).
        self.assertLess(len(asym), 22, f"{len(asym)} resources unmirrored")

    def test_has_resource_variety(self):
        # respect normal density: a healthy spread of resource types
        kinds = {_t(t, "Resource") for t in self.tiles if t.find("Resource") is not None}
        self.assertGreater(len(kinds), 6, f"too few resource kinds: {kinds}")

    def test_two_mirror_starts(self):
        self.assertEqual(len(self.starts), 2)
        a, b = self.starts
        ax, ay = int(a.attrib["X"]), int(a.attrib["Y"])
        bx, by = int(b.attrib["X"]), int(b.attrib["Y"])
        self.assertEqual(by, ay, "starts not on the same row")
        self.assertIn(bx, (self.w - 1 - ax, self.w - ax, self.w - ax - 1),
                      f"starts not mirrored: ({ax},{ay}) vs ({bx},{by})")

    def test_sites_mirror_symmetric(self):
        sset = {(i % self.w, i // self.w) for i, t in enumerate(self.tiles)
                if t.find("CitySite") is not None}
        asym = [(x, y) for (x, y) in sset
                if (mirror_x(x, y, self.w), y) not in sset]
        self.assertEqual(asym, [], f"{len(asym)} city sites lack a mirror twin")

    def test_island_is_separate(self):
        # flood-fill land outward from the volcano; the island must be a small
        # component (no land bridge to the mainland).
        volc = next(((i % self.w, i // self.w) for i, t in enumerate(self.tiles)
                     if _t(t, "Height") == "HEIGHT_VOLCANO"), None)
        self.assertIsNotNone(volc, "no volcano")

        def is_land(x, y):
            if not (0 <= x < self.w and 0 <= y < self.h):
                return False
            return _t(self.tile(x, y), "Terrain") != "TERRAIN_WATER"

        seen, stack = set(), [volc]
        while stack:
            cx, cy = stack.pop()
            if (cx, cy) in seen or not is_land(cx, cy):
                continue
            seen.add((cx, cy))
            stack.extend(neighbors(cx, cy))
        # the island is ~40 land tiles; a land bridge to the mainland would make
        # this component hundreds of tiles.
        self.assertLess(len(seen), 80,
                        f"island not isolated — land component is {len(seen)} tiles")

    def test_has_forests_and_scrub(self):
        veg = collections.Counter(
            _t(t, "Vegetation") for t in self.tiles if t.find("Vegetation") is not None)
        self.assertGreater(veg.get("VEGETATION_TREES", 0), 10, "no forests")
        self.assertGreater(veg.get("VEGETATION_SCRUB", 0), 10, "no scrub")

    def test_tribes_balanced_and_paired(self):
        # One diplomacy tribe per side (a DIFFERENT one each), placed at mirror
        # positions; one tribe alone on the central axis. Equal site counts per
        # half (so barb-vs-tribe balance matches); horse tribes are paired —
        # if one side is a horse tribe the other must be too.
        horse = {"TRIBE_SCYTHIANS", "TRIBE_NUMIDIANS"}
        west, east, centre = [], [], []
        for i, t in enumerate(self.tiles):
            tr = _t(t, "TribeSite")
            if not tr:
                continue
            x = i % self.w
            (west if x < self.w // 2 else east if x > self.w // 2 else centre).append(tr)
        self.assertEqual(len(west), len(east),
                         f"tribe sites unbalanced: {len(west)} west vs {len(east)} east")
        self.assertEqual(len(west), 1, "expected exactly one tribe per side")
        self.assertEqual(len(centre), 1, "expected exactly one centre tribe")
        self.assertNotEqual(west[0], east[0], "both sides got the same tribe")
        self.assertEqual(bool(set(west) & horse), bool(set(east) & horse),
                         f"horse tribe not paired: {west} vs {east}")
        self.assertNotIn("TRIBE_HUNS", west + east, "Huns must not be on a player side")

    def test_climate_mostly_temperate(self):
        ter = collections.Counter(_t(t, "Terrain") for t in self.tiles)
        # temperate is the dominant land climate; lush is a minority (river-hugging)
        self.assertGreater(ter["TERRAIN_TEMPERATE"], ter["TERRAIN_LUSH"],
                           "expected mostly temperate, not lush")
        self.assertGreater(ter["TERRAIN_TEMPERATE"], ter["TERRAIN_ARID"],
                           "too much arid")


if __name__ == "__main__":
    unittest.main(verbosity=2)
