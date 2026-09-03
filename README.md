# ProceduralRoads

A Valheim mod that generates procedural roads connecting locations across your world.

## Features

- Automatically generates roads from spawn to nearby points of interest
- Terrain-aware pathfinding that follows natural contours
- Configurable road width, length, and count

## Installation

1. Install [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
2. Install [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)
3. Drop `ProceduralRoads.dll` into `BepInEx/plugins/`

## Configuration

Edit `warpalicious.ProceduralRoads.cfg` in `BepInEx/config/`:

| Setting | Default | Description |
|---------|---------|-------------|
| RoadWidth | 4 | Road width in meters (2-10) |
| IslandRoadPercentage | 50 | Percentage of islands that will have roads (0-100). Largest islands selected first. |
| CustomLocations | (empty) | Comma-separated list of location names to include in road generation |

### Custom Locations via Config

Use the `CustomLocations` setting to add locations from other mods (e.g., Expand World Data):

```
CustomLocations = Runestone_Boars,Runestone_Greydwarfs,MerchantCamp
```

## API for Mod Authors

Other mods can register locations for road generation programmatically.

### Direct Reference (if embedding or referencing the DLL)

```csharp
using ProceduralRoads;

// Register a location
RoadNetworkGenerator.RegisterLocation("MyCustomLocation");

// Unregister if needed
RoadNetworkGenerator.UnregisterLocation("MyCustomLocation");

// Get all registered locations
IReadOnlyCollection<string> locations = RoadNetworkGenerator.GetRegisteredLocations();
```

### Reflection (soft dependency, no DLL reference required)

```csharp
private static void RegisterRoadLocation(string locationName)
{
    var assembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "ProceduralRoads");

    if (assembly == null) return;

    var generatorType = assembly.GetType("ProceduralRoads.RoadNetworkGenerator");
    var method = generatorType?.GetMethod("RegisterLocation", 
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

    method?.Invoke(null, new object[] { locationName });
}
```

### Available API Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `RegisterLocation` | `void RegisterLocation(string locationName)` | Add a location to road generation |
| `UnregisterLocation` | `void UnregisterLocation(string locationName)` | Remove a location from road generation |
| `GetRegisteredLocations` | `IReadOnlyCollection<string> GetRegisteredLocations()` | Get all registered location names |

### Notes

- Register locations during mod initialization (Awake/Start)
- Location names must match the prefab name exactly (e.g., `Runestone_Boars`, not `Runestone Boars`)
- Both API registrations and config entries are merged at generation time

## License

MIT License - see LICENSE.md

## Validation tooling

The mod can judge its own output. With `[Debug] DebugValidation = true` it
runs `RoadNetworkValidator` after generation and writes
`ProceduralRoads.selftest.json` (pass/fail, route count, network components,
ford count, the uncapped wet-point / crossing-length / grade totals, a hash
of every centerline point, and the violations) plus
`ProceduralRoads.routes.csv` (every route point) to the config folder. The
same run is available on demand from the console as `road_selftest`.
`[Debug] ForceRegenerate = true` ignores roads persisted in the world and
regenerates from scratch on every load, so a fixture world with pre-placed
locations gives the same `pointsHash` on every station. The effective
config is logged once at start as a `[CONFIG]` line: read it first when a
hash moves.

Console commands (cheats on): `road_selftest`, `road_routes`,
`road_route_nearest`, `road_route_export`, `road_debug_locations` (rings
around every connected location), `road_snap_probe [prefab]` (snap points
and collider bounds of build pieces).

Scripts (`scripts/`): `server-validate.sh` runs a dedicated server on a
fixture world and greps the `[SELFTEST]` line; `nas-validate.sh` does the
same on a remote host over ssh; `ingame-validate.sh` drives a local client;
`world-fixture.sh` saves and restores a pristine copy of a fixture world
(paint and leveling apply only when a zone first generates, so every run
starts from the untouched world). `ci/validate.yml` is a GitHub Actions
job that does the server run.
