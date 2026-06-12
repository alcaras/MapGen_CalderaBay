using System;
using System.Collections.Generic;
using TenCrowns.GameCore;

namespace OwMapCreation
{
    // CALDERA BAY — a mirror duel built on the engine's COASTAL RAIN BASIN
    // generator. Instead of stamping a custom height field (fragile, and it
    // fought the engine's rivers/lakes), we LOCK the few defining features — a
    // central drowned bay, the northern mountain range with a river gorge, the
    // flanking spurs, and a volcanic island seed — and let CoastalRainBasin grow
    // organic elevation, rivers, lakes and rain-shadow climate around them.
    public class MapScriptCalderaBay : CoastalRainBasin
    {
        public MapScriptCalderaBay(ref MapParameters mapParameters, Infos infos)
            : base(ref mapParameters, infos)
        {
        }

        public static new void GetCustomOptionsMulti(List<MapOptionsMultiType> options, Infos infos)
        {
            options.Add(infos.getType<MapOptionsMultiType>("MAP_OPTIONS_MULTI_CALDERA_CLIMATE"));
        }

        // A fixed "wide duel" size — chosen so the engine's natural city sites
        // (at the normal min distance) land at 14–18.
        protected override void SetMapSize()
        {
            base.SetMapSize();              // honour the CHOSEN size and aspect
                                            // ratio (Duel 46x44 sq / 58x34 wide /
                                            // 72x28 ultrawide, Tiny 58x58, ...)
            if ((mapParameters.iWidth & 1) == 1)
                mapParameters.iWidth--;     // the stagger mirror needs even W
            if ((mapParameters.iHeight & 1) == 1)
                mapParameters.iHeight--;    // the point rotation needs even H
        }

        // ---- Climate (latitude) map option → the basin's latitude band ----
        private int ResolveClimate()
        {
            var opt = infos.getType<MapOptionsMultiType>("MAP_OPTIONS_MULTI_CALDERA_CLIMATE");
            MapOptionType choice;
            if ((int)opt >= 0 &&
                mapParameters.gameParams.mapMapMultiOptions.TryGetValue(opt, out choice))
            {
                int c = (int)choice;
                if (c == (int)infos.getType<MapOptionType>("MAP_OPTION_CALDERA_MEDITERRANEAN")) return 0;
                if (c == (int)infos.getType<MapOptionType>("MAP_OPTION_CALDERA_TEMPERATE")) return 1;
                if (c == (int)infos.getType<MapOptionType>("MAP_OPTION_CALDERA_NORTHERN")) return 2;
                return -1;                       // explicit RANDOM → engine latitudes
            }
            return 1;                            // option not set → TEMPERATE default
        }
        private short ClimateLat(short med, short temp, short north, short rand)
        {
            switch (ResolveClimate()) { case 0: return med; case 1: return temp; case 2: return north; default: return rand; }
        }
        public override short MinLatitude { get { return ClimateLat(18, 35, 55, base.MinLatitude); } }
        public override short MaxLatitude { get { return ClimateLat(38, 52, 70, base.MaxLatitude); } }
        // keep lakes to a few (the basin default over-fills our locked terrain)
        protected override short LakePercent { get { return 3; } }

        // A 2-player game only puts ~2 tribes in use, but the duel layout needs
        // THREE distinct roles (west side, east side, central) with a horse-
        // paired side pair. Top the engine's selection up to 4 named tribes,
        // making sure at least two foot tribes are present so a valid same-class
        // side pair always exists.
        protected override void SetTribesToUse()
        {
            base.SetTribesToUse();
            string[] foot = { "TRIBE_GAULS", "TRIBE_VANDALS", "TRIBE_DANES", "TRIBE_THRACIANS" };
            string[] horse = { "TRIBE_SCYTHIANS", "TRIBE_NUMIDIANS" };
            System.Func<string, bool> has = (n) =>
            {
                TribeType tt = infos.getType<TribeType>(n);
                foreach (TribeType u in tribesToUse) if ((int)u == (int)tt) return true;
                return false;
            };
            System.Action<string> add = (n) =>
            {
                TribeType tt = infos.getType<TribeType>(n);
                if ((int)tt >= 0 && !has(n)) tribesToUse.Add(tt);
            };
            int footCount = 0;
            foreach (string n in foot) if (has(n)) footCount++;
            // ensure a pairable foot duo
            for (int i = 0; i < foot.Length && footCount < 2; i++)
                if (!has(foot[i])) { add(foot[i]); footCount++; }
            // top up to 5 named tribes total: the game honours ~2 settlements
            // per tribe-in-use, so 5 tribes buy a 10-settlement budget — room
            // for the central tribe's 4 plus 2-3 sites per side tribe even on
            // an 18-site map (we still only PLACE 3 named tribes on the board)
            int guard = 0;
            while (tribesToUse.Count < 5 && guard++ < 10)
            {
                string pick = (random.Next(3) == 0 && (!has(horse[0]) || !has(horse[1])))
                    ? (has(horse[0]) ? horse[1] : horse[0])
                    : foot[random.Next(foot.Length)];
                if (!has(pick)) add(pick);
            }
        }

        // Mountain-walled valley POCKETS (land with no entrance and no water
        // access) get marked unreachable by the engine and render as dead fog
        // mid-map. Pre-empt it: fill such pockets solid with mountains BEFORE
        // the engine computes unreachable areas — they read as a massif instead.
        protected override void SetUnreachableAreas()
        {
            TameMassifs();         // break giant organic massifs in the plain…
            TamePiedmont();        // …open the engine's second wall…
            SealPockets();         // …then fill whatever stays sealed…
            base.SetUnreachableAreas();   // …so the fog marking sees final passability
        }
        private void SealPockets()
        {
            int W = MapWidth, H = MapHeight, n = W * H;
            int[] comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            var sizes = new List<int>();
            var hasSeaAccess = new List<bool>();
            for (int i = 0; i < n; i++)
            {
                if (comp[i] >= 0) continue;
                TileData t0 = GetTile(i);
                if (t0.Terrain.Equals(WATER_TERRAIN) || t0.Height.Equals(MOUNTAIN_HEIGHT)
                    || t0.Height.Equals(VOLCANO_HEIGHT)) continue;
                int id = sizes.Count; sizes.Add(0); hasSeaAccess.Add(false);
                var st = new Stack<int>(); st.Push(i);
                while (st.Count > 0)
                {
                    int j = st.Pop();
                    if (comp[j] >= 0) continue;
                    TileData t = GetTile(j);
                    if (t.Terrain.Equals(WATER_TERRAIN) || t.Height.Equals(MOUNTAIN_HEIGHT)
                        || t.Height.Equals(VOLCANO_HEIGHT)) continue;
                    comp[j] = id; sizes[id]++;
                    int x = j % W, y = j / W;
                    int[] dx, dy; Neigh(y, out dx, out dy);
                    for (int k = 0; k < 6; k++)
                    {
                        int nx = x + dx[k], ny = y + dy[k];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        TileData nb = GetTile(nx, ny);
                        if (nb.Terrain.Equals(WATER_TERRAIN))
                        {
                            if (!nb.Height.Equals(LAKE_HEIGHT)) hasSeaAccess[id] = true;
                            continue;
                        }
                        if (comp[ny * W + nx] < 0) st.Push(ny * W + nx);
                    }
                }
            }
            int main = -1;
            for (int id = 0; id < sizes.Count; id++)
                if (main < 0 || sizes[id] > sizes[main]) main = id;
            var hasSite = new bool[sizes.Count];
            var isGorge = new bool[sizes.Count];
            CitySiteType noSite = GetTile(0, 0).CitySite;
            for (int i = 0; i < n; i++)
            {
                if (comp[i] < 0) continue;
                if (!GetTile(i).CitySite.Equals(noSite)) hasSite[comp[i]] = true;
                if (mGorge != null && mGorge[i]) isGorge[comp[i]] = true;   // the river's notch stays open
            }
            for (int i = 0; i < n; i++)
            {
                int id = comp[i];
                if (id < 0 || id == main || hasSeaAccess[id] || hasSite[id] || isGorge[id]) continue;
                TileData t = GetTile(i);                  // walled-in pocket → massif
                t.Height = MOUNTAIN_HEIGHT;
                t.Terrain = TEMPERATE_TERRAIN;
            }
        }

        // ---- the defining shape: lock it, then let the engine grow the rest ----
        protected override void GenerateLand()
        {
            LockCaldera();
            base.GenerateLand();
        }

