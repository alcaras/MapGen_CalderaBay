using System;
using System.Collections.Generic;
using TenCrowns.GameCore;

namespace OwMapCreation
{
    // CALDERA BAY — a geomorphologically-motivated mirror duel.
    //
    // The story (which fixes every feature): a coastal mountain range walls
    // off the north and sheds water south across a plain into the sea. The
    // trunk river's drowned lower course is the central BAY (a ria/estuary);
    // it exits the range through a water-gap. Foothill SPURS finger south from
    // the range, splitting each half into lanes; the valleys + the open plain
    // are the passes. The estuary's fertile floodplain is ringed by deltaic
    // MARSH (or, some seasons, a sun-baked arid basin) — slow to cross. And in
    // the bay sits a small VOLCANIC ISLAND, its slopes fed by radial streams:
    // a rich neutral prize reachable only by sea. Two realms face off across
    // the water, mountains at their backs.
    //
    // Implementation: a continuous, mirror-symmetric height field e(x,y)
    // classified into ocean/coast/flat/hill/mountain, with the island and the
    // marsh/desert moat stamped over it. We keep the engine's elevation+river
    // passes so real rivers flow downhill on our slope into the bay; rivers
    // live in separate fields, so our terrain re-stamp doesn't erase them.
    public class MapScriptCalderaBay : DefaultMapScript
    {
        // --- macro terrain (tile units; sea level is e = 0) ---
        const int SEA_ROWS = 8;            // sea depth along the south edge
        const double BAY_RISE = 10;        // rows the estuary climbs above the sea at centre (~half the map)
        const double BAY_HALF_FRAC = 0.15; // estuary half-width as a fraction of map width
        const int COAST_BAND = 2;          // shallow (coast) water rows before deep ocean
        const double NOISE_AMP = 2.6;      // organic irregularity for coastline & mountains

        // --- spurs / foothills (idea 4: more spurs, reaching south) ---
        const double RIDGE_AMP = 16;       // how tall the spurs rise
        const double SPUR_REACH = 0.5;     // <1 → gentler taper → spurs reach further south
        const double SPUR_OFFSET_FRAC = 0.17; // the two spurs sit this far (frac of W) from centre
        const double SPUR_IN_W = 1.3;      // inner (bay-facing) half-width — SHARP
        const double SPUR_OUT_W = 3.6;     // outer (toward edge) half-width — a smooth parabolic CURVE
        const double NORTH_PLATEAU = 14;   // base slope plateaus here (open highland, not a wall)
        const int RANGE_ROWS = 3;          // mountain range band along the top
        const double GORGE_HALF_FRAC = 0.11; // central gap in the top range — the river gorge / bay exit
        const double HILL_E = 10;          // e >= this → hills
        const double MTN_E = 18;           // e >= this → mountains

        // --- marsh/desert moat around the estuary (idea 3) ---
        const double MOAT_HALF_FRAC = 0.20; // central band that turns boggy/arid
        const double MOAT_E = 4;            // only the lowest floodplain (e below this)

        // --- volcanic bay island (idea 2) ---
        const double ISLAND_R = 3.4;        // island radius (tiles)
        const double ISLAND_MOAT = 1.5;     // forced water ring → island needs a sea crossing
        const int ISLAND_Y = 7;             // island centre row (inside the bay)

        private bool mRolled, mDesertMoat;

        public MapScriptCalderaBay(ref MapParameters mapParameters, Infos infos)
            : base(ref mapParameters, infos)
        {
        }

        // Declares the script's custom multi-choice options. The engine calls
        // this static method by reflection to know which options to surface in
        // the New Game UI and to load into the script's multiOptions dict.
        public static new void GetCustomOptionsMulti(List<MapOptionsMultiType> options, Infos infos)
        {
            options.Add(infos.getType<MapOptionsMultiType>("MAP_OPTIONS_MULTI_CALDERA_MOAT"));
        }

        protected override void GenerateLand()
        {
            if (!mRolled) { mDesertMoat = ResolveMoat(); mRolled = true; }
            ApplyLayout();
        }

