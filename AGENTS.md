# Working on ProceduralRoads

## One owner per decision

Look things up before you write code:
1. **docs/OWNERS.md.** Find the decision's row. It names the owner (`Type.Member`) that every path calls.
2. **The number's one home.**
   - `RoadConstants.cs` for global values.
   - `RoadRules.cs` for every value a biome may override: the global sits beside its biome overrides, the value is read only through `RoadRules.For`, and the biome is decided by `RoadBiome`.
   - Otherwise, the member the OWNERS row names.
3. **Only then the code.** Check that every path calls that owner, and that the number is read from its home.

Rules:
- **Call the owner.** Never write a second copy of a rule, a check, or its number. If the code and the OWNERS row disagree, fix one of them in the same commit; never add a second row.
- **Shared inputs have one owner too.** A check never reads its own version of:
  - the ground before roads: `RoadNetworkGenerator.GroundBeforeRoads`;
  - a Location's edge: `PlacedLocationShape`;
  - the water lines: `RoadWater`;
  - the biome: `RoadBiome` (the world's biome readers are banned at compile time outside `RoadBiome.At`).
- **Raw height reads are banned at compile time.** Use `RoadNetworkGenerator.GroundBeforeRoads` (a pre-road judge), `RoadWater.Ground` (a water test) or the finished ground (`RoadTerrainModifier.FinishedGround`, `StructureGround.At`). The allowlist is only in `NaturalGround` (`Src/Roads/NaturalGround.cs`, its game-only part `NaturalGround.Live.cs`): one member per allowed input (a Location's own levelling, the search samples, island detection, the terrain writer's input, diagnostics, the live terrain). The banned readers are listed in `ProceduralRoads/BannedSymbols.txt`.
- **The water lines are banned at compile time too.** `RoadConstants.SeaLevel`, `DryRoadFloor`, `ShallowWaterHeight` and `DeepWaterHeight` are read only in `RoadConstants` (their home) and `RoadWater` (their owner). Everywhere else, `RoadRules` included, ask `RoadWater`: `Surface` (where the water stands), `DryFloor` (the floor every road on land is built up to), `ShallowLine` and `DeepLine` (the lines a biome's water values are set from), and the tests `Wet`, `AtOrOverSea`, `BuiltDry`, `AtOrOverDeepLine`, `DepthOver` and `AtLeastDryFloor`. The banned constants are listed in `ProceduralRoads/BannedSymbols.txt` (`F:` entries).
- **A new decision** adds its row to docs/OWNERS.md in the same commit. Name the owner `Type.Member`, never `file:line`.
- **A new number** (a threshold, a limit, a margin) needs the maintainer's approval first. Say why no existing owner covers it.
- **Tools** that judge the mod's output read the mod's owners and values. They never copy a constant or a formula.
- **Tests:**
  - `OwnersDocTests` fails when a listed owner is renamed or deleted.
  - `OwnerLiteralTests` fails when a key constant's number is written outside its owner.
  - `BiomeOwnerTests` fails when a biome is tested outside `RoadBiome` / `RoadRules`.
  - `NaturalGroundTests` fails when a file other than `NaturalGround`'s, the blend (`BiomeBlendedHeight.cs`) and `RoadBiome.cs` switches the raw-reader ban (RS0030) off, or when a `NaturalGround` member gains a caller not listed with its reason in `docs/OWNERS.md` "Raw ground readers" (the one allowlist). It also fails when a water line is read from `RoadConstants` outside `RoadWater`, or when `RoadWater` or `RoadConstants` switches the ban off for anything but the water lines' declarations.
  - A decision made on several paths gets a parity test: one input, every path, the same verdict.

## Floats

- Keep a stored float expression whole. The game's Mono rounds once, at the store, so splitting `a * b + c` into helpers or temporaries can change the result.
- `x >= k` is not `!(x < k)`: they differ when `x` is NaN. Keep each comparison as written.

## Checks

Build with 0 warnings and run the test classes your change touches. The full suite runs at integration.
