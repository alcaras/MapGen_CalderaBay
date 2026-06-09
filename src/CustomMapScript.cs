using TenCrowns.GameCore;

namespace OwMapCreation
{
    // The simplest possible custom map: a completely flat, all-lush square
    // of land. No water, no hills/mountains, no rivers, no deserts, no
    // forests — every tile is identical Lush + Flat. Useful as a baseline
    // sanity check that the build → owmapgen → render loop works, and as a
    // template to start carving real terrain from.
    //
    // Class name (MapScriptFlatLush) is what you pass to owmapgen --script.
    // The "MapScript" prefix is stripped, so --script "FlatLush" (or
    // "flat-lush", or "Flat Lush") all resolve to this class.
    //
    // Strategy: stamp every tile Lush+Flat in GenerateLand, no-op every
    // feature-generating stage, then re-stamp at the last hooks because the
    // engine's water-area / boundary / lake passes (which we keep, so the
    // map stays valid) would otherwise pit the interior and frame the edge
    // with water. After Build() the map is 100% flat lush; the handful of
    // lake/urban tiles you may see in a preview are added by owmapgen's
    // post-Build start placement, which no map-script hook can reach.
    public class MapScriptFlatLush : DefaultMapScript
    {
        public MapScriptFlatLush(ref MapParameters mapParameters, Infos infos)
            : base(ref mapParameters, infos)
        {
        }

        // Stamp the whole map flat + lush. LUSH_TERRAIN / FLAT_HEIGHT are
        // protected helpers on DefaultMapScript; Tiles is every map tile.
        protected override void GenerateLand()
        {
            StampFlatLush(includeBoundary: true);
        }

        // Skip every feature pass so the map stays perfectly uniform.
        protected override void GenerateDeserts() { }
        protected override void GenerateMountains() { }
        protected override void GenerateElevations() { }
        protected override void GenerateRivers() { }
        protected override void ModifyTerrain() { }
        protected override void SmoothTerrain() { }
        protected override void BuildVegetation() { }

        // Re-stamp after the engine frames the map but before continents and
        // cities are built, so city placement sees a clean uniform interior.
        // Leave boundary tiles (the water frame) alone at this point.
        protected override void SetUnreachableAreas()
        {
            base.SetUnreachableAreas();
            StampFlatLush(includeBoundary: false);
        }

        // CalculateClosestCitySites is the very last method Build() runs.
        // Stamp EVERY tile (boundary included) so the finished map is land
        // edge to edge — no frame, no interior lakes, no marsh — leaving
        // Build() with a pristine, perfectly uniform flat-lush map.
        protected override void CalculateClosestCitySites()
        {
            base.CalculateClosestCitySites();
            StampFlatLush(includeBoundary: true);
        }

        // Force every (optionally non-boundary) tile to flat lush. Boundary
        // tiles are the engine's water frame; skip them until the final pass.
        private void StampFlatLush(bool includeBoundary)
        {
            foreach (TileData tile in Tiles)
            {
                if (!includeBoundary && tile.Boundary)
                    continue;
                tile.Terrain = LUSH_TERRAIN;
                tile.Height = FLAT_HEIGHT;
            }
        }
    }
}