        // The "Estuary Floodplain" map option: Marsh, Desert, or Random
        // (a per-game coin-flip). Falls back to Random if the option is unset.
        private bool ResolveMoat()
        {
            // Read the selection straight from the game parameters. (The base
            // InitMapData only loads the built-in options into multiOptions, not
            // custom ones, so TryGetMultiOption wouldn't see it.)
            var opt = infos.getType<MapOptionsMultiType>("MAP_OPTIONS_MULTI_CALDERA_MOAT");
            MapOptionType choice;
            if ((int)opt >= 0 &&
                mapParameters.gameParams.mapMapMultiOptions.TryGetValue(opt, out choice))
            {
                int c = (int)choice;
                if (c == (int)infos.getType<MapOptionType>("MAP_OPTION_CALDERA_MARSH")) return false;
                if (c == (int)infos.getType<MapOptionType>("MAP_OPTION_CALDERA_DESERT")) return true;
            }
            return random.Next(2) == 0;
        }

        // Place land/mountains and climate ourselves, but KEEP the engine's
        // elevation + river passes (rivers flow downhill into the bay) and its
        // BuildVegetation (natural forests/scrub on our terrain, like the
        // built-in maps). We no-op only the passes that would fight our design.
        protected override void GenerateDeserts() { }
        protected override void GenerateMountains() { }
        protected override void ModifyTerrain() { }
        protected override void SmoothTerrain() { }

        // Tribes: exactly one DIPLOMACY tribe per player — a DIFFERENT one each
        // side, placed at mirror positions (so the layout is fair but the two
        // neighbours aren't identical) — plus one tribe alone in the contested
        // centre. Rules the user asked for:
        //   * a different tribe per player side;
        //   * if either side's tribe is a HORSE tribe (Scythians/Numidians),
        //     BOTH sides get a horse tribe (never a horse vs foot mismatch);
        //   * both player tribes are diplomacy tribes, so the count of
        //     barbarian-vs-tribe sites is identical on each half (1 tribe, 0
        //     barb per side);
        //   * the centre tribe is HUNS only if the engine actually rolled them
        //     (they "had a chance to spawn"), else another tribe — and never on
        //     a player's site (it sits on the dead-centre self-mirror axis).
        // We keep the engine's tribe setup (SetTribesToUse, etc.) via base, read
        // back what it rolled, then re-place the sites.
        protected override void PlaceTribes()
        {
            base.PlaceTribes();
            int W = MapWidth, H = MapHeight;
            TribeType none = GetTile(0, 0).TribeSite;
            CitySiteType noSite = GetTile(0, 0).CitySite;
            TribeType huns = infos.getType<TribeType>("TRIBE_HUNS");

            // What did the engine roll? Use its natural pick for the west tribe
            // (very "like the game"), and only allow Huns in the centre if they
            // were among the rolled tribes.
            bool hunsRolled = false;
            TribeType engineChoice = none;
            for (int i = 0; i < W * H; i++)
            {
                TribeType tb = GetTile(i).TribeSite;
                if (tb.Equals(none)) continue;
                if ((int)tb == (int)huns) hunsRolled = true;
                else if (engineChoice.Equals(none)) engineChoice = tb;
            }

            TribeType[] pool = BuildTribePool(huns);     // diplomacy, non-Huns
            TribeType west = engineChoice.Equals(none) ? pool[0] : engineChoice;
            TribeType east = PickPartner(west, pool);    // different + horse-paired
            TribeType centre = hunsRolled ? huns : PickThird(west, east, pool);

            for (int i = 0; i < W * H; i++)              // clear the engine's scatter
                if (!GetTile(i).TribeSite.Equals(none)) GetTile(i).TribeSite = none;

            // West player's tribe at a west site; the mirror tile gets the EAST
            // tribe (same position → fair, different identity → varied).
            PlaceMirroredPair((int)(W * 0.24), H / 2 + 2, west, east, none, noSite);
            // Centre tribe alone on the contested axis (a self-mirror tile, so it
            // belongs to neither half and is never doubled).
            PlaceCentreAxis((int)(H * 0.55), centre, none, noSite);
        }

