#!/usr/bin/env python3
"""Structural spec for Caldera Bay — the engine-native (CoastalRainBasin) map.

Caldera Bay subclasses the built-in CoastalRainBasin generator and LOCKS a few
defining features (a sea on one edge, the central bay, the opposite-edge range
with a gorge, flanking spurs, a volcanic-island seed), letting the engine grow
organic elevation/rivers/lakes/rain-shadow climate around them.

Spec (the duel's own 64×38 wide size, mirror):
  * exactly ONE sea, on the south OR north edge; the bay drains into it
  * a mountain range on the OPPOSITE edge with a central gorge
  * a small volcanic island in the bay (exactly one volcano) with a city site
  * 14–18 city sites; the placed sites are ≥8 apart (the engine minimum)
  * left–right mirror symmetric
  * rivers flow and reach water (no orphans)
  * one diplomacy tribe per player (different each side, horse-paired), never
    Barbarians; a centre tribe

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
HORSE = {"TRIBE_SCYTHIANS", "TRIBE_NUMIDIANS"}
NON_DIPLO = ("BARBARIAN", "RAIDER", "REBEL", "ANARCHY")


def _t(el, tag):
    c = el.find(tag)
    return c.text if c is not None else None


def mirror_x(x, y, W):
    return (W - 1 - x) if y % 2 == 0 else (W - x)


def neighbors(x, y):
    d = (((-1, 1), (0, 1), (1, 0), (0, -1), (-1, -1), (-1, 0)) if y & 1
         else ((0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, 0)))
    return [(x + dx, y + dy) for dx, dy in d]


def hexdist(a, b):
    aq = a[0] - ((a[1] + (a[1] & 1)) // 2)
    bq = b[0] - ((b[1] + (b[1] & 1)) // 2)
    return (abs(aq - bq) + abs((-aq - a[1]) - (-bq - b[1])) + abs(a[1] - b[1])) // 2


class CalderaBayMap(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        OUT.mkdir(exist_ok=True)
        for f in OUT.glob("*.xml"):
            f.unlink()
        import os
        env = {**os.environ, "PATH": "/opt/homebrew/bin:" + os.environ["PATH"]}
        build = subprocess.run(
            ["dotnet", "build", "src/CustomMapScript.csproj",
             f"-p:GameDir={GAMEDIR}", "-v", "quiet", "-nologo"],
            cwd=ROOT, capture_output=True, text=True, env=env)
        if build.returncode != 0:
            raise AssertionError("build failed:\n" + build.stdout[-2000:])
        (ROOT / "mod" / "CustomMapScript.dll").write_bytes(
            (ROOT / "src" / "bin" / "CustomMapScript.dll").read_bytes())
        subprocess.run(
            [str(OW), "--mod", str(ROOT / "mod"), "--script", "CalderaBay",
             "--size", "smallest", "--players", "2", "--seed", "1",
             "--aspect-ratio", "wide", "--mirror", "--output", str(OUT)],
            cwd=ROOT, capture_output=True, text=True)
        xmls = sorted(OUT.glob("*.xml"))
        if not xmls:
            raise AssertionError("no map produced")
        root = ET.parse(xmls[0]).getroot()
        cls.w = int(root.attrib["MapWidth"])
        cls.tiles = root.findall("Tile")
        cls.h = len(cls.tiles) // cls.w
        cls.starts = root.findall("./PlayerStarts/PlayerStart")

    def tile(self, x, y):
        return self.tiles[y * self.w + x]

    def is_water(self, x, y):
        return _t(self.tile(x, y), "Terrain") == "TERRAIN_WATER"

    def sea_side(self):
        sw = sum(1 for x in range(self.w) if self.is_water(x, 0))
        nw = sum(1 for x in range(self.w) if self.is_water(x, self.h - 1))
        return ("south", sw) if sw > nw else ("north", nw)

    # ---- terrain frame ----
    def test_custom_size(self):
        self.assertEqual((self.w, self.h), (64, 43), "expected the duel's 64×43 size")

    def test_sea_at_least_five_tall(self):
        # the sea band on the sea edge is ≥5 tiles in every column (a line of
        # ships can't easily wall it off).
        side, _ = self.sea_side()
        worst = 99
        for x in range(self.w):
            d = 0
            ys = range(self.h) if side == "south" else range(self.h - 1, -1, -1)
            for y in ys:
                if self.is_water(x, y):
                    d += 1
                else:
                    break
            worst = min(worst, d)
        self.assertGreaterEqual(worst, 5, "the sea should be ≥5 tiles tall everywhere")

    def test_one_sea_edge(self):
        side, edge = self.sea_side()
        self.assertGreater(edge, self.w * 0.9, "the sea edge should be ~all water")
        far = self.h - 1 if side == "south" else 0
        farw = sum(1 for x in range(self.w) if self.is_water(x, far))
        self.assertLess(farw, self.w * 0.3,
                        "no second sea behind the range (far edge should be land)")

    def test_range_on_far_edge_with_gorge(self):
        side, _ = self.sea_side()
        row = self.h - 1 if side == "south" else 0   # the range is OPPOSITE the sea
        mtn = sum(1 for x in range(self.w)
                  if _t(self.tile(x, row), "Height") == "HEIGHT_MOUNTAIN")
        self.assertGreater(mtn, self.w * 0.45, "not enough mountains on the range edge")
        c = self.w // 2
        gorge_clear = not any(_t(self.tile(c + dx, row), "Height") == "HEIGHT_MOUNTAIN"
                              for dx in (-1, 0, 1))
        self.assertTrue(gorge_clear, "the central gorge should breach the range")

    def test_bay_drains_into_the_sea(self):
        # the central bay reaches well inland from the sea edge.
        side, _ = self.sea_side()
        c = self.w // 2
        col = [self.is_water(c + dx, y) for dx in (-1, 0, 1) for y in range(self.h)]
        rows = [y for dx in (-1, 0, 1) for y in range(self.h) if self.is_water(c + dx, y)]
        if side == "south":
            reach = max(rows) / (self.h - 1)
        else:
            reach = (self.h - 1 - min(rows)) / (self.h - 1)
        self.assertGreater(reach, 0.4, "the bay should cut well inland toward the range")

    # ---- the volcanic island ----
    def test_one_volcano(self):
        volc = sum(1 for t in self.tiles if _t(t, "Height") == "HEIGHT_VOLCANO")
        self.assertEqual(volc, 1, "expected exactly one volcano (the island caldera)")

    def test_island_isolated_with_site(self):
        c = self.w // 2

        def land(x, y):
            return 0 <= x < self.w and 0 <= y < self.h and not self.is_water(x, y)

        # the island is a small land component near the centre column
        seen, found = set(), False
        for y in range(self.h):
            for x in range(c - 3, c + 4):
                if (x, y) in seen or not land(x, y):
                    continue
                comp, stack = [], [(x, y)]
                while stack:
                    cx, cy = stack.pop()
                    if (cx, cy) in seen or not land(cx, cy):
                        continue
                    seen.add((cx, cy)); comp.append((cx, cy))
                    stack.extend(neighbors(cx, cy))
                if 16 <= len(comp) <= 60 and any(
                        self.tile(a, b).find("CitySite") is not None for a, b in comp):
                    found = True
        self.assertTrue(found, "the bay island should be small, isolated, and hold a site")

    # ---- city sites ----
    def test_site_count(self):
        n = sum(1 for t in self.tiles if t.find("CitySite") is not None)
        self.assertTrue(14 <= n <= 18, f"expected 14–18 city sites, got {n}")

    def test_placed_sites_spaced(self):
        # our placed sites (not the host's ACTIVE_START capital marker) are ≥8 apart
        sites = [(i % self.w, i // self.w) for i, t in enumerate(self.tiles)
                 if t.find("CitySite") is not None
                 and _t(t, "CitySite") != "ACTIVE_START"]
        md = min((hexdist(a, b) for i, a in enumerate(sites) for b in sites[i + 1:]),
                 default=99)
        self.assertGreaterEqual(md, 8, f"placed sites closer than 8 ({md}) → urban merge")

    # ---- symmetry, rivers ----
    def test_mirror_symmetric(self):
        def klass(t):
            if _t(t, "Height") in ("HEIGHT_MOUNTAIN", "HEIGHT_VOLCANO"):
                return "M"
            return "W" if _t(t, "Terrain") == "TERRAIN_WATER" else "L"
        bad = 0
        for y in range(self.h):
            for x in range(self.w):
                mx = mirror_x(x, y, self.w)
                if mx <= x or mx >= self.w:
                    continue
                if klass(self.tile(x, y)) != klass(self.tile(mx, y)):
                    bad += 1
        self.assertLess(bad, 14, f"{bad} tiles break left-right mirror")

    def test_rivers_reach_water(self):
        rt = set((i % self.w, i // self.w) for i, t in enumerate(self.tiles)
                 if any(t.find(tag) is not None for tag in RIVER_TAGS))
        self.assertGreater(len(rt), 0, "no rivers")

        def wat(x, y):
            return (0 <= x < self.w and 0 <= y < self.h
                    and (self.is_water(x, y) or _t(self.tile(x, y), "Height") == "HEIGHT_LAKE"))
        seen, orphans = set(), 0
        for s in rt:
            if s in seen:
                continue
            comp, stack = [], [s]
            while stack:
                c = stack.pop()
                if c in seen or c not in rt:
                    continue
                seen.add(c); comp.append(c)
                for nb in neighbors(*c):
                    if nb in rt:
                        stack.append(nb)
            if not any(wat(nx, ny) for cx, cy in comp for nx, ny in neighbors(cx, cy)):
                orphans += 1
        self.assertEqual(orphans, 0, "every river must reach the sea or a lake")

    def test_two_mirror_starts(self):
        self.assertEqual(len(self.starts), 2)
        a, b = self.starts
        ax, ay = int(a.attrib["X"]), int(a.attrib["Y"])
        bx, by = int(b.attrib["X"]), int(b.attrib["Y"])
        self.assertEqual(by, ay)
        self.assertIn(bx, (self.w - 1 - ax, self.w - ax, self.w - ax - 1))

    # ---- tribes ----
    def test_tribes(self):
        # Per side: ONE named tribe type holding several sites, plus one barb
        # camp; capitals/expansions free; the centre holds a named tribe garrison
        # (the highland prize) and the island is barb-guarded.
        c = self.w // 2
        west, east, centre = [], [], []
        for i, t in enumerate(self.tiles):
            tr = _t(t, "TribeSite")
            if not tr:
                continue
            x = i % self.w
            (west if x < c else east if x > c else centre).append(tr)
        cnamed = set(s for s in centre if "BARBARIAN" not in s)
        wnamed = set(s for s in west if "BARBARIAN" not in s) - cnamed
        enamed = set(s for s in east if "BARBARIAN" not in s) - cnamed
        self.assertLessEqual(len(wnamed), 1, f"one side tribe west, got {wnamed}")
        self.assertLessEqual(len(enamed), 1, f"one side tribe east, got {enamed}")
        for s in wnamed | enamed:
            self.assertFalse(any(b in s for b in ("RAIDER", "REBEL", "ANARCHY")),
                             f"player tribe must be a real tribe, not {s}")
        self.assertEqual(bool(wnamed & HORSE), bool(enamed & HORSE),
                         "horse tribes must be paired")
        self.assertGreaterEqual(len(centre), 1, "centre garrisons should exist")


class CalderaBaySweep(unittest.TestCase):
    """Anomaly battery over MANY seeds — single-seed tests let seed-specific
    bugs hide (lake chains along the range, stray inland water, orphan rivers,
    walled-in tarns). Every visual bug report becomes an invariant here."""

    SEEDS = (2, 3, 5, 8)
    CLIMATES = ("MAP_OPTION_CALDERA_MEDITERRANEAN", "MAP_OPTION_CALDERA_NORTHERN")

    @classmethod
    def setUpClass(cls):
        cls.maps = []
        for opt in cls.CLIMATES:
            for seed in cls.SEEDS:
                out = OUT / f"sweep_{opt[-4:]}_{seed}"
                out.mkdir(parents=True, exist_ok=True)
                for f in out.glob("*.xml"):
                    f.unlink()
                subprocess.run(
                    [str(OW), "--mod", str(ROOT / "mod"), "--script", "CalderaBay",
                     "--size", "smallest", "--players", "2", "--seed", str(seed),
                     "--aspect-ratio", "wide", "--mirror",
                     "--map-option", f"MAP_OPTIONS_MULTI_CALDERA_CLIMATE={opt}",
                     "--output", str(out)], cwd=ROOT, capture_output=True)
                xmls = sorted(out.glob("*.xml"))
                if xmls:
                    root = ET.parse(xmls[0]).getroot()
                    cls.maps.append((f"{opt.split('_')[-1]}-{seed}", root))
        if len(cls.maps) < len(cls.SEEDS) * len(cls.CLIMATES):
            raise AssertionError("sweep generation failed")

    def each(self):
        for name, root in self.maps:
            w = int(root.attrib["MapWidth"])
            tiles = root.findall("Tile")
            yield name, w, len(tiles) // w, tiles

    def test_sweep_no_water_anomalies(self):
        problems = []
        for name, w, h, tiles in self.each():
            def tl(x, y):
                return tiles[y * w + x]
            def wat(x, y):
                return _t(tl(x, y), "Terrain") == "TERRAIN_WATER"
            def hgt(x, y):
                return _t(tl(x, y), "Height")
            south = (sum(1 for x in range(w) if wat(x, 0))
                     > sum(1 for x in range(w) if wat(x, h - 1)))

            def river(x, y):
                return any(tl(x, y).find(tag) is not None for tag in RIVER_TAGS)

            # river-fed highland tarns are a legitimate engine feature (rivers
            # that can't reach the sea pool into lakes); ARTIFACT ponds — with no
            # river anywhere near — are not, and neither is water walled in rock.
            range_lakes = walled = 0
            for i, t in enumerate(tiles):
                if _t(t, "Height") != "HEIGHT_LAKE":
                    continue
                x, y = i % w, i // w
                d = y if south else h - 1 - y
                fed = river(x, y) or any(river(nx, ny) for nx, ny in neighbors(x, y)
                                         if 0 <= nx < w and 0 <= ny < h)
                if d > 0.72 * (h - 1) and not fed:
                    range_lakes += 1
                m = sum(1 for nx, ny in neighbors(x, y)
                        if 0 <= nx < w and 0 <= ny < h
                        and hgt(nx, ny) in ("HEIGHT_MOUNTAIN", "HEIGHT_VOLCANO"))
                if m >= 2:
                    walled += 1
            # stray (non-lake) water disconnected from the sea
            seen, stack = set(), [(x, 0 if south else h - 1) for x in range(w)
                                  if wat(x, 0 if south else h - 1)]
            while stack:
                cx, cy = stack.pop()
                if (cx, cy) in seen or not (0 <= cx < w and 0 <= cy < h) or not wat(cx, cy):
                    continue
                seen.add((cx, cy))
                stack.extend(neighbors(cx, cy))
            stray = sum(1 for i, t in enumerate(tiles)
                        if wat(i % w, i // w) and (i % w, i // w) not in seen
                        and _t(t, "Height") != "HEIGHT_LAKE")
            if walled:
                problems.append(f"{name}: {walled} lakes walled in mountains")
            if stray:
                problems.append(f"{name}: {stray} stray water tiles")
            if range_lakes > 0:
                problems.append(f"{name}: {range_lakes} riverless artifact ponds in the range zone")
        self.assertEqual(problems, [], "water anomalies:\n" + "\n".join(problems))

    def test_sweep_structure(self):
        problems = []
        for name, w, h, tiles in self.each():
            sites = sum(1 for t in tiles if t.find("CitySite") is not None)
            volc = sum(1 for t in tiles if _t(t, "Height") == "HEIGHT_VOLCANO")
            def wat(x, y):
                return _t(tiles[y * w + x], "Terrain") == "TERRAIN_WATER"
            south = (sum(1 for x in range(w) if wat(x, 0))
                     > sum(1 for x in range(w) if wat(x, h - 1)))
            far = (sum(1 for x in range(w) if wat(x, h - 1)) if south
                   else sum(1 for x in range(w) if wat(x, 0)))
            depth = 99
            for x in range(w):
                dcol = 0
                for y in (range(h) if south else range(h - 1, -1, -1)):
                    if wat(x, y):
                        dcol += 1
                    else:
                        break
                depth = min(depth, dcol)
            if not 14 <= sites <= 18:
                problems.append(f"{name}: {sites} sites")
            # no SECOND mountain wall in the piedmont band below the range, and
            # no city sites wedged on a shelf against the range
            for y in range(h):
                d = y if south else h - 1 - y
                row_mtn = sum(1 for x in range(w)
                              if _t(tiles[y * w + x], "Height") == "HEIGHT_MOUNTAIN")
                if h - 9 <= d < h - 3 and row_mtn > w * 0.3:
                    problems.append(f"{name}: second wall ({row_mtn} mtns at d={d})")
                if d >= h - 8:
                    row_sites = sum(1 for x in range(w)
                                    if tiles[y * w + x].find("CitySite") is not None)
                    if row_sites:
                        problems.append(f"{name}: {row_sites} sites on the range shelf (d={d})")
            if volc != 1:
                problems.append(f"{name}: {volc} volcanoes")
            if far >= w * 0.3:
                problems.append(f"{name}: second sea ({far} far-edge water)")
            if depth < 5:
                problems.append(f"{name}: sea only {depth} tall")
            # coastline must meander (wobbled band) — several distinct depths
            depths = set()
            for x in range(w):
                dcol = 0
                for y in (range(h) if south else range(h - 1, -1, -1)):
                    if wat(x, y):
                        dcol += 1
                    else:
                        break
                depths.add(dcol)
            if len(depths) < 4:
                problems.append(f"{name}: coastline too straight ({len(depths)} depths)")
            # the island is a real island (≥16 land tiles), and both prizes have
            # extra resources around them (the organic rich-roll + the floor)
            volc = next(((i % w, i // w) for i, t in enumerate(tiles)
                         if _t(t, "Height") == "HEIGHT_VOLCANO"), None)
            if volc:
                isl, stack = set(), [volc]
                while stack:
                    cx, cy = stack.pop()
                    if (cx, cy) in isl or not (0 <= cx < w and 0 <= cy < h) or wat(cx, cy):
                        continue
                    isl.add((cx, cy))
                    stack.extend(neighbors(cx, cy))
                if len(isl) < 16:
                    problems.append(f"{name}: island only {len(isl)} tiles")
                isite = next(((x, y) for x, y in isl
                              if tiles[y * w + x].find("CitySite") is not None), None)
                if isite:
                    near = sum(1 for i, t in enumerate(tiles)
                               if t.find("Resource") is not None
                               and hexdist((i % w, i // w), isite) <= 3)
                    if near < 3:
                        problems.append(f"{name}: island prize has {near} resources")
            c = w // 2
            cen = [(c, y) for y in range(h)
                   if tiles[y * w + c].find("CitySite") is not None]
            if cen:
                hp = max(cen, key=lambda p: (p[1] if south else h - 1 - p[1]))
                near = sum(1 for i, t in enumerate(tiles)
                           if t.find("Resource") is not None
                           and hexdist((i % w, i // w), hp) <= 4)
                if near < 3:
                    problems.append(f"{name}: highland prize has {near} resources")
            # player-side tribes must be NAMED tribes, never barb-type camps —
            # and every tribe marker must sit ON a city site (tribal settlements
            # are city sites in Old World; markers elsewhere degrade to barbs)
            for i, t in enumerate(tiles):
                tr = _t(t, "TribeSite")
                if not tr:
                    continue
                if t.find("CitySite") is None:
                    problems.append(f"{name}: tribe {tr} not on a city site")
                if any(b in tr for b in ("RAIDER", "REBEL", "ANARCHY")):
                    problems.append(f"{name}: {tr} placed as a camp")
            # tribe-site distribution: 2-3 barb camps, the rest of the held
            # sites named tribes, and >=4 free sites (starts + expansions)
            n_free = n_barb = n_tribe = 0
            for i, t in enumerate(tiles):
                if t.find("CitySite") is None:
                    continue
                tr = _t(t, "TribeSite")
                if not tr:
                    n_free += 1
                elif "BARBARIAN" in tr:
                    n_barb += 1
                else:
                    n_tribe += 1
            if n_barb != 4:
                problems.append(f"{name}: {n_barb} barb camps (want 4 — 2 per side)")
            if n_tribe < 5:
                problems.append(f"{name}: only {n_tribe} tribe-held sites")
            if n_free < 4:
                problems.append(f"{name}: only {n_free} free sites")
            # spur walls must run unbroken into the range: every row of the
            # piedmont band has at least one mountain on each side
            for dd in range(h - 7, h - 3):
                yy = dd if south else h - 1 - dd
                wm = sum(1 for x in range(3, c)
                         if _t(tiles[yy * w + x], "Height") == "HEIGHT_MOUNTAIN")
                em = sum(1 for x in range(c + 1, w - 3)
                         if _t(tiles[yy * w + x], "Height") == "HEIGHT_MOUNTAIN")
                if wm == 0 or em == 0:
                    problems.append(f"{name}: spur gap in piedmont row d={dd}")
            # no ghost urban: every urban tile belongs to a site within 2
            site_xy = [(i % w, i // w) for i, t in enumerate(tiles)
                       if t.find("CitySite") is not None]
            for i, t in enumerate(tiles):
                if _t(t, "Terrain") != "TERRAIN_URBAN":
                    continue
                xy = (i % w, i // w)
                if not any(hexdist(xy, s) <= 2 for s in site_xy):
                    problems.append(f"{name}: ghost urban at {xy}")
        self.assertEqual(problems, [], "structure problems:\n" + "\n".join(problems))


if __name__ == "__main__":
    unittest.main(verbosity=2)
