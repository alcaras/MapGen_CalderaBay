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
            base.SetMapSize();
            mapParameters.iWidth = 64;
            mapParameters.iHeight = 43;     // tall enough for 14–18 sites with the tall sea + lagoon
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
            }
            return -1;
        }
        private short ClimateLat(short med, short temp, short north, short rand)
        {
            switch (ResolveClimate()) { case 0: return med; case 1: return temp; case 2: return north; default: return rand; }
        }
        public override short MinLatitude { get { return ClimateLat(18, 35, 55, base.MinLatitude); } }
        public override short MaxLatitude { get { return ClimateLat(38, 52, 70, base.MaxLatitude); } }
        // keep lakes to a few (the basin default over-fills our locked terrain)
        protected override short LakePercent { get { return 3; } }

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

        private void LockCaldera()
        {
            int W = MapWidth, H = MapHeight;
            double cx = (W - 1) / 2.0;
            mOurMountain = new bool[W * H];

            // The SEA is on one edge (south or north, per gen); the mountains on
            // the opposite edge; the bay drains from the sea edge inland toward the
            // range. `d` = rows from the sea edge, so the same logic works flipped.
            mSeaSouth = random.Next(2) == 0;
            int seaBand = 6 + random.Next(3);                // base sea depth, 6–8 (always ≥5)
            int nSpurs = 1 + random.Next(2);                 // 1–2 spurs per side
            double[] spurOff = new double[nSpurs];
            for (int i = 0; i < nSpurs; i++) spurOff[i] = 0.16 + 0.22 * (random.Next(100) / 100.0);

            // WOBBLED COASTLINE: the locked sea band's depth varies per column (a
            // smooth, mirror-symmetric random walk from the centre outward), so the
            // shore meanders with coves and headlands instead of a ruler line.
            int half = W / 2 + 2;
            int[] depthHalf = new int[half + 1];
            double wob = 0;
            for (int k = 0; k <= half; k++)
            {
                wob += (random.Next(3) - 1) * 0.8;
                wob = Math.Max(-1.5, Math.Min(2.5, wob));
                depthHalf[k] = Math.Max(6, seaBand + (int)Math.Round(wob));
            }

            // BIG VOLCANIC ISLAND in a lagoon basin: radius ~2.6–3.4 (≈25–35 land
            // tiles), sitting in a widened bay-mouth basin that guarantees a ≥2-tile
            // water ring around it (so it is always sea-locked).
            mIslandR = 2.6 + random.Next(9) / 10.0;          // 2.6–3.4
            int islandD = seaBand + (int)Math.Ceiling(mIslandR) + 2;

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
                    spurAt[i][y] = W * spurOff[i] + drift;
                }
            }

            for (int x = 0; x < W; x++)
            {
                double xs = Math.Abs(x - cx);
                for (int y = 0; y < H; y++)
                {
                    TileData t = GetTile(x, y);
                    int d = mSeaSouth ? y : (H - 1 - y);      // rows from the sea edge
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
                    if (d >= H - 3)
                    {
                        if (xs > W * 0.11) LockLand(t, MOUNTAIN_HEIGHT, TEMPERATE_TERRAIN);
                        else LockLand(t, FLAT_HEIGHT, TEMPERATE_TERRAIN);   // the gorge pass
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
                    for (int i = 0; i < nSpurs && !onSpur; i++)
                        if (Math.Abs(xs - spurAt[i][y]) < 0.9 && inf > 0.42) onSpur = true;
                    if (onSpur) { LockLand(t, MOUNTAIN_HEIGHT, TEMPERATE_TERRAIN); continue; }

                    if (d >= H - 7)
                    {
                        LockTileTerrain(t, TEMPERATE_TERRAIN, true);        // land, but natural height
                        t.Terrain = TEMPERATE_TERRAIN;
                        continue;
                    }
                }
            }

            // VOLCANIC ISLAND in its lagoon BASIN — a real island (≈25–35 tiles):
            // lush flats with a hill ring near the centre (slopes for the caldera
            // and its gold), ringed by ≥2 tiles of locked water so it is always
            // its own landmass. The basin also widens the lower bay naturally.
            mIslandX = W / 2;
            mIslandY = mSeaSouth ? islandD : (H - 1 - islandD);
            int reach = (int)Math.Ceiling(mIslandR) + 2;
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = mIslandX + dx, y = mIslandY + dy;
                    if (x < 0 || x >= W || y < 0 || y >= H) continue;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    TileData t = GetTile(x, y);
                    if (dist <= 1.4)
                        LockLand(t, HILL_HEIGHT, TEMPERATE_TERRAIN);   // volcanic slopes (gold-valid)
                    else if (dist <= mIslandR)
                        LockLand(t, FLAT_HEIGHT, LUSH_TERRAIN);        // fertile apron
                    else if (dist <= mIslandR + 2.0)
                        LockWater(t);                                  // the guaranteed ring
                }
        }
        private bool mSeaSouth;
        private int mIslandX, mIslandY;
        private double mIslandR;

        // ================= gameplay (sites, tribes, prizes) =================

        private TerrainType T(string z) { return infos.getType<TerrainType>(z); }
        private ResourceType R(string z) { return infos.getType<ResourceType>(z); }
        private bool OnIsland(int x, int y)
        {
            int dx = x - mIslandX, dy = y - mIslandY;
            double r = mIslandR + 1.5;              // the island + its water ring
            return dx * dx + dy * dy <= r * r;
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
                if (s.ID != mIslandSiteId && s.ID != mCentreSiteId) continue;
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
            for (int yy = 0; yy < H; yy++)
                for (int xx = 0; xx < W; xx++)
                    if (OnIsland(xx, yy) && !GetTile(xx, yy).CitySite.Equals(none))
                    { mIslandSiteId = yy * W + xx; return; }            // already there
            // a self-mirror island tile (odd row, x=cx) that's foundable land…
            for (int rad = 0; rad < 6; rad++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int y = mIslandY + rad * s;
                    if (y < 0 || y >= H || (y % 2) == 0) continue;
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
                    if (y < 0 || y >= H || (y % 2) == 0 || !OnIsland(cx, y)) continue;
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
            TamePiedmont();        // …and sometimes builds a SECOND wall below our range
            MirrorGameplay();      // enforce exact L–R symmetry of the gameplay layer
            RepairLakes();         // again: the mirror can copy a river-fed west lake
                                   // onto an east tile with no river (riverless pond)
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
        }
        private void RichenAround(int siteId, int want, int radius)
        {
            if (siteId < 0) return;
            int W = MapWidth, H = MapHeight;
            int sx = siteId % W, sy = siteId / W;
            ResourceType noRes = GetTile(mIslandX, mIslandY).Resource;   // the volcano: never has one
            VegetationType noVeg = GetTile(0, 0).Vegetation;             // open sea: never has any

            int have = 0;
            var cands = new List<int[]>();
            for (int y = Math.Max(0, sy - radius); y <= Math.Min(H - 1, sy + radius); y++)
                for (int x = Math.Max(0, sx - radius); x <= Math.Min(W - 1, sx + radius); x++)
                {
                    if (TileDist(x, y, sx, sy) > radius) continue;
                    TileData t = GetTile(x, y);
                    if (!t.Resource.Equals(noRes)) { have++; continue; }
                    if (x > W / 2) continue;                 // canonical side; the mirror doubles
                    if (t.Terrain.Equals(URBAN_TERRAIN)) continue;
                    if (!t.CitySite.Equals(GetTile(0, 0).CitySite)) continue;
                    cands.Add(new[] { x, y });
                }
            // random order (Fisher–Yates with the seeded RNG)
            for (int i = cands.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); var tmp = cands[i]; cands[i] = cands[j]; cands[j] = tmp; }

            foreach (var c in cands)
            {
                if (have >= want) break;
                TileData t = GetTile(c[0], c[1]);
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
                    int mxx = (c[1] % 2 == 0) ? (W - 1 - c[0]) : (W - c[0]);
                    if (mxx > W / 2 && mxx < W && GetTile(mxx, c[1]).Terrain.Equals(WATER_TERRAIN))
                    {
                        GetTile(mxx, c[1]).Resource = R(pick);
                        if (TileDist(mxx, c[1], sx, sy) <= radius) have++;
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
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    TileData t = GetTile(x, y);
                    if (!t.Height.Equals(LAKE_HEIGHT)) continue;
                    int d = mSeaSouth ? y : (H - 1 - y);              // rows from the sea edge
                    bool inRangeZone = d > 0.72 * (H - 1);
                    int[] dx, dy; Neigh(y, out dx, out dy);
                    // fed = a river actually TOUCHES this lake tile (that's what
                    // keeps the river un-orphaned); a river merely nearby doesn't
                    // justify a pond in the mountains.
                    bool riverFed = IsRiver(t);
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

        // The engine sometimes grows a SECOND mountain wall a few rows in front of
        // our locked range, creating a walled-off plateau shelf between the two
        // (which then even collects city sites). Demote unlocked mountains in the
        // piedmont band to hills — foothills, not a second wall. Our own locked
        // tiles (the range and the spur seeds crossing the band) are untouched.
        private void TamePiedmont()
        {
            int W = MapWidth, H = MapHeight;
            for (int y = 0; y < H; y++)
            {
                int d = mSeaSouth ? y : (H - 1 - y);          // rows from the sea edge
                if (d < H - 9 || d >= H - 3) continue;        // the band below the range
                for (int x = 0; x < W; x++)
                {
                    TileData t = GetTile(x, y);
                    if (t.Height.Equals(MOUNTAIN_HEIGHT)
                        && (mOurMountain == null || !mOurMountain[t.ID]))
                        t.Height = HILL_HEIGHT;
                }
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
        private bool Foundable(int x, int y, CitySiteType none)
        {
            if (x < 3 || x >= MapWidth - 3 || y < 2 || y >= MapHeight - 2) return false;
            int d = mSeaSouth ? y : (MapHeight - 1 - y);     // rows from the sea edge
            if (d >= MapHeight - 8) return false;            // never wedged against the range
            TileData t = GetTile(x, y);
            if (!t.CitySite.Equals(none)) return false;
            if (t.Terrain.Equals(WATER_TERRAIN)) return false;
            if (t.Height.Equals(MOUNTAIN_HEIGHT) || t.Height.Equals(VOLCANO_HEIGHT) || t.Height.Equals(LAKE_HEIGHT)) return false;
            return true;
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
                    if (OnIsland(x, y) || !Foundable(x, y, none)) continue;
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

        private void FinalizeSites()
        {
            int W = MapWidth, H = MapHeight, c = W / 2;
            CitySiteType none = GetTile(0, 0).CitySite, sample = FirstSiteType(none);
            if (sample.Equals(none)) return;
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
            for (int y = 0; y < H; y++)
                for (int x = 0; x < c; x++)
                {
                    if (GetTile(x, y).CitySite.Equals(none)) continue;
                    int mxx = (y % 2 == 0) ? (W - 1 - x) : (W - x);
                    if (mxx > c && mxx < W) GetTile(mxx, y).CitySite = sample;
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
                    int dSea = mSeaSouth ? y : (H - 1 - y);
                    if (x < 3 || x > W / 2 - 3 || y < 2 || y >= H - 2 || OnIsland(x, y)
                        || dSea >= H - 8)                    // engine sites on the range shelf
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
                if (sites.Count <= 7 && wd >= SITE_SPACE) break;
                GetTile(sites[worst][0], sites[worst][1]).CitySite = none;
                sites.RemoveAt(worst);
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
                    int mxx = (y % 2 == 0) ? (W - 1 - x) : (W - x);
                    if (mxx <= x || mxx >= W) continue;
                    TileData src = GetTile(x, y), dst = GetTile(mxx, y);
                    dst.Terrain = src.Terrain; dst.Height = src.Height;
                    dst.Vegetation = src.Vegetation; dst.Resource = src.Resource;
                }
        }

        // ---- tribes: one tribe per side (different, horse-paired), one centre
        // tribe (Huns only if rolled). CRITICAL: only tribes the ENGINE actually
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
        // player side (a mirrored pair — different tribes, horse-paired), and the
        // centre tribe (Huns when rolled) holding the contested HIGHLAND city.
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

            // the centre tribe garrisons the highland prize city; barbarians
            // guard the island prize (the third barb camp)
            if (mCentreSiteId >= 0) GetTile(mCentreSiteId).TribeSite = centre;
            if (mIslandSiteId >= 0) GetTile(mIslandSiteId).TribeSite = barbs;

            // Per side: the CAPITAL (west-most site, where the start lands) and
            // its NEAREST site stay FREE (start + first expansion); the most
            // coastal of the rest is a BARBARIAN camp; every other site is held
            // by the side's TRIBE. The east half mirrors the west exactly.
            CitySiteType noSite = GetTile(0, 0).CitySite;
            var wsites = new List<int[]>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < c; x++)
                    if (!GetTile(x, y).CitySite.Equals(noSite) && !OnIsland(x, y))
                        wsites.Add(new[] { x, y });
            if (wsites.Count == 0) return;

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
            int barbIdx = -1; int bestSea = int.MaxValue;
            for (int i = 0; i < wsites.Count; i++)
            {
                if (i == capIdx || i == freeIdx) continue;
                int dSea = mSeaSouth ? wsites[i][1] : (H - 1 - wsites[i][1]);
                if (dSea < bestSea) { bestSea = dSea; barbIdx = i; }
            }
            for (int i = 0; i < wsites.Count; i++)
            {
                if (i == capIdx || i == freeIdx) continue;
                int bx = wsites[i][0], by = wsites[i][1];
                TribeType who = (i == barbIdx) ? barbs : west;
                TribeType whoEast = (i == barbIdx) ? barbs : east;
                GetTile(bx, by).TribeSite = who;
                int mxx = (by % 2 == 0) ? (W - 1 - bx) : (W - bx);
                if (mxx > c && mxx < W && !GetTile(mxx, by).CitySite.Equals(noSite))
                    GetTile(mxx, by).TribeSite = whoEast;
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