        // Lock a tile's height AND terrain (and set them) so the engine's land
        // generation can't re-flood or re-shape our framed features.
        private void LockWater(TileData t)
        {
            // Lock TERRAIN only (guaranteed water — the engine can't land it) but
            // leave HEIGHT free, so the engine's own coast pass assigns shallow
            // COAST along every shoreline (organic transitions, coastal shipping,
            // and valid tiles for sea resources like pearls around the island).
            LockTileTerrain(t, WATER_TERRAIN, true);
            t.Terrain = WATER_TERRAIN;
            t.Height = OCEAN_HEIGHT;
        }
        private void LockOcean(TileData t)
        {
            // deep water, height-locked: never becomes COAST, so city borders
            // can't claim it — a real moat that takes a boat to cross.
            LockTileTerrain(t, WATER_TERRAIN, true);
            LockTileHeight(t, OCEAN_HEIGHT, true);
            t.Terrain = WATER_TERRAIN;
            t.Height = OCEAN_HEIGHT;
        }
        private void LockLand(TileData t, HeightType h, TerrainType terr)
        {
            LockTileHeight(t, h, true);
            LockTileTerrain(t, terr, true);
            t.Terrain = terr;
            // remember which mountains are OURS (the engine height-locks its own
            // chains too, so IsHeightLocked can't tell them apart later)
            if (h.Equals(MOUNTAIN_HEIGHT) && mOurMountain != null) mOurMountain[t.ID] = true;
        }
        private bool[] mOurMountain;
        private bool[] mGorge;

        private void LockCaldera()
        {
            int W = MapWidth, H = MapHeight;
            double cx = (W - 1) / 2.0;
            mOurMountain = new bool[W * H];
            mGorge = new bool[W * H];

            // The SEA is on one edge (south or north, per gen); the mountains on
            // the opposite edge; the bay drains from the sea edge inland toward the
            // range. `d` = rows from the sea edge, so the same logic works flipped.
            mSeaSouth = random.Next(2) == 0;
            // ---- dimension-adaptive knobs: everything below derives from the
            // FINAL W/H (any size, any aspect ratio) -------------------------
            int seaBase = Math.Max(5, Math.Min(8, H / 7));   // ultrawide 5 … tall 8
            int seaBand = seaBase + random.Next(3);          // wobbles per game (always ≥5)
            int rangeMax = Math.Max(4, Math.Min(7, H / 6));  // shallower range on squat maps
            int rangeMin = Math.Max(2, rangeMax - 4);
            mRangeMax = rangeMax;
            mBandLo = H - (rangeMax + 6);                    // piedmont band floor
            mShelfD = H - (H >= 36 ? 8 : 6);                 // no-site shelf by the range
            mPerSide = Math.Max(5, Math.Min(13, W * H / 320));  // site target per side
            // exactly ONE spur per side, framing the central corridor: the bay,
            // the island, the highland prize and both forward sites all sit
            // BETWEEN the mirrored spurs.
            int nSpurs = 1;
            double[] spurOff = new double[nSpurs];
            // 0.35–0.40 of W from the map EDGE (= 0.10–0.15 from centre): the
            // spurs hug the central corridor, the capitals sit in the wide
            // outer plains, and the 4 central-tribe cities are framed between
            // the mirrored pair.
            spurOff[0] = 0.10 + 0.05 * (random.Next(100) / 100.0);
            mSpurOffFrac = spurOff[0];

            // WOBBLED COASTLINE: the locked sea band's depth varies per column (a
            // smooth, mirror-symmetric random walk from the centre outward), so the
            // shore meanders with coves and headlands instead of a ruler line.
            int half = W / 2 + 2;
            int[] depthHalf = new int[half + 1];
            double wob = 0;
            for (int k = 0; k <= half; k++)
            {
                wob += (random.Next(3) - 1) * 0.9;
                wob = Math.Max(-2.0, Math.Min(3.0, wob));
                depthHalf[k] = Math.Max(6, seaBand + (int)Math.Round(wob));
            }

            // WOBBLED RANGE: the locked mountain wall's depth also varies per
            // column (3–7 rows, the same mirror-symmetric walk), so the range
            // bulges into lobes and pulls back into foothill valleys instead
            // of a ruler line. Deep enough that the siteless shelf in front of
            // it (sites stop at d=H-9) reads as mountains, not blank plain;
            // the engine grows organic hills/peaks in the dips.
            int[] rangeHalf = new int[half + 1];
            double rwob = 0;
            for (int k = 0; k <= half; k++)
            {
                rwob += (random.Next(3) - 1) * 0.9;
                rwob = Math.Max(-1.5, Math.Min(3.5, rwob));
                rangeHalf[k] = Math.Max(rangeMin, Math.Min(rangeMax,
                    (rangeMin + 1) + (int)Math.Round(rwob)));
            }

            // VOLCANIC HALF-ISLAND pressed against the sea edge: the cone juts
            // out of the ocean at the map border (clipped by it — not a complete
            // island), still sea-locked from the mainland by a guaranteed
            // water ring on its inland side.
            mIslandR = 3.1 + random.Next(2) / 10.0;          // 3.1–3.2 (×2.5 across; ≥3.1 keeps
                                                             // the mirrored edge row on land)
            int islandD = 3;                                 // taller: the site row clears the edge

            // WANDERING spur seed-lines: per-row drift so the locked seeds aren't
            // dead-straight walls (the engine grows ridges around them). Drift is
            // per ROW, applied to |xs| — so it stays mirror-symmetric.
            double[][] spurAt = new double[nSpurs][];
            for (int i = 0; i < nSpurs; i++)
            {
                spurAt[i] = new double[H];
                double drift = 0;
                for (int y = 0; y < H; y++)
                {
                    drift += (random.Next(3) - 1) * 0.5;             // wander ±½ tile per row
                    drift = Math.Max(-2.5, Math.Min(2.5, drift));    // stay near the axis
                    // never dip inside the gorge mouth (xs<0.08W is the locked
                    // pass through the range) — the wall must top out against
                    // the range wall, not open a back door around the gorge
                    spurAt[i][y] = Math.Max(W * 0.08 + 1.8, W * spurOff[i] + drift);
                }
            }
            mSpurAt = spurAt[0];   // per-row wall position, for the corridor site rule

            for (int x = 0; x < W; x++)
            {
                double xs = Math.Abs(x - cx);
                for (int y = 0; y < H; y++)
                {
                    TileData t = GetTile(x, y);
                    int d = DSea(x, y);                        // rows from THIS half's sea edge
                    double inf = d / (double)(H - 1);          // 0 sea edge → 1 mountain edge

                    // GUARANTEED SEA band on the sea edge, ≥6 deep everywhere (a
                    // ship line can't wall it off) and WOBBLED per column so the
                    // coastline meanders.
                    int k = (int)Math.Round(Math.Abs(x - cx));
                    if (d < depthHalf[Math.Min(k, half)]) { LockWater(t); continue; }

                    // central BAY — a drowned channel cutting ~55% inland from the
                    // sea, narrowing, draining into the sea band.
                    double bayHalf = W * 0.085 * (1.0 - 0.7 * Math.Min(1.0, inf / 0.55));
                    if (inf < 0.55 && xs < bayHalf) { LockWater(t); continue; }

                    // the far (mountain) EDGE: the RANGE proper (mountains, with the
                    // central GORGE pass), plus a TERRAIN-ONLY locked buffer in
                    // front of it. The buffer guarantees land (no second sea behind
                    // the range) but leaves HEIGHT free — locking it flat used to
                    // force the engine's elevation into a trough along the lock
                    // boundary, which FillLakes then lined with a lake chain.
                    if (d >= H - rangeHalf[Math.Min(k, half)])
                    {
                        if (xs > W * 0.08) LockLand(t, MOUNTAIN_HEIGHT, TEMPERATE_TERRAIN);
                        else
                        {
                            LockLand(t, FLAT_HEIGHT, TEMPERATE_TERRAIN);    // the gorge pass
                            if (mGorge != null) mGorge[t.ID] = true;        // exempt from pocket fill
                        }
                        continue;
                    }
                    // flanking SPURS — wandering but CONTINUOUS mountain seed-lines:
                    // a spur is a hard barrier (the lanes go AROUND its tip, never
                    // through it). Checked BEFORE the buffer band so the wall runs
                    // unbroken all the way INTO the range (a gap here was exactly
                    // the "pass at the top of the spur" bug — TamePiedmont demotes
                    // unlocked mountains in that band, so only a locked seed
                    // survives there).
                    bool onSpur = false;
                    int ySpur = (PointMode && !WestHalf(x)) ? (H - 1 - y) : y;   // east frame is rotated
                    for (int i = 0; i < nSpurs && !onSpur; i++)
                        if (Math.Abs(xs - spurAt[i][ySpur]) < 1.1 && inf > 0.42) onSpur = true;   // ~2-3 wide: solid, not fat
                    if (onSpur) { LockLand(t, MOUNTAIN_HEIGHT, TEMPERATE_TERRAIN); continue; }

                    if (d >= H - 7)
                    {
                        LockTileTerrain(t, TEMPERATE_TERRAIN, true);        // land, but natural height
                        t.Terrain = TEMPERATE_TERRAIN;
                        continue;
                    }
                }
            }

            // The half-island: a WIDE cone (elliptical, 1.5× across) of lush
            // flats with a hill core, clipped by the map edge, then a free-
            // height water ring (its own coast forms there) and a 3-tile
            // height-locked OCEAN moat — far enough from any mainland coast
            // that city borders can never tile-buy a bridge to it.
            if (PointMode)
            {
                // the caldera proper: one self-rotational island astride the
                // exact map centre, mid-strait — contested from both ends
                mIslandCX = (W - 1) / 2.0;
                mIslandCY = (H - 1) / 2.0;
                mIslandX = W / 2; mIslandY = H / 2;
            }
            else
            {
                mIslandX = W / 2;
                mIslandY = mSeaSouth ? islandD : (H - 1 - islandD);
                mIslandCX = mIslandX; mIslandCY = mIslandY;
            }
            int reachY = (int)Math.Ceiling(mIslandR) + 8;
            int reachX = (int)Math.Ceiling(1.5 * (mIslandR + 6.5)) + 1;
            for (int dy = -reachY - 1; dy <= reachY; dy++)
                for (int dx = -reachX - 1; dx <= reachX; dx++)
                {
                    int x = mIslandX + dx, y = mIslandY + dy;
                    if (x < 0 || x >= W || y < 0 || y >= H) continue;
                    double e = IslandE(x, y);
                    // the moat's outer shell is rounder than the island (the
                    // coast pass turns ANY land-adjacent water to coast, locks
                    // or not — only raw distance to land keeps the gap deep)
                    double ddx = x - mIslandCX, ddy = y - mIslandCY;
                    double eOut = Math.Sqrt(Math.Pow(ddx / 1.5, 2) + ddy * ddy);
                    TileData t = GetTile(x, y);
                    if (e <= 1.4)
                        LockLand(t, HILL_HEIGHT, TEMPERATE_TERRAIN);   // volcanic slopes (gold-valid)
                    else if (e <= mIslandR)
                        LockLand(t, FLAT_HEIGHT, LUSH_TERRAIN);        // fertile apron
                    else if (e <= mIslandR + 1.5)
                        LockWater(t);                                  // the island's own coast ring
                    else if (eOut <= mIslandR + 6.5)
                        LockOcean(t);                                  // the deep moat (no tile-buy)
                }
        }
        private bool mSeaSouth;
        // ---- symmetry frame -------------------------------------------------
        // CENTERLINE (or no mirror): the classic estuary — sea on one edge,
        // range opposite, everything left-right symmetric about the seam.
        // CENTERPOINT: the S-STRAIT variant — the same per-tile formulas, but
        // the frame FLIPS across the seam (west keeps sea-south, the east half
        // is the 180-degree rotation): the two bay channels overlap mid-map
        // into one strait from sea to sea, the spurs become its flanking
        // walls, each range ends at a gorge by the strait mouth, and a single
        // self-rotational island sits astride the exact centre.
        private bool PointMode { get { return mirrorType == MirrorMapType.CENTERPOINT; } }
        private bool WestHalf(int x) { return x < MapWidth / 2; }
        // rows from THIS tile's own sea edge
        private int DSea(int x, int y)
        {
            bool southSea = PointMode ? (WestHalf(x) == mSeaSouth) : mSeaSouth;
            return southSea ? y : (MapHeight - 1 - y);
        }
        // the engine's pairing: stagger mirror (centerline) or 180-degree
        // rotation (centerpoint — (W-1-x, H-1-y); with even H the row-parity
        // flip absorbs the hex stagger, verified against the engine's
        // centerpointSymmetricTileID)
        private void MirrorOf(int x, int y, out int mx, out int my)
        {
            if (PointMode) { mx = MapWidth - 1 - x; my = MapHeight - 1 - y; }
            else { mx = (y % 2 == 0) ? (MapWidth - 1 - x) : (MapWidth - x); my = y; }
        }
        private double mSpurOffFrac;
        private double[] mSpurAt;
        private int mRangeMax = 7, mBandLo, mShelfD, mPerSide = 6;
        private int mIslandX, mIslandY;
        private double mIslandCX, mIslandCY;
        private double mIslandR;