        private bool IsHorseTribe(TribeType t)
        {
            int scy = (int)infos.getType<TribeType>("TRIBE_SCYTHIANS");
            int num = (int)infos.getType<TribeType>("TRIBE_NUMIDIANS");
            return (int)t == scy || (int)t == num;
        }

        // The diplomacy (non-barbarian) tribe pool, in a fixed order so the pick
        // is deterministic for a given engine roll. Excludes Huns (centre-only).
        private TribeType[] BuildTribePool(TribeType huns)
        {
            string[] names = { "TRIBE_GAULS", "TRIBE_VANDALS", "TRIBE_DANES",
                               "TRIBE_THRACIANS", "TRIBE_SCYTHIANS", "TRIBE_NUMIDIANS" };
            var list = new System.Collections.Generic.List<TribeType>();
            foreach (var n in names)
            {
                TribeType t = infos.getType<TribeType>(n);
                if ((int)t >= 0 && (int)t != (int)huns) list.Add(t);
            }
            return list.ToArray();
        }

        // A partner tribe for the east player: different from the west tribe and
        // in the SAME horse class — a horse tribe is paired with the other horse
        // tribe; a foot tribe with another foot tribe.
        private TribeType PickPartner(TribeType west, TribeType[] pool)
        {
            bool wHorse = IsHorseTribe(west);
            foreach (TribeType t in pool)
                if ((int)t != (int)west && IsHorseTribe(t) == wHorse) return t;
            foreach (TribeType t in pool)           // fallback: any different tribe
                if ((int)t != (int)west) return t;
            return west;
        }

        private TribeType PickThird(TribeType a, TribeType b, TribeType[] pool)
        {
            foreach (TribeType t in pool)
                if ((int)t != (int)a && (int)t != (int)b) return t;
            return a;
        }

        private bool EligibleTribe(TileData t, TribeType none, CitySiteType noSite)
        {
            if (t.Terrain.Equals(WATER_TERRAIN)) return false;
            if (t.Height.Equals(MOUNTAIN_HEIGHT) || t.Height.Equals(VOLCANO_HEIGHT)) return false;
            if (!t.CitySite.Equals(noSite)) return false;
            return t.TribeSite.Equals(none);
        }

        // Place the west tribe near (sx,sy) on the west half and the east tribe
        // on that tile's exact mirror — identical position, different identity.
        private void PlaceMirroredPair(int sx, int sy, TribeType west, TribeType east,
                                       TribeType none, CitySiteType noSite)
        {
            int W = MapWidth, H = MapHeight;
            for (int rad = 0; rad < 12; rad++)
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int x = sx + dx, y = sy + dy;
                        if (x < 0 || x >= W || y < 0 || y >= H) continue;
                        if (x >= W / 2) continue;                 // west half only
                        int mxx = (y % 2 == 0) ? (W - 1 - x) : (W - x);
                        if (mxx < 0 || mxx >= W) continue;
                        TileData t = GetTile(x, y);
                        TileData m = GetTile(mxx, y);
                        if (!EligibleTribe(t, none, noSite)) continue;
                        if (!EligibleTribe(m, none, noSite)) continue;
                        t.TribeSite = west;
                        m.TribeSite = east;
                        return;
                    }
        }

        // The centre tribe sits on a self-mirror tile (odd row, x=W/2) so it is
        // exactly central — never on a player's side, never doubled.
        private void PlaceCentreAxis(int sy, TribeType tribe, TribeType none, CitySiteType noSite)
        {
            int W = MapWidth, H = MapHeight, x = W / 2;
            for (int rad = 0; rad < 14; rad++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int y = sy + rad * s;
                    if (y < 0 || y >= H || (y % 2) == 0) continue;   // need self-mirror row
                    TileData t = GetTile(x, y);
                    if (!EligibleTribe(t, none, noSite)) continue;
                    t.TribeSite = tribe;
                    return;
                }
        }

