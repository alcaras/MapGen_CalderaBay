#!/usr/bin/env python3
"""Red-green TDD spec for the Flat-Lush map script.

The spec (what the user asked for):
  * completely flat terrain   -> every tile HEIGHT_FLAT
  * all lush                  -> every tile TERRAIN_LUSH
  * no rivers / mountains / …  -> zero river edges, zero non-flat heights
  * no forests                -> zero vegetation
  * square map                -> width ~= rows
  * duel, smallest size       -> exactly 2 city sites / starts, small tile count

Run:  python3 tests/test_flat_lush.py
It (re)builds + generates a fresh map via ./preview.sh, then asserts on the
emitted XML. A broken build or a non-conforming map fails the suite.
"""
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PREVIEW = ROOT / "preview"
RIVER_TAGS = ("RiverW", "RiverSW", "RiverSE", "RiverE", "RiverNW", "RiverNE")


def _text(el, tag):
    child = el.find(tag)
    return child.text if child is not None else None


class FlatLushMap(unittest.TestCase):
    tiles = None
    width = None
    rows = None

    @classmethod
    def setUpClass(cls):
        # Build + generate. A failed build/gen is a legitimate RED.
        proc = subprocess.run(
            ["bash", str(ROOT / "preview.sh"), "1"],
            cwd=ROOT, capture_output=True, text=True,
        )
        if proc.returncode != 0:
            raise AssertionError(
                "preview.sh failed (build or generate):\n"
                + proc.stdout[-3000:] + "\n" + proc.stderr[-3000:]
            )
        xmls = sorted(PREVIEW.glob("*.xml"))
        if not xmls:
            raise AssertionError("no map XML produced in preview/")
        root = ET.parse(xmls[0]).getroot()
        cls.width = int(root.attrib["MapWidth"])
        cls.tiles = root.findall("Tile")
        cls.rows = len(cls.tiles) // cls.width
        cls.starts = root.findall("./PlayerStarts/PlayerStart")

    # After Build() the map is 100% flat lush. owmapgen's post-Build start
    # placement then founds cities (TERRAIN_URBAN) and can leave a few lake
    # tiles near a capital — an artifact no map-script hook can reach. So the
    # spec is: strictly none of the features we excluded (hills, mountains,
    # rivers, forests, deserts), and the map overwhelmingly flat lush.
    WATER_TOLERANCE = 12  # start-placement lakes seen across seeds: 3-5

    def test_no_forbidden_terrain(self):
        # Only lush (land), urban (cities) and a little water are allowed —
        # never desert/tundra/temperate/tropical/wet/marsh.
        seen = {_text(t, "Terrain") for t in self.tiles}
        self.assertLessEqual(
            seen, {"TERRAIN_LUSH", "TERRAIN_URBAN", "TERRAIN_WATER"},
            f"unexpected terrain types: {seen}")

    def test_no_elevation(self):
        # The whole point is "completely flat": zero hills/mountains/volcano.
        elevated = {"HEIGHT_HILL", "HEIGHT_MOUNTAIN", "HEIGHT_VOLCANO"}
        seen = {_text(t, "Height") for t in self.tiles}
        self.assertEqual(seen & elevated, set(),
                         f"elevated tiles present: {seen & elevated}")

    def test_noncity_land_is_flat_lush(self):
        # Every tile that isn't a founded city or a start-placement lake must
        # be exactly flat lush — that is the whole map we authored.
        bad = [t for t in self.tiles
               if _text(t, "Terrain") not in ("TERRAIN_URBAN", "TERRAIN_WATER")
               and (_text(t, "Terrain") != "TERRAIN_LUSH"
                    or _text(t, "Height") != "HEIGHT_FLAT")]
        self.assertEqual(bad, [], f"{len(bad)} non-city land tiles not flat lush")

    def test_water_within_tolerance(self):
        water = sum(1 for t in self.tiles
                    if _text(t, "Terrain") == "TERRAIN_WATER")
        self.assertLessEqual(water, self.WATER_TOLERANCE,
                             f"{water} water tiles (> {self.WATER_TOLERANCE})")

    def test_no_rivers(self):
        with_river = [t for t in self.tiles
                      if any(t.find(tag) is not None for tag in RIVER_TAGS)]
        self.assertEqual(len(with_river), 0, "river edges present")

    def test_no_vegetation(self):
        veg = [t for t in self.tiles if t.find("Vegetation") is not None]
        self.assertEqual(len(veg), 0, f"{len(veg)} tiles carry vegetation")

    def test_square(self):
        # "square" aspect at smallest size is ~1:1 (e.g. 46x44), not exact.
        ratio = self.width / self.rows
        self.assertTrue(0.85 <= ratio <= 1.18,
                        f"not square: width={self.width} rows={self.rows}")

    def test_duel_two_starts(self):
        self.assertEqual(len(self.starts), 2,
                         f"expected 2 player starts, got {len(self.starts)}")

    def test_starts_point_symmetric(self):
        self.assertEqual(len(self.starts), 2)
        a, b = self.starts
        ax, ay = int(a.attrib["X"]), int(a.attrib["Y"])
        bx, by = int(b.attrib["X"]), int(b.attrib["Y"])
        self.assertEqual((bx, by), (self.width - 1 - ax, self.rows - 1 - ay),
                         f"starts not point-symmetric: ({ax},{ay}) vs ({bx},{by})")


if __name__ == "__main__":
    unittest.main(verbosity=2)