        // ================= gameplay (sites, tribes, prizes) =================

        private TerrainType T(string z) { return infos.getType<TerrainType>(z); }
        private ResourceType R(string z) { return infos.getType<ResourceType>(z); }
        private const double ISLAND_SX = 2.0;       // horizontal stretch (a wide cone)
        private double IslandE(int x, int y)        // elliptical "radius" from the core
        {
            double dx = (x - mIslandCX) / ISLAND_SX, dy = y - mIslandCY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        private bool OnIsland(int x, int y)
        {
            return IslandE(x, y) <= mIslandR + 1.5; // the island + its coast ring
        }
        private static void Neigh(int y, out int[] dx, out int[] dy)
        {
            if ((y & 1) == 1) { dx = new[] { -1, 0, 1, 0, -1, -1 }; dy = new[] { 1, 1, 0, -1, -1, 0 }; }
            else { dx = new[] { 0, 1, 1, 1, 0, -1 }; dy = new[] { 1, 1, 0, -1, -1, 0 }; }
        }

        // GUARANTEE both contested prize sites (island + highland) in AddCities —
        // BEFORE the engine's resource pass — so they're treated as real sites
        // and can be promoted to RICH in GetCitySiteResourceLevels (the engine
        // then rolls extra resources around them organically, validity- and
        // density-respecting — nothing hardcoded).
        protected override bool AddCities()
        {
            bool ok = base.AddCities();
            EnsureIslandCitySite();
            EnsureCentreCitySite();
            return ok;
        }
        private int mIslandSiteId = -1, mCentreSiteId = -1;
        private int mIslandSiteId2 = -1, mCentreSiteId2 = -1;

        // The organic prize richness: the engine sorts city sites into rich /
        // moderate / poor (per the player's resource-density option) and rolls
        // resources accordingly. The two contested centres are always RICH.
        protected override void GetCitySiteResourceLevels(
            List<TileData> citySites, List<TileData> richSites,
            List<TileData> moderateSites, List<TileData> poorSites)
        {
            base.GetCitySiteResourceLevels(citySites, richSites, moderateSites, poorSites);
            foreach (TileData s in citySites)
            {
                if (s.ID != mIslandSiteId && s.ID != mCentreSiteId
                    && s.ID != mIslandSiteId2 && s.ID != mCentreSiteId2) continue;
                moderateSites.Remove(s);
                poorSites.Remove(s);
                if (!richSites.Contains(s)) richSites.Add(s);
            }
        }

        private void EnsureIslandCitySite()
        {
            int W = MapWidth, H = MapHeight, cx = W / 2;
            CitySiteType none = GetTile(0, 0).CitySite, sample = FirstSiteType(none);
            if (sample.Equals(none)) return;
            // the site must CLEAR the map edge (its city ring needs room) —
            // never adopt or place within 2 rows of the sea border
            System.Func<int, bool> clears = (yy) =>
            {
                return Math.Min(yy, H - 1 - yy) >= 2 || DSea(cx, yy) >= 2;
            };
            if (PointMode)
            {
                // canonical site west of the seam; its 180-degree partner is
                // created by the site mirror (two prize sites >=8 apart on the
                // one central island — contested from both ends of the strait)
                for (int rad = 0; rad < 4; rad++)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        int x = cx - 5, y = H / 2 + rad * s2;
                        if (y < 2 || y >= H - 2) continue;
                        if (IslandE(x, y) > mIslandR) continue;
                        TileData t0 = GetTile(x, y);
                        t0.Terrain = LUSH_TERRAIN; t0.Height = FLAT_HEIGHT;
                        t0.CitySite = sample; mIslandSiteId = t0.ID; return;
                    }
                return;
            }
            for (int yy = 0; yy < H; yy++)
                for (int xx = 0; xx < W; xx++)
                    if (clears(yy) && OnIsland(xx, yy) && !GetTile(xx, yy).CitySite.Equals(none))
                    { mIslandSiteId = yy * W + xx; return; }            // already there
            // a self-mirror island tile (odd row, x=cx) that's foundable land…
            for (int rad = 0; rad < 6; rad++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int y = mIslandY + rad * s;
                    if (y < 0 || y >= H || (y % 2) == 0 || !clears(y)) continue;
                    if (!OnIsland(cx, y)) continue;
                    TileData t = GetTile(cx, y);
                    if (t.Terrain.Equals(WATER_TERRAIN) || t.Height.Equals(VOLCANO_HEIGHT) || t.Height.Equals(MOUNTAIN_HEIGHT)) continue;
                    t.CitySite = sample; mIslandSiteId = t.ID; return;
                }
            // …else FORCE the nearest central island tile to a foundable flat so
            // the prize always exists (never a west-half fallback that doubles).
            for (int rad = 0; rad < 6; rad++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int y = mIslandY + rad * s;
                    if (y < 0 || y >= H || (y % 2) == 0 || !clears(y) || !OnIsland(cx, y)) continue;
                    TileData t = GetTile(cx, y);
                    t.Terrain = LUSH_TERRAIN; t.Height = FLAT_HEIGHT; t.CitySite = sample;
                    mIslandSiteId = t.ID; return;
                }
        }