        // Our terrain is finalised here — BEFORE the engine builds continents,
        // cities, urban tiles and resources — so all of that engine work (the
        // city-site founding tiles, natural resource distribution, vegetation…)
        // survives untouched, exactly as the built-in maps do.
        protected override void SetUnreachableAreas()
        {
            base.SetUnreachableAreas();
            ApplyLayout();
        }

        // The ONLY thing we still do after the engine: enforce mirror symmetry.
        // The engine mirrors city sites but not resources/urban/terrain detail,
        // and a duel must be fair. MirrorGameplay COPIES west→east (it preserves
        // the engine's urban/resources, just mirrors them) — no re-stamping.
        protected override void CalculateClosestCitySites()
        {
            base.CalculateClosestCitySites();
            PlaceCenterPrizes();   // a light bias at the two contested centres
            MirrorGameplay();      // then enforce mirror symmetry
        }

        // A LIGHT prize bias on the two contested centres (everything else is the
        // engine's natural distribution). Placed on the west half; MirrorGameplay
        // mirrors it. Island: gold + a pearl. Top-centre highlands: marble + jade.
        private void PlaceCenterPrizes()
        {
            int W = MapWidth, H = MapHeight;
            double cx = (W - 1) / 2.0;
            VegetationType noVeg = GetTile(0, 0).Vegetation;   // an empty (deep-ocean) tile

            // Set a resource on a tile, making the tile VALID for it: correct
            // terrain + height, and vegetation cleared (e.g. gold/marble can't
            // sit under trees). MirrorGameplay mirrors all of this east.
            System.Action<TileData, TerrainType, HeightType, string> put =
                (t, ter, h, res) =>
                { t.Terrain = ter; t.Height = h; t.Vegetation = noVeg; t.Resource = R(res); };

            bool gold = false, pearl = false;
            for (int y = 0; y < H && (!gold || !pearl); y++)
                for (int x = 0; x < W / 2 && (!gold || !pearl); x++)
                {
                    if (!OnIsland(x, y)) continue;
                    TileData t = GetTile(x, y);
                    if (!gold && t.Height.Equals(HILL_HEIGHT))
                    { put(t, TEMPERATE_TERRAIN, HILL_HEIGHT, "RESOURCE_GOLD"); gold = true; }   // gold: temperate/arid hill
                    else if (!pearl && t.Terrain.Equals(WATER_TERRAIN) && t.Height.Equals(COAST_HEIGHT))
                    { t.Resource = R("RESOURCE_PEARL"); pearl = true; }                          // pearl: coast water
                }
            bool marble = false, jade = false;
            for (int y = H - RANGE_ROWS - 1; y >= SEA_ROWS && (!marble || !jade); y--)
                for (int x = (int)cx - 1; x >= (int)cx - 6 && (!marble || !jade); x--)
                {
                    TileData t = GetTile(x, y);
                    if (t.Terrain.Equals(WATER_TERRAIN)) continue;
                    if (t.Height.Equals(MOUNTAIN_HEIGHT) || t.Height.Equals(VOLCANO_HEIGHT)) continue;
                    if (!marble && t.Height.Equals(FLAT_HEIGHT))
                    { put(t, LUSH_TERRAIN, FLAT_HEIGHT, "RESOURCE_MARBLE"); marble = true; }      // marble: lush/temperate/arid flat
                    else if (!jade && t.Height.Equals(HILL_HEIGHT))
                    { put(t, T("TERRAIN_ARID"), HILL_HEIGHT, "RESOURCE_JADE"); jade = true; }     // jade: arid flat/hill
                }
        }