        // The LAST Build stage (the engine's order is …AddCities → AddResources →
        // AddMiddleRowCitiesToMirrorMap → PlaceTribes → AddBonusImprovements →
        // AddMapElementNames) — so every pass here sees the FINAL engine output.
        // Running this from CalculateClosestCitySites (inside AddCities) was a
        // bug: AddResources then rolled right over everything we placed.
        protected override void AddMapElementNames()
        {
            RepairLakes();         // no lake chains along the range, no tarns walled in rock
            TameVolcanoes();       // the engine scatters volcanoes through the ranges
            FlattenCoast();        // CoastalRainBasin walls the SEA edge with mountains
            SealPockets();         // late safety: small strips sealed by the mirror
            MirrorGameplay();      // enforce exact L–R symmetry of the gameplay layer
            RepairLakes();         // again: the mirror can copy a river-fed west lake
                                   // onto an east tile with no river (riverless pond)
            SealPockets();         // and it can wall in slivers on the east half
            SetIslandCaldera();    // the ONE volcano — after the mirror (a self-mirror tile)
            FinalizeSites();       // authoritative 14–18 sites at distance 8
            AssignTribes();        // tribes live ON final city sites (else: barbs in-game)
            CleanGhostUrban();     // trimmed sites must not leave ghost urban rings
            RichenPrizes();        // floor on prize richness if the engine rolled stingy
            base.AddMapElementNames();   // names go on the FINAL terrain
        }

        // The rich-site promotion (GetCitySiteResourceLevels) usually delivers,
        // but the engine's rolls can come up stingy on the island's few tiles.
        // Guarantee a FLOOR organically: count resources near each prize and, if
        // short, add RANDOMLY-CHOSEN resources that are VALID for each tile's
        // terrain/height (no scripted resource list) — mirrored for fairness.
        private void RichenPrizes()
        {
            RichenAround(mIslandSiteId, 4, 3);
            RichenAround(mCentreSiteId, 4, 4);   // wider: tundra highlands hold nothing
            // (point mode: the rotated partners receive the mirrored copies)
        }
        private void RichenAround(int siteId, int want, int radius)
        {
            if (siteId < 0) return;
            int W = MapWidth, H = MapHeight;
            int sx = siteId % W, sy = siteId / W;
            VegetationType noVeg = GetTile(0, 0).Vegetation;             // open sea: never has any

            int have = 0;
            var cands = new List<int[]>();
            for (int y = Math.Max(0, sy - radius); y <= Math.Min(H - 1, sy + radius); y++)
                for (int x = Math.Max(0, sx - radius); x <= Math.Min(W - 1, sx + radius); x++)
                {
                    if (TileDist(x, y, sx, sy) > radius) continue;
                    TileData t = GetTile(x, y);
                    if ((int)t.Resource >= 0) { have++; continue; }      // has a resource
                    if (x > W / 2) continue;                 // canonical side; the mirror doubles
                    if (t.Terrain.Equals(URBAN_TERRAIN)) continue;
                    if (!t.CitySite.Equals(GetTile(0, 0).CitySite)) continue;
                    cands.Add(new[] { x, y });
                }
            // random order (Fisher–Yates with the seeded RNG)
            for (int i = cands.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); var tmp = cands[i]; cands[i] = cands[j]; cands[j] = tmp; }

            for (int pass = 0; pass < 2; pass++)
            foreach (var c in cands)
            {
                if (have >= want) break;
                TileData t = GetTile(c[0], c[1]);
                if ((int)t.Resource >= 0) continue;
                // pass 1 (fallback): cold/waste highlands support NO resources at
                // all (tundra) — warm a couple of tiles near the prize into a
                // sheltered temperate pocket so the prize isn't barren.
                if (pass == 1 && !t.Terrain.Equals(WATER_TERRAIN)
                    && ValidResourceFor(t) == null
                    && (t.Height.Equals(FLAT_HEIGHT) || t.Height.Equals(HILL_HEIGHT))
                    && TileDist(c[0], c[1], sx, sy) <= 2)
                {
                    t.Terrain = TEMPERATE_TERRAIN;
                    int mw, mwy; MirrorOf(c[0], c[1], out mw, out mwy);
                    if (mw >= 0 && mw < W && mwy >= 0 && mwy < MapHeight && (mw != c[0] || mwy != c[1]))
                        GetTile(mwy * W + mw).Terrain = TEMPERATE_TERRAIN;
                }
                string pick = ValidResourceFor(t);
                if (pick == null) continue;
                t.Resource = R(pick);
                t.Vegetation = noVeg;                        // all picks are veg-free-valid
                have++;
                // Mirror with the ENGINE's own pairing (GetMirrorTileOfThisTile),
                // so the engine's later mirror pass (MirrorPlayerStarts in mirror
                // games) preserves rather than wipes the pair. It returns -1 for
                // water — water resources (pearls etc.) aren't mirror-copied by
                // the engine, so they're safe as placed; pair them ourselves.
                int mid = GetMirrorTileOfThisTile(t.ID);
                if (mid >= 0 && mid != t.ID)
                {
                    TileData m = GetTile(mid);                // terrain is mirror-identical
                    m.Resource = R(pick);
                    m.Vegetation = noVeg;
                    if (TileDist(mid % W, mid / W, sx, sy) <= radius) have++;
                }
                else if (t.Terrain.Equals(WATER_TERRAIN))
                {
                    int mxx, mxy; MirrorOf(c[0], c[1], out mxx, out mxy);
                    if (mxx >= 0 && mxx < W && mxy >= 0 && mxy < MapHeight
                        && (mxx != c[0] || mxy != c[1])
                        && GetTile(mxy * W + mxx).Terrain.Equals(WATER_TERRAIN))
                    {
                        GetTile(mxy * W + mxx).Resource = R(pick);
                        if (TileDist(mxx, mxy, sx, sy) <= radius) have++;
                    }
                }
            }
        }
        // A random resource VALID for this tile, per resource.xml's actual
        // abTerrainValid/abHeightValid tables (only resources valid WITHOUT
        // vegetation, since we clear it). Tundra/marsh/sand support none.
        private string ValidResourceFor(TileData t)
        {
            var opts = new List<string>();
            bool lush = t.Terrain.Equals(LUSH_TERRAIN);
            bool temp = t.Terrain.Equals(TEMPERATE_TERRAIN);
            bool arid = (int)t.Terrain == (int)T("TERRAIN_ARID");
            if (t.Terrain.Equals(WATER_TERRAIN))
            {
                if (t.Height.Equals(COAST_HEIGHT))
                { opts.Add("RESOURCE_FISH"); opts.Add("RESOURCE_CRAB"); opts.Add("RESOURCE_PEARL"); opts.Add("RESOURCE_DYE"); }
            }
            else if (t.Height.Equals(FLAT_HEIGHT))
            {
                if (lush) { opts.Add("RESOURCE_BARLEY"); opts.Add("RESOURCE_CATTLE"); opts.Add("RESOURCE_MARBLE"); opts.Add("RESOURCE_HORSE"); opts.Add("RESOURCE_HONEY"); opts.Add("RESOURCE_SPICES"); opts.Add("RESOURCE_SILK"); }
                else if (temp) { opts.Add("RESOURCE_WHEAT"); opts.Add("RESOURCE_MARBLE"); opts.Add("RESOURCE_SALT"); opts.Add("RESOURCE_HORSE"); opts.Add("RESOURCE_LAVENDER"); opts.Add("RESOURCE_CITRUS"); }
                else if (arid) { opts.Add("RESOURCE_JADE"); opts.Add("RESOURCE_MARBLE"); opts.Add("RESOURCE_SALT"); opts.Add("RESOURCE_INCENSE"); }
            }
            else if (t.Height.Equals(HILL_HEIGHT))
            {
                if (lush) { opts.Add("RESOURCE_ORE"); opts.Add("RESOURCE_HONEY"); opts.Add("RESOURCE_CITRUS"); }
                else if (temp) { opts.Add("RESOURCE_ORE"); opts.Add("RESOURCE_GOLD"); opts.Add("RESOURCE_SILVER"); opts.Add("RESOURCE_GEM"); opts.Add("RESOURCE_WINE"); opts.Add("RESOURCE_CITRUS"); }
                else if (arid) { opts.Add("RESOURCE_ORE"); opts.Add("RESOURCE_GOLD"); opts.Add("RESOURCE_SILVER"); opts.Add("RESOURCE_GEM"); opts.Add("RESOURCE_JADE"); opts.Add("RESOURCE_INCENSE"); }
            }
            if (opts.Count == 0) return null;
            return opts[random.Next(opts.Count)];
        }