        // The engine places city sites, resources and tribes AFTER DoMirrorMap,
        // so on a mirror map they come out asymmetric (e.g. 5 sites one side, 4
        // the other). Force exact symmetry: copy the canonical (west) half's
        // sites/resources onto the east half (tribes are placed separately, one
        // distinct tribe per side). Terrain is already symmetric by
        // construction, so these land on identical ground.
        private void MirrorGameplay()
        {
            // Caldera Bay is always a mirror duel, so enforce it unconditionally
            // (don't depend on the MirrorMap option being wired in headless gen).
            int W = MapWidth, H = MapHeight;
            // Explicit hex row-stagger mirror (even rows W-1-x, odd rows W-x) —
            // this matches owmapgen's start mirror AND works for every tile,
            // including water (the engine's GetMirrorTileOfThisTile returns -1
            // for water, which left the island's pearls/fish unmirrored).
            // Copy each pair once, west (lower x) → east.
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int mxx = (y % 2 == 0) ? (W - 1 - x) : (W - x);
                    if (mxx <= x || mxx >= W) continue;   // west→east, skip self/edge
                    TileData src = GetTile(x, y);
                    TileData dst = GetTile(mxx, y);
                    dst.Terrain = src.Terrain;      // also mirror the resource-driven
                    dst.Height = src.Height;        // terrain tweaks (temperate farms, etc.)
                    dst.Vegetation = src.Vegetation; // engine forests/scrub
                    dst.CitySite = src.CitySite;
                    dst.Resource = src.Resource;
                    // NB: TribeSite is intentionally NOT mirrored — PlaceTribes
                    // already placed a DIFFERENT tribe per side at mirror
                    // positions; copying west→east here would clobber the east
                    // tribe and make both sides identical.
                }
            }
        }

        // Continuous height at (x, y). x grows east, y grows north (y=0 = south
        // sea edge). Even in x about the centre column ⇒ left-right symmetric.
        private double Height(int x, int y, int W, int H)
        {
            double cx = (W - 1) / 2.0;
            double xs = x - cx;
            // Base rises from the sea but plateaus at highland level — no thick
            // horizontal mountain band; the mountains come from the spurs.
            double slope = Math.Min(y - SEA_ROWS, NORTH_PLATEAU);

            double bayHalf = W * BAY_HALF_FRAC;
            double trunk = BAY_RISE * Math.Max(0.0, 1.0 - Math.Abs(xs) / bayHalf);

            double denom = Math.Max(1, H - 1 - SEA_ROWS);
            double north = Math.Min(1.0, Math.Max(0.0, (y - SEA_ROWS) / denom));  // 0 coast → 1 top
            double taper = Math.Pow(north, SPUR_REACH);

            // Two spurs (mirror pair) flanking the bay. Each is an ASYMMETRIC
            // triangle: a SHARP drop on the inner (bay-facing) side, a smooth
            // parabolic CURVE on the outer side; widening toward the range.
            double off = Math.Abs(xs) - W * SPUR_OFFSET_FRAC;          // <0 inner (toward bay), >0 outer
            double w = (off >= 0 ? SPUR_OUT_W : SPUR_IN_W) * (0.5 + 2.0 * north);
            double spur = Math.Exp(-(off * off) / (w * w));

            // organic noise → irregular coastline & mountains. (Harsh land/water
            // edges are fixed by the coast pass in ApplyLayout; mirror symmetry by
            // MirrorGameplay.)
            double n = 1.6 * Math.Sin(x * 0.70 + y * 0.45)
                     + 1.1 * Math.Sin(x * 0.33 - y * 0.90 + 1.3)
                     + 0.7 * Math.Sin(x * 1.25 + y * 0.20 + 2.1);

            double e = slope - trunk + RIDGE_AMP * spur * taper + NOISE_AMP * n;

            // mountain range along the top — but NOT through the central gorge
            // (where the river/bay breaches the range).
            if (y >= H - RANGE_ROWS && Math.Abs(xs) > W * GORGE_HALF_FRAC)
                e = Math.Max(e, MTN_E + 4);
            return e;
        }

        private void ApplyLayout()
        {
            int W = MapWidth, H = MapHeight;
            double cx = (W - 1) / 2.0;

            for (int x = 0; x < W; x++)
            {
                double xs = x - cx;
                for (int y = 0; y < H; y++)
                {
                    TileData tile = GetTile(x, y);

                    // Preserve engine LAKES — they're where rivers terminate, so
                    // keeping them ensures every river ends in water, not on land.
                    if (tile.Height.Equals(LAKE_HEIGHT)) continue;

                    // --- volcanic island in the bay (symmetric about cx) ---
                    double dx = xs, dy = y - ISLAND_Y;
                    double idist = Math.Sqrt(dx * dx + dy * dy);
                    if (idist <= ISLAND_R)
                    {
                        tile.Terrain = LUSH_TERRAIN;
                        if (idist <= 1.1) tile.Height = VOLCANO_HEIGHT;       // the caldera peak
                        else if (idist <= 2.2) tile.Height = HILL_HEIGHT;     // volcanic slopes
                        else tile.Height = FLAT_HEIGHT;                       // fertile coastal flat
                        continue;
                    }
                    if (idist <= ISLAND_R + ISLAND_MOAT)
                    {
                        // forced water ring: the island is always its own land
                        // mass, reachable only by crossing water.
                        tile.Terrain = WATER_TERRAIN;
                        tile.Height = COAST_HEIGHT;
                        continue;
                    }

                    double e = Height(x, y, W, H);
                    if (e < 0)
                    {
                        tile.Terrain = WATER_TERRAIN;
                        tile.Height = e > -COAST_BAND ? COAST_HEIGHT : OCEAN_HEIGHT;
                    }
                    else if (e < MOAT_E && Math.Abs(xs) < W * MOAT_HALF_FRAC)
                    {
                        // the estuary's floodplain: deltaic marsh, or arid basin
                        tile.Terrain = mDesertMoat ? DESERT_TERRAIN : WET_TERRAIN;
                        tile.Height = FLAT_HEIGHT;
                    }
                    else
                    {
                        // dry land: a moisture-driven climate sets the terrain,
                        // elevation sets the height.
                        tile.Terrain = Climate(x, y, W, H);
                        tile.Height = e < HILL_E ? FLAT_HEIGHT
                                    : e < MTN_E ? HILL_HEIGHT : MOUNTAIN_HEIGHT;
                    }
                }
            }
            FixCoast();
        }

        // Proper coast: every water tile touching land becomes shallow COAST
        // (a beach/shallow ring), deeper water stays OCEAN — so the land/water
        // transition is never harsh, however irregular the coastline. Engine
        // lakes are left alone (they're their own water type).
        private void FixCoast()
        {
            int W = MapWidth, H = MapHeight;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    TileData t = GetTile(x, y);
                    if (!t.Terrain.Equals(WATER_TERRAIN)) continue;
                    if (t.Height.Equals(LAKE_HEIGHT)) continue;
                    int[] dx, dy; Neigh(y, out dx, out dy);
                    bool nearLand = false;
                    for (int k = 0; k < 6; k++)
                    {
                        int nx = x + dx[k], ny = y + dy[k];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        if (!GetTile(nx, ny).Terrain.Equals(WATER_TERRAIN)) { nearLand = true; break; }
                    }
                    t.Height = nearLand ? COAST_HEIGHT : OCEAN_HEIGHT;
                }
        }

        // Climate (mirror-symmetric: even in x). MOST of the plain is temperate.
        // Lush hugs water — the river valleys, the coast and the estuary — while
        // the dry spur tops and inland pockets turn arid. (Vegetation/forests are
        // left to the engine's BuildVegetation, as the built-in maps do.)
        private TerrainType Climate(int x, int y, int W, int H)
        {
            double cx = (W - 1) / 2.0, xs = x - cx;
            double span = Math.Max(1, (H - MTN_ROWS) - SEA_ROWS);
            double mid = Math.Min(1.0, Math.Max(0.0, (y - SEA_ROWS) / span)); // 0 coast → 1 range

            double coast = Math.Max(0.0, 1.2 - (y - SEA_ROWS) / 5.0);          // wet near the south sea
            double estuary = Math.Max(0.0, 1.0 - Math.Abs(xs) / (W * 0.22)) * (1.0 - mid);
            double dSpur = Math.Abs(Math.Abs(xs) - W * SPUR_OFFSET_FRAC);
            double dry = Math.Exp(-(dSpur * dSpur) / (SPUR_OUT_W * SPUR_OUT_W)) * mid;  // rain shadow on the spurs
            double noise = 0.12 * Math.Cos(xs * 0.5) + 0.10 * Math.Cos(y * 0.8);
            double moisture = 0.45 + Math.Max(coast, estuary) - 0.7 * dry + noise; // temperate baseline

            if (moisture > 0.95) return LUSH_TERRAIN;        // narrow: coast, estuary, river valleys
            if (moisture > 0.22) return TEMPERATE_TERRAIN;   // the broad majority
            return T("TERRAIN_ARID");                        // dry spur flanks
        }

        // ---- resource tiering (ideas 1, 2, 5) ----------------------------
        // Place on the canonical west half + the island; MirrorGameplay copies
        // the whole gameplay layer (terrain + resources) to the east half.

        private TerrainType T(string z) { return infos.getType<TerrainType>(z); }
        private ResourceType R(string z) { return infos.getType<ResourceType>(z); }

        private const int MTN_ROWS = 2; // top rows treated as the range crest

        private HashSet<int> mUsed;

        private void PlaceResources()
        {
            // The home/expansion economy is left entirely NATURAL — Old World's own
            // resource distribution (mirrored for symmetry). We only add the two
            // deliberate central PRIZES (the island here + the mountain city, post-
            // mirror) and a few sea resources along the coast.
            mUsed = new HashSet<int>();
            PlaceIslandRiches();   // southern prize, in the bay
            PlaceSeaResources();   // fish/crab along the coast
        }

        // The mountain city — the SINGLE dead-centre northern site (the one on
        // the mirror axis) — is the second contested prize alongside the island.
        // Run this AFTER mirroring and search the full width: the axis site is a
        // self-mirror tile (often on the east of centre), so a west-only search
        // would miss it and the mirror would otherwise wipe an east-side enrich.
        private void EnrichMountainCity()
        {
            int W = MapWidth, H = MapHeight;
            CitySiteType none = GetTile(0, 0).CitySite;
            int mcx = -1, mcy = -1, bestY = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (!OnIsland(x, y) && !GetTile(x, y).CitySite.Equals(none)
                        && Math.Abs(x - (W - 1) / 2.0) < 4.0 && y > bestY)
                    { bestY = y; mcx = x; mcy = y; }
            if (mcx < 0) return;
            PlaceNear(mcx, mcy, "RESOURCE_MARBLE", FLAT_HEIGHT, LUSH_TERRAIN, none);
            PlaceNear(mcx, mcy, "RESOURCE_JADE", HILL_HEIGHT, T("TERRAIN_ARID"), none);
            PlaceNear(mcx, mcy, "RESOURCE_SILVER", HILL_HEIGHT, T("TERRAIN_ARID"), none);
        }

        // A handful of sea resources along the coast (fish / crab), spaced out.
        private void PlaceSeaResources()
        {
            int W = MapWidth, H = MapHeight;
            string[] sea = { "RESOURCE_FISH", "RESOURCE_CRAB" };
            int idx = 0, placed = 0;
            for (int y = 0; y < H && placed < 6; y++)
                for (int x = 0; x < W / 2 && placed < 6; x++)
                {
                    if (OnIsland(x, y)) continue;
                    int id = y * W + x;
                    if (mUsed.Contains(id)) continue;
                    TileData t = GetTile(x, y);
                    if (!t.Terrain.Equals(WATER_TERRAIN) || !t.Height.Equals(COAST_HEIGHT)) continue;
                    if ((x * 3 + y) % 7 != 0) continue;   // spread them along the shore
                    t.Resource = R(sea[idx++ % 2]); mUsed.Add(id); placed++;
                }
        }

        private bool OnIsland(int x, int y)
        {
            double dx = x - (MapWidth - 1) / 2.0, dy = y - ISLAND_Y;
            return Math.Sqrt(dx * dx + dy * dy) <= ISLAND_R + ISLAND_MOAT;
        }

        // The volcanic island: the richest spot on the map, but a prize, not a
        // piñata — a couple of luxuries on the slopes, some fertile flats, a few
        // pearls in the ring. Caps are per (west) half; MirrorGameplay doubles.
        private void PlaceIslandRiches()
        {
            int W = MapWidth, H = MapHeight;
            double cx = (W - 1) / 2.0;
            int li = 0, food = 0, pearl = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W / 2; x++)
                {
                    double dx = x - cx, dy = y - ISLAND_Y;
                    double idist = Math.Sqrt(dx * dx + dy * dy);
                    if (idist > ISLAND_R + ISLAND_MOAT) continue;
                    TileData t = GetTile(x, y);
                    if (t.Height.Equals(VOLCANO_HEIGHT)) continue;          // impassable peak
                    if (t.Height.Equals(HILL_HEIGHT) && li < 1)            // one luxury
                    { t.Terrain = TEMPERATE_TERRAIN; t.Resource = R("RESOURCE_GOLD"); li++; }
                    else if (t.Height.Equals(FLAT_HEIGHT) && food < 1)     // one grain
                    { t.Terrain = TEMPERATE_TERRAIN; t.Resource = R("RESOURCE_WHEAT"); food++; }
                    else if (pearl < 1 && idist <= ISLAND_R + 1.0          // one pearl
                             && t.Terrain.Equals(WATER_TERRAIN) && t.Height.Equals(COAST_HEIGHT))
                    { t.Resource = R("RESOURCE_PEARL"); pearl++; }
                }
        }

        // Put resource `res` on a worked tile next to a site, preferring one
        // already at the right height, else reshaping one to fit. Tracks tiles
        // it has used so several placements on one site don't clobber each other.
        private void PlaceNear(int sx, int sy, string res, HeightType reqH,
                               TerrainType reqT, CitySiteType none)
        {
            int W = MapWidth, H = MapHeight;
            int[] dx, dy; Neigh(sy, out dx, out dy);
            int fx = -1, fy = -1;
            for (int i = 0; i < 6; i++)
            {
                int nx = sx + dx[i], ny = sy + dy[i];
                if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                int id = ny * W + nx;
                if (mUsed.Contains(id)) continue;
                TileData t = GetTile(nx, ny);
                if (t.Terrain.Equals(WATER_TERRAIN)) continue;
                if (!t.CitySite.Equals(none)) continue;
                if (t.Height.Equals(VOLCANO_HEIGHT) || t.Height.Equals(MOUNTAIN_HEIGHT)) continue;
                if (t.Height.Equals(reqH))
                { t.Terrain = reqT; t.Resource = R(res); mUsed.Add(id); return; }
                if (fx < 0) { fx = nx; fy = ny; }
            }
            if (fx >= 0)
            {
                TileData t = GetTile(fx, fy);
                t.Height = reqH; t.Terrain = reqT; t.Resource = R(res);
                mUsed.Add(fy * W + fx);
            }
        }

        private static void Neigh(int y, out int[] dx, out int[] dy)
        {
            if ((y & 1) == 1) { dx = new[] { -1, 0, 1, 0, -1, -1 }; dy = new[] { 1, 1, 0, -1, -1, 0 }; }
            else { dx = new[] { 0, 1, 1, 1, 0, -1 }; dy = new[] { 1, 1, 0, -1, -1, 0 }; }
        }

        private static int HexDist(int ax, int ay, int bx, int by)
        {
            int aq = ax - ((ay + (ay & 1)) / 2), ar = ay;
            int bq = bx - ((by + (by & 1)) / 2), br = by;
            int dcx = aq - bq, dcz = ar - br, dcy = -dcx - dcz;
            return (Math.Abs(dcx) + Math.Abs(dcy) + Math.Abs(dcz)) / 2;
        }
    }
}