        // The island's single caldera peak, on a self-mirror tile (odd row, x=W/2)
        // so MirrorGameplay can't clobber it and it stays exactly central.
        private void SetIslandCaldera()
        {
            int W = MapWidth, H = MapHeight, c = W / 2;
            if (PointMode)
            {
                // no tile is self-symmetric under the 180-degree rotation, so
                // the caldera is a rotational PAIR of cones mid-island
                for (int rad = 0; rad < 5; rad++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        int x = c - 2, y = H / 2 + rad * s;
                        if (y < 1 || y >= H - 1 || IslandE(x, y) > mIslandR) continue;
                        TileData t = GetTile(x, y);
                        if (t.Terrain.Equals(WATER_TERRAIN) || t.Terrain.Equals(URBAN_TERRAIN)) continue;
                        if (!t.CitySite.Equals(GetTile(0, 0).CitySite)) continue;
                        int mx, my; MirrorOf(x, y, out mx, out my);
                        TileData m = GetTile(my * W + mx);
                        if (m.Terrain.Equals(WATER_TERRAIN) || m.Terrain.Equals(URBAN_TERRAIN)) continue;
                        if (!m.CitySite.Equals(GetTile(0, 0).CitySite)) continue;
                        t.Height = VOLCANO_HEIGHT; t.Terrain = T("TERRAIN_ARID");
                        m.Height = VOLCANO_HEIGHT; m.Terrain = T("TERRAIN_ARID");
                        return;
                    }
                return;
            }
            for (int rad = 0; rad < 5; rad++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int y = mIslandY + rad * s;
                    if (y < 0 || y >= H || (y % 2) == 0 || !OnIsland(c, y)) continue;
                    TileData t = GetTile(c, y);
                    if (t.Terrain.Equals(WATER_TERRAIN)) continue;
                    if (!t.CitySite.Equals(GetTile(0, 0).CitySite)) continue;   // never ON the prize site
                    if (t.Terrain.Equals(URBAN_TERRAIN)) continue;
                    t.Height = VOLCANO_HEIGHT; t.Terrain = T("TERRAIN_ARID"); return;
                }
        }

        // The engine's AddVolcanicMountains peppers the whole range with volcanoes;
        // a temperate Caldera Bay wants just the one caldera (on the island, set in
        // PlaceCenterPrizes). Convert all the rest back to ordinary mountains.
        // Lakes the engine pools in the wrong places read as "random water in the
        // mountains". Two repairs, both RIVER-SAFE:
        //  • a lake deep in the range zone with NO river feeding it → back to land
        //    (it's an elevation-pit artifact, not a real water feature);
        //  • any surviving lake flanked by mountains → soften those mountains to
        //    hills, so it reads as a valley tarn instead of water walled in rock.
        private void RepairLakes()
        {
            int W = MapWidth, H = MapHeight;

            // PROTECTED lakes: for every river system whose only water contact is
            // a lake, that lake must survive — culling it orphans the river.
            bool[] protectedLake = new bool[W * H];
            bool[] visited = new bool[W * H];
            for (int i0 = 0; i0 < W * H; i0++)
            {
                if (visited[i0] || !IsRiver(GetTile(i0))) continue;
                var comp = new List<int>();
                var st = new Stack<int>(); st.Push(i0);
                while (st.Count > 0)
                {
                    int j = st.Pop();
                    if (j < 0 || j >= W * H || visited[j] || !IsRiver(GetTile(j))) continue;
                    visited[j] = true; comp.Add(j);
                    int jx = j % W, jy = j / W; int[] ddx, ddy; Neigh(jy, out ddx, out ddy);
                    for (int k = 0; k < 6; k++)
                    {
                        int nx = jx + ddx[k], ny = jy + ddy[k];
                        if (nx >= 0 && nx < W && ny >= 0 && ny < H) st.Push(ny * W + nx);
                    }
                }
                bool seaContact = false;
                var lakeContacts = new List<int>();
                foreach (int j in comp)
                {
                    int jx = j % W, jy = j / W; int[] ddx, ddy; Neigh(jy, out ddx, out ddy);
                    for (int k = 0; k < 6; k++)
                    {
                        int nx = jx + ddx[k], ny = jy + ddy[k];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        TileData nb = GetTile(nx, ny);
                        if (nb.Height.Equals(LAKE_HEIGHT)) lakeContacts.Add(ny * W + nx);
                        else if (nb.Terrain.Equals(WATER_TERRAIN)) seaContact = true;
                    }
                }
                if (!seaContact) foreach (int li in lakeContacts) protectedLake[li] = true;
            }

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    TileData t = GetTile(x, y);
                    if (!t.Height.Equals(LAKE_HEIGHT)) continue;
                    int d = mSeaSouth ? y : (H - 1 - y);              // rows from the sea edge
                    bool inRangeZone = d > 0.72 * (H - 1);
                    int[] dx, dy; Neigh(y, out dx, out dy);
                    bool riverFed = IsRiver(t) || protectedLake[y * W + x];
                    // Cull artifact ponds: not fed by any river AND (deep in the
                    // range zone OR cut off by the map border). River-fed tiles
                    // always survive — culling them would orphan the river; this
                    // naturally SHRINKS border tarns to their river-touching tiles.
                    bool atEdge = x < 3 || x > W - 4;
                    if ((inRangeZone || atEdge) && !riverFed)
                    {
                        t.Height = FLAT_HEIGHT;                       // artifact pond → land
                        t.Terrain = TEMPERATE_TERRAIN;
                        continue;
                    }
                    for (int k = 0; k < 6; k++)                        // open up walled-in tarns
                    {
                        int nx = x + dx[k], ny = y + dy[k];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        TileData n = GetTile(nx, ny);
                        // never soften OUR spur/range walls — that would melt a
                        // pass through a barrier that must stay solid
                        if (n.Height.Equals(MOUNTAIN_HEIGHT)
                            && (mOurMountain == null || !mOurMountain[n.ID]))
                            n.Height = HILL_HEIGHT;
                    }
                }
        }


        // The engine sometimes grows a SECOND mountain wall a few rows in front
        // of our locked range. The shelf between the walls is large (100+ tiles)
        // and gathers city sites, so it can't be filled with mountains — it has
        // to be OPENED: demote the engine's unlocked mountains in the piedmont
        // band to hills (our own locked walls are untouched). Removing this pass
        // was tried and immediately failed the pocket sweep.
        private void TamePiedmont()
        {
            int W = MapWidth, H = MapHeight;
            int lo = mBandLo > 0 ? mBandLo : H - 13;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int d = DSea(x, y);                       // rows from THIS half's sea edge
                    if (d < lo || d >= H - 3) continue;       // the band below the range
                    TileData t = GetTile(x, y);
                    if (t.Height.Equals(MOUNTAIN_HEIGHT)
                        && (mOurMountain == null || !mOurMountain[t.ID]))
                        t.Height = HILL_HEIGHT;
                }
        }

        // The engine can also grow a giant ORGANIC massif out in the open plain
        // (often budding off a locked spur seed) — a 30-40 tile wall that boxes
        // a capital into a corner pocket. Keep plain mountains as small scenic
        // clumps only: any unlocked mountain cluster bigger than a few tiles in
        // the plain zone (below the piedmont band) is demoted to hills. Our
        // locked walls (range, spurs) are never touched.
        private void TameMassifs()
        {
            int W = MapWidth, H = MapHeight;
            bool InPlain(int idx)
            {
                int d = DSea(idx % W, idx / W);
                return d < (mBandLo > 0 ? mBandLo : H - 13);
            }
            bool Organic(TileData t) => t.Height.Equals(MOUNTAIN_HEIGHT)
                && (mOurMountain == null || !mOurMountain[t.ID]);
            var seen = new bool[W * H];
            var comp = new List<TileData>();
            for (int i = 0; i < W * H; i++)
            {
                TileData t0 = GetTile(i);
                if (seen[i] || !InPlain(i) || !Organic(t0)) continue;
                comp.Clear();
                var stack = new Stack<int>();
                stack.Push(i); seen[i] = true;
                while (stack.Count > 0)
                {
                    int j = stack.Pop();
                    comp.Add(GetTile(j));
                    int jx = j % W, jy = j / W; int[] ddx, ddy; Neigh(jy, out ddx, out ddy);
                    for (int k = 0; k < 6; k++)
                    {
                        int nx = jx + ddx[k], ny = jy + ddy[k];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        int nid = ny * W + nx;
                        if (seen[nid] || !InPlain(nid) || !Organic(GetTile(nid))) continue;
                        seen[nid] = true; stack.Push(nid);
                    }
                }
                if (comp.Count > 6)
                    foreach (TileData t in comp) t.Height = HILL_HEIGHT;
            }
        }

        private void TameVolcanoes()
        {
            for (int i = 0; i < MapWidth * MapHeight; i++)
            {
                TileData t = GetTile(i);
                if (t.Height.Equals(VOLCANO_HEIGHT)) t.Height = MOUNTAIN_HEIGHT;
            }
        }

        // CoastalRainBasin grows a mountain range right along the SEA edge (its
        // rain-shadow source). Caldera Bay keeps its mountains INLAND (the range
        // on the far edge + the spurs), so the coast reads as a low coastal plain:
        // demote mountains in the seaward third to hills. The climate/rain-shadow
        // is already assigned by now, so the arid belts survive.
        private void FlattenCoast()
        {
            int W = MapWidth, H = MapHeight;
            double thresh = 0.30 * (H - 1);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    TileData t = GetTile(x, y);
                    if (!t.Height.Equals(MOUNTAIN_HEIGHT)) continue;
                    if (mOurMountain != null && mOurMountain[t.ID]) continue;   // never melt OUR walls
                    int d = mSeaSouth ? y : (H - 1 - y);     // rows from the sea edge
                    bool coastal = d < thresh;               // the seaward third…
                    if (!coastal)                            // …and any shoreline (incl. bay shores)
                    {
                        int[] dx, dy; Neigh(y, out dx, out dy);
                        for (int k = 0; k < 6; k++)
                        {
                            int nx = x + dx[k], ny = y + dy[k];
                            if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                            if (GetTile(nx, ny).Terrain.Equals(WATER_TERRAIN)) { coastal = true; break; }
                        }
                    }
                    if (coastal)                              // a coast is low — and broken up
                        t.Height = ((x * 31 + y * 17) % 3 == 0) ? FLAT_HEIGHT : HILL_HEIGHT;
                }
        }

        // ---- city-site management (14–18 total, ≥8 apart, 2 central prizes) ----
        private const double SITE_SPACE = 8.0;

        private CitySiteType FirstSiteType(CitySiteType none)
        {
            for (int i = 0; i < MapWidth * MapHeight; i++)
            { CitySiteType cs = GetTile(i).CitySite; if (!cs.Equals(none)) return cs; }
            return none;
        }
        private bool Foundable(int x, int y, CitySiteType none, bool allowCorridor = false)
        {
            if (x < 3 || x >= MapWidth - 3 || y < 2 || y >= MapHeight - 2) return false;
            int d = DSea(x, y);                              // rows from the sea edge
            if (d >= (mShelfD > 0 ? mShelfD : MapHeight - 8)) return false;  // never wedged against the range
            if (mReach != null && mReach[y * MapWidth + x] != mMainComp) return false;  // land-reachable from the caps
            if (!allowCorridor && InsideSpurFrame(x, y)) return false;  // corridor = prizes only
            if (!allowCorridor && !OpenGround(x, y)) return false;      // no rock-pocket sites
            TileData t = GetTile(x, y);
            if (!t.CitySite.Equals(none)) return false;
            if (t.Terrain.Equals(WATER_TERRAIN)) return false;
            if (t.Height.Equals(MOUNTAIN_HEIGHT) || t.Height.Equals(VOLCANO_HEIGHT) || t.Height.Equals(LAKE_HEIGHT)) return false;
            return true;
        }
        // a site needs WORKABLE ground around it: heavy rock within hex-4
        // makes a cramped pocket city — and the west-most one becomes the
        // CAPITAL (start placement snaps to existing sites, so this is the
        // only reliable way to keep capitals out of mountain corners).
        private bool OpenGround(int x, int y)
        {
            int mtn = 0, tot = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dxx = -4; dxx <= 4; dxx++)
                {
                    int nx = x + dxx, ny = y + dy;
                    if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight) continue;
                    if (TileDist(x, y, nx, ny) > 4) continue;
                    tot++;
                    TileData n = GetTile(ny * MapWidth + nx);
                    if (n.Height.Equals(MOUNTAIN_HEIGHT) || n.Height.Equals(VOLCANO_HEIGHT)) mtn++;
                }
            return mtn * 25 <= tot * 7;                      // ≤28% mountain within hex-4
        }
        // Engine-native placement steering (the built-in Dota script's
        // pattern): declare invalid spots UP FRONT so the engine's AddCities
        // scatters sites naturally where they can STAY, instead of placing
        // them and our late passes wiping them. The prizes are still seated
        // deliberately (Ensure*CitySite bypasses these hooks).
        protected override bool IsValidCitySite(TileData pCitySite, bool bCheckAdjacent = true)
        {
            int x = TileX(pCitySite), y = TileY(pCitySite);
            if (OnIsland(x, y)) return false;            // the island prize is placed deliberately
            if (InsideSpurFrame(x, y)) return false;     // the corridor holds only the prizes
            int d = DSea(x, y);
            if (d >= (mShelfD > 0 ? mShelfD : MapHeight - 8)) return false;  // never wedged on the range shelf
            if (!OpenGround(x, y)) return false;         // no rock-pocket sites
            return base.IsValidCitySite(pCitySite, bCheckAdjacent);
        }
        protected override bool IsValidPlayerStart(TileData tile, PlayerType player, int minTeamStartDistance)
        {
            int x = TileX(tile), y = TileY(tile);
            if (OnIsland(x, y) || InsideSpurFrame(x, y)) return false;
            // no boxed-in capitals: reject spots with heavy rock around them
            // (a corner pocket behind a massif plays terribly as a start)
            int mtn = 0, tot = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dxx = -4; dxx <= 4; dxx++)
                {
                    int nx = x + dxx, ny = y + dy;
                    if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight) continue;
                    if (TileDist(x, y, nx, ny) > 4) continue;
                    tot++;
                    TileData n = GetTile(ny * MapWidth + nx);
                    if (n.Height.Equals(MOUNTAIN_HEIGHT) || n.Height.Equals(VOLCANO_HEIGHT)) mtn++;
                }
            if (mtn * 4 > tot) return false;             // >25% mountain within hex-4
            // NEVER require an existing city site here: the base check itself
            // calls IsValidCitySite, which REJECTS occupied site tiles — the
            // two conditions are mutually exclusive, so requiring a site made
            // every tile invalid and the whole map failed to generate in-game
            // (owmapgen's start path doesn't consult this hook, hiding it).
            return base.IsValidPlayerStart(tile, player, minTeamStartDistance);
        }
        // the corridor between the mirrored spurs is reserved for the TWO
        // prizes (island + highland) — every other site stays on the player
        // side of its spur.
        private bool InsideSpurFrame(int x, int y)
        {
            if (mSpurAt == null) return false;
            double xs = Math.Abs(x - (MapWidth - 1) / 2.0);
            return xs < mSpurAt[y] + 1.6;
        }
        private static double TileDist(int ax, int ay, int bx, int by)
        {
            int aq = ax - ((ay + (ay & 1)) / 2), ar = ay;
            int bq = bx - ((by + (by & 1)) / 2), br = by;
            return (Math.Abs(aq - bq) + Math.Abs((-aq - ar) - (-bq - br)) + Math.Abs(ar - br)) / 2.0;
        }

        // the highland prize sits just in front of the RANGE (the mountain edge,
        // opposite the sea) on the dead-centre self-mirror axis.
        private void EnsureCentreCitySite()
        {
            int W = MapWidth, H = MapHeight, x = W / 2;
            CitySiteType none = GetTile(0, 0).CitySite, sample = FirstSiteType(none);
            if (sample.Equals(none)) return;
            if (PointMode)
            {
                // the canonical highland prize sits just west of the seam in
                // front of the WEST half's range (by its gorge / the strait
                // mouth); the site mirror seats the partner at the far end
                bool southSea = mSeaSouth;               // west half's sea side
                int dirP = southSea ? -1 : 1;
                int startP = southSea ? (H - 5) : 4;
                for (int pass = 0; pass < 2; pass++)
                    for (int k = 0; k < H; k++)
                    {
                        int y = startP + dirP * k;
                        if (y < 0 || y >= H) break;
                        for (int xx = x - 1; xx >= x - 4; xx--)
                        {
                            if (OnIsland(xx, y) || !Foundable(xx, y, none, true)) continue;
                            if (DSea(xx, y) < 0.55 * (H - 1)) continue;
                            if (pass == 0 && LandNeighbors(xx, y) < 3) continue;
                            GetTile(xx, y).CitySite = sample; mCentreSiteId = y * W + xx; return;
                        }
                    }
                return;
            }
            for (int y = 0; y < H; y++)
            {
                if ((y % 2) == 0) continue;
                if (GetTile(x, y).CitySite.Equals(none) || OnIsland(x, y)) continue;
                int dHere = mSeaSouth ? y : (H - 1 - y);
                if (dHere < 0.55 * (H - 1)) continue;   // a coastal centre site is NOT the highland prize
                mCentreSiteId = y * W + x; return;
            }
            int dir = mSeaSouth ? -1 : 1;            // scan from the range toward the sea
            int startY = mSeaSouth ? (H - 5) : 4;
            // first try to seat it where there's real settleable land around it
            // (so the highland city isn't a lone tile walled in by mountains)…
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < H; k++)
                {
                    int y = startY + dir * k;
                    if (y < 0 || y >= H) break;
                    if ((y % 2) == 0) continue;
                    if (OnIsland(x, y) || !Foundable(x, y, none, true)) continue;
                    if (pass == 0 && LandNeighbors(x, y) < 3) continue;   // not deep in the mountains
                    GetTile(x, y).CitySite = sample; mCentreSiteId = y * W + x; return;
                }
        }
        private int LandNeighbors(int x, int y)
        {
            int W = MapWidth, H = MapHeight, n = 0;
            int[] dx, dy; Neigh(y, out dx, out dy);
            for (int k = 0; k < 6; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                TileData t = GetTile(nx, ny);
                if (t.Terrain.Equals(WATER_TERRAIN)) continue;
                if (t.Height.Equals(MOUNTAIN_HEIGHT) || t.Height.Equals(VOLCANO_HEIGHT)) continue;
                n++;
            }
            return n;
        }

        // Land-reachability map: every site except the island must live in ONE
        // walkable component (the mainland, where the capitals are). Computed on
        // the FINAL terrain at FinalizeSites time and enforced via Foundable.
        private int[] mReach; private int mMainComp = -1;
        private void BuildReachability()
        {
            int W = MapWidth, H = MapHeight, n = W * H;
            mReach = new int[n];
            for (int i = 0; i < n; i++) mReach[i] = -1;
            int best = -1, bestSize = 0, id = 0;
            for (int i = 0; i < n; i++)
            {
                if (mReach[i] >= 0) continue;
                TileData t0 = GetTile(i);
                if (t0.Terrain.Equals(WATER_TERRAIN) || t0.Height.Equals(MOUNTAIN_HEIGHT)
                    || t0.Height.Equals(VOLCANO_HEIGHT)) continue;
                int size = 0;
                var st = new Stack<int>(); st.Push(i);
                while (st.Count > 0)
                {
                    int j = st.Pop();
                    if (j < 0 || j >= n || mReach[j] >= 0) continue;
                    TileData t = GetTile(j);
                    if (t.Terrain.Equals(WATER_TERRAIN) || t.Height.Equals(MOUNTAIN_HEIGHT)
                        || t.Height.Equals(VOLCANO_HEIGHT)) continue;
                    mReach[j] = id;
                    if (j % W < W / 2) size++;   // anchor the main comp WEST
                    int x = j % W, y = j / W; int[] dx, dy; Neigh(y, out dx, out dy);
                    for (int k = 0; k < 6; k++)
                    {
                        int nx = x + dx[k], ny = y + dy[k];
                        if (nx >= 0 && nx < W && ny >= 0 && ny < H) st.Push(ny * W + nx);
                    }
                }
                if (size > bestSize) { bestSize = size; best = id; }   // (largest west-anchored below)
                id++;
            }
            mMainComp = best;
        }

        private void FinalizeSites()
        {
            int W = MapWidth, H = MapHeight, c = W / 2;
            CitySiteType none = GetTile(0, 0).CitySite, sample = FirstSiteType(none);
            if (sample.Equals(none)) return;
            BuildReachability();   // terrain is final here — sites must be on the mainland
            // wipe centre+east sites EXCEPT the two prizes — the engine already
            // rolled rich resources around those exact tiles, so they must stay.
            for (int y = 0; y < H; y++)
                for (int x = c; x < W; x++)
                {
                    int id = y * W + x;
                    if (id == mIslandSiteId || id == mCentreSiteId) continue;
                    if (!GetTile(x, y).CitySite.Equals(none)) GetTile(x, y).CitySite = none;
                }
            EnsureIslandCitySite();
            EnsureCentreCitySite();
            var avoid = new List<int[]>();
            for (int y = 0; y < H; y++)
                for (int x = c; x < W; x++)
                    if (!GetTile(x, y).CitySite.Equals(none)) avoid.Add(new[] { x, y });
            BalanceWestSites(avoid);
            mIslandSiteId2 = mCentreSiteId2 = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < c; x++)
                {
                    if (GetTile(x, y).CitySite.Equals(none)) continue;
                    int mxx, myy; MirrorOf(x, y, out mxx, out myy);
                    if (!PointMode && mxx <= c) continue;          // (as before: strictly east)
                    if (mxx < 0 || mxx >= W || myy < 0 || myy >= H) continue;
                    GetTile(mxx, myy).CitySite = sample;
                    int id = y * W + x, mid = myy * W + mxx;
                    if (id == mIslandSiteId) mIslandSiteId2 = mid;     // the prize PAIRS
                    if (id == mCentreSiteId) mCentreSiteId2 = mid;     // (point mode)
                }
        }

        private void BalanceWestSites(List<int[]> avoid)
        {
            int W = MapWidth, H = MapHeight;
            CitySiteType none = GetTile(0, 0).CitySite, sample = FirstSiteType(none);
            if (sample.Equals(none)) return;
            var sites = new List<int[]>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W / 2; x++)
                {
                    if (GetTile(x, y).CitySite.Equals(none)) continue;
                    if (y * W + x == mIslandSiteId || y * W + x == mCentreSiteId)
                    { avoid.Add(new[] { x, y }); continue; }        // prizes: fixed, spacing-relevant
                    int dSea = DSea(x, y);
                    if (x < 3 || x > W / 2 - 3 || y < 2 || y >= H - 2 || OnIsland(x, y)
                        || dSea >= (mShelfD > 0 ? mShelfD : H - 8)   // engine sites on the range shelf
                        || InsideSpurFrame(x, y)             // the corridor is prizes-only
                        || !OpenGround(x, y)                 // boxed into late-grown rock
                        || (mReach != null && mReach[y * W + x] != mMainComp))   // or cut off by land
                    { GetTile(x, y).CitySite = none; continue; }
                    sites.Add(new[] { x, y });
                }
            System.Func<int, double> nearest = (i) =>
            {
                double nd = double.MaxValue;
                int mix = (sites[i][1] % 2 == 0) ? (W - 1 - sites[i][0]) : (W - sites[i][0]);
                for (int j = 0; j < sites.Count; j++)
                    if (i != j)
                    {
                        nd = Math.Min(nd, TileDist(sites[i][0], sites[i][1], sites[j][0], sites[j][1]));
                        int mjx = (sites[j][1] % 2 == 0) ? (W - 1 - sites[j][0]) : (W - sites[j][0]);
                        nd = Math.Min(nd, TileDist(mix, sites[i][1], mjx, sites[j][1]));
                    }
                foreach (var a in avoid)
                {
                    nd = Math.Min(nd, TileDist(sites[i][0], sites[i][1], a[0], a[1]));
                    nd = Math.Min(nd, TileDist(mix, sites[i][1], a[0], a[1]));
                }
                return nd;
            };
            while (sites.Count > 0)
            {
                int worst = -1; double wd = double.MaxValue;
                for (int i = 0; i < sites.Count; i++)
                { double nd = nearest(i); if (nd < wd) { wd = nd; worst = i; } }
                if (sites.Count <= mPerSide + 1 && wd >= SITE_SPACE) break;
                GetTile(sites[worst][0], sites[worst][1]).CitySite = none;
                sites.RemoveAt(worst);
            }

            // PAD up to 6 per side if short — at the FULL engine distance (8) from
            // everything including mirrors and prizes; never cramming below it.
            for (int y = 2; y < H - 2 && sites.Count < mPerSide + 1; y++)
                for (int x = 3; x <= W / 2 - 3 && sites.Count < mPerSide + 1; x++)
                {
                    if (OnIsland(x, y) || !Foundable(x, y, none)) continue;
                    int mx = (y % 2 == 0) ? (W - 1 - x) : (W - x);
                    bool ok = true;
                    foreach (var sxy in sites)
                    {
                        int smx = (sxy[1] % 2 == 0) ? (W - 1 - sxy[0]) : (W - sxy[0]);
                        if (TileDist(x, y, sxy[0], sxy[1]) < SITE_SPACE
                            || TileDist(mx, y, smx, sxy[1]) < SITE_SPACE) { ok = false; break; }
                    }
                    if (ok) foreach (var a in avoid)
                        if (TileDist(x, y, a[0], a[1]) < SITE_SPACE
                            || TileDist(mx, y, a[0], a[1]) < SITE_SPACE) { ok = false; break; }
                    if (!ok) continue;
                    GetTile(x, y).CitySite = sample;
                    sites.Add(new[] { x, y });
                }
        }

        // enforce exact L–R symmetry of terrain/resources (NOT sites/tribes —
        // those are placed symmetric-by-construction separately).
        private void MirrorGameplay()
        {
            int W = MapWidth, H = MapHeight;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int mxx, myy; MirrorOf(x, y, out mxx, out myy);
                    if (PointMode) { if (!WestHalf(x)) continue; }
                    else if (mxx <= x || mxx >= W) continue;
                    if (mxx < 0 || mxx >= W || myy < 0 || myy >= H) continue;
                    TileData src = GetTile(x, y), dst = GetTile(myy * W + mxx);
                    dst.Terrain = src.Terrain; dst.Height = src.Height;
                    dst.Vegetation = src.Vegetation; dst.Resource = src.Resource;
                }
        }

        // ---- tribes: one tribe per side (different, horse-paired), one centre
        // tribe (Huns forced to centre — see AssignTribes). CRITICAL: only
        // tribes the ENGINE actually
        // put in use this game are valid — a site of any other tribe degrades to
        // plain barbarians in-game. So the pool is exactly the distinct tribes
        // base.PlaceTribes() rolled, never a hardcoded list.
        // PlaceTribes here only HARVESTS what the engine rolled (the game's
        // tribes-in-use) and clears the board. The actual assignment happens at
        // the end of Build (AssignTribes), because in Old World tribal
        // settlements live ON CITY SITES — the engine sets TribeSite on site
        // tiles — and our sites aren't final until FinalizeSites has run.
        // (Tribe markers on non-site tiles degrade to plain barbarians in-game,
        // which is exactly the "only barbs" bug.)
        protected override void PlaceTribes()
        {
            base.PlaceTribes();
            int W = MapWidth, H = MapHeight;
            TribeType none = GetTile(0, 0).TribeSite;
            TribeType huns = infos.getType<TribeType>("TRIBE_HUNS");
            int barb = (int)infos.getType<TribeType>("TRIBE_BARBARIANS");
            int raid = (int)infos.getType<TribeType>("TRIBE_RAIDERS");
            int rebl = (int)infos.getType<TribeType>("TRIBE_REBELS");
            mHunsRolled = false;
            mTribePool = new List<TribeType>();
            for (int i = 0; i < W * H; i++)
            {
                TribeType tb = GetTile(i).TribeSite;
                if (tb.Equals(none)) continue;
                if ((int)tb == (int)huns) { mHunsRolled = true; continue; }
                if ((int)tb == barb || (int)tb == raid || (int)tb == rebl) continue;
                bool seen = false;
                foreach (TribeType p in mTribePool) if ((int)p == (int)tb) { seen = true; break; }
                if (!seen) mTribePool.Add(tb);
            }
            if (mTribePool.Count == 0 && mHunsRolled) mTribePool.Add(huns);
            if (mTribePool.Count == 0) return;   // tribes disabled — keep the engine's camps
            for (int i = 0; i < W * H; i++)      // clear; AssignTribes re-places on final sites
                if (!GetTile(i).TribeSite.Equals(none)) GetTile(i).TribeSite = none;
        }
        private List<TribeType> mTribePool;
        private bool mHunsRolled;

        // Assign the rolled tribes to FINAL city sites: one mid-board site per
        // player side (a mirrored pair — different tribes, horse-paired), and
        // the centre tribe holding the contested prizes. The HUNS are the one
        // deliberate special case: the sides get DIFFERENT tribes, and the
        // Huns are far nastier neighbours than the diplomacy tribes — one
        // player living beside Huns while the other courts Gauls is an
        // asymmetric difficulty roll. So when the game rolls them, they take
        // the CENTRE, where both players face them equally.
        private void AssignTribes()
        {
            if (mTribePool == null || mTribePool.Count == 0) return;
            int W = MapWidth, H = MapHeight, c = W / 2;
            TribeType none = GetTile(0, 0).TribeSite;
            TribeType huns = infos.getType<TribeType>("TRIBE_HUNS");

            // a DIFFERENT same-horse-class pair if one was rolled, else same both sides
            TribeType west = mTribePool[0], east = mTribePool[0];
            bool found = false;
            for (int i = 0; i < mTribePool.Count && !found; i++)
                for (int j = 0; j < mTribePool.Count && !found; j++)
                    if (i != j && IsHorseTribe(mTribePool[i]) == IsHorseTribe(mTribePool[j]))
                    { west = mTribePool[i]; east = mTribePool[j]; found = true; }
            TribeType centre = west;
            if (mHunsRolled) centre = huns;
            else foreach (TribeType p in mTribePool)
                if ((int)p != (int)west && (int)p != (int)east) { centre = p; break; }

            TribeType barbs = infos.getType<TribeType>("TRIBE_BARBARIANS");

            // The CENTRAL tribe holds the contested middle kingdom: the highland
            // prize, the island prize, and (when a side has 7+ sites) one forward
            // site per side. Layout per side, MIRRORED EXACTLY: capital (free) +
            // its nearest site (free) + 2 barbarian camps (most coastal) + 1
            // central-tribe forward site (closest to the centre seam) + the rest
            // held by that side's tribe. Net on a 16-site map: 2 start, 2 free,
            // 4 barb, 4 central, 2+2 side tribes.
            if (mCentreSiteId >= 0) GetTile(mCentreSiteId).TribeSite = centre;
            if (mIslandSiteId >= 0) GetTile(mIslandSiteId).TribeSite = centre;
            if (mCentreSiteId2 >= 0) GetTile(mCentreSiteId2).TribeSite = centre;
            if (mIslandSiteId2 >= 0) GetTile(mIslandSiteId2).TribeSite = centre;

            CitySiteType noSite = GetTile(0, 0).CitySite;
            var wsites = new List<int[]>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < c; x++)
                {
                    if (GetTile(x, y).CitySite.Equals(noSite) || OnIsland(x, y)) continue;
                    if (y * W + x == mIslandSiteId || y * W + x == mCentreSiteId) continue;  // prizes are the centre tribe's
                    wsites.Add(new[] { x, y });
                }
            if (wsites.Count == 0) return;

            var role = new TribeType?[wsites.Count];   // null = free
            var kind = new int[wsites.Count];          // 0=free 1=barb 2=side 3=central

            int capIdx = 0;
            for (int i = 1; i < wsites.Count; i++)
                if (wsites[i][0] < wsites[capIdx][0]) capIdx = i;
            int freeIdx = -1; double bestFree = 1e9;
            for (int i = 0; i < wsites.Count; i++)
            {
                if (i == capIdx) continue;
                double d = TileDist(wsites[i][0], wsites[i][1], wsites[capIdx][0], wsites[capIdx][1]);
                if (d < bestFree) { bestFree = d; freeIdx = i; }
            }

            // two barb camps: the most coastal of the remaining sites
            for (int b = 0; b < 2; b++)
            {
                int pick = -1, bestSea = int.MaxValue;
                for (int i = 0; i < wsites.Count; i++)
                {
                    if (i == capIdx || i == freeIdx || role[i] != null) continue;
                    int dSea = DSea(wsites[i][0], wsites[i][1]);
                    if (dSea < bestSea) { bestSea = dSea; pick = i; }
                }
                if (pick >= 0) { role[pick] = barbs; kind[pick] = 1; }
            }

            // count what's left for tribes; with 3+ left, the central tribe takes
            // the FORWARD site (closest to the centre seam), the side tribe the rest
            int remaining = 0;
            for (int i = 0; i < wsites.Count; i++)
                if (i != capIdx && i != freeIdx && role[i] == null) remaining++;
            if (remaining >= 3 && !PointMode)   // point mode: 2 islands + 2 highlands = 4 already
            {
                // the central tribe's FORWARD site: the corridor holds only the
                // two prizes, so it's each side's site closest to the seam —
                // the one pressed up against its spur
                int pick = -1, bestX = -1;
                for (int i = 0; i < wsites.Count; i++)
                {
                    if (i == capIdx || i == freeIdx || role[i] != null) continue;
                    if (wsites[i][0] > bestX) { bestX = wsites[i][0]; pick = i; }
                }
                if (pick >= 0) { role[pick] = centre; kind[pick] = 3; }
            }
            for (int i = 0; i < wsites.Count; i++)
                if (i != capIdx && i != freeIdx && role[i] == null) { role[i] = west; kind[i] = 2; }

            // The GAME honours roughly 2 settlements per tribe-in-use and strips
            // the rest back to barbarian territory (verified from a turn-1 save:
            // 3 tribes in use → exactly 6 named sites survived of our 8). Keep
            // our named-site count within that budget — shed side sites first.
            int named = 2;                                       // highland + island
            for (int i = 0; i < wsites.Count; i++)
                if (kind[i] == 2 || kind[i] == 3) named += 2;    // site + its mirror
            // 5 tribes-in-use buy a 10-settlement budget — covers every duel
            // roll (side tribes keep 2-3 sites). On BIGGER map sizes the
            // extra sites beyond the budget become additional barbarian camps
            // (mirrored pairs): a wilder frontier, never free giveaways.
            int budget = 2 * (tribesToUse != null ? tribesToUse.Count : 5);
            for (int i = wsites.Count - 1; i >= 0 && named > budget; i--)
                if (kind[i] == 2) { role[i] = barbs; kind[i] = 1; named -= 2; }

            // apply + mirror EXACTLY (same role; side tribe west↔east)
            for (int i = 0; i < wsites.Count; i++)
            {
                if (role[i] == null) continue;
                int bx = wsites[i][0], by = wsites[i][1];
                TribeType who = role[i].Value;
                TribeType whoEast = (kind[i] == 2) ? east : who;   // only SIDE sites flip tribes
                GetTile(bx, by).TribeSite = who;
                int mxx, myy; MirrorOf(bx, by, out mxx, out myy);
                if (mxx >= c && mxx < W && myy >= 0 && myy < H
                    && !GetTile(myy * W + mxx).CitySite.Equals(noSite))
                    GetTile(myy * W + mxx).TribeSite = whoEast;
            }
        }

        // Urban founding tiles belong to a city site; when FinalizeSites trims a
        // site, its urban ring must go too or it lingers as a ghost town.
        private void CleanGhostUrban()
        {
            int W = MapWidth, H = MapHeight;
            CitySiteType none = GetTile(0, 0).CitySite;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    TileData t = GetTile(x, y);
                    if (!t.Terrain.Equals(URBAN_TERRAIN)) continue;
                    bool near = false;
                    for (int dy = -2; dy <= 2 && !near; dy++)
                        for (int dx = -2; dx <= 2 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                            if (TileDist(x, y, nx, ny) > 2) continue;
                            if (!GetTile(nx, ny).CitySite.Equals(none)) near = true;
                        }
                    if (!near) t.Terrain = TEMPERATE_TERRAIN;   // ghost town → plain land
                }
        }

        private bool IsHorseTribe(TribeType t)
        {
            int scy = (int)infos.getType<TribeType>("TRIBE_SCYTHIANS");
            int num = (int)infos.getType<TribeType>("TRIBE_NUMIDIANS");
            return (int)t == scy || (int)t == num;
        }
    }
}
