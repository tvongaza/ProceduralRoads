# ProceduralRoads

A Valheim mod that generates procedural roads connecting locations across your world.

## Features

- Automatically generates roads from spawn to nearby points of interest
- Terrain-aware pathfinding that follows natural contours
- Configurable road width, length, and count
- Server-side: players without the mod see and walk the same roads

Road statistics count **stored point records**, including records copied into several spatial cells, repeated path commits and levelled bridge supports. **Committed planned-path length** sums successful planned paths, including overlap and manual appends; it excludes unplanned bridge-support collections and is not unique road distance. New saves preserve this length. Nonempty legacy road data cannot recover it reliably, so reports show **unknown (legacy save)** instead of zero. The Roads data envelope is now v4; it reads v1–v3, but older Roads readers cannot read newly written v4 data. Keep an older world backup if you intend to downgrade. This does not change road geometry or its network hash.

## Installation

1. Install [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
2. Install [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)
3. Drop `ProceduralRoads.dll` into `BepInEx/plugins/`

On a dedicated server, install it on the server; players do not need it
(see [Servers and players without the mod](#servers-and-players-without-the-mod)).
Players who do install it are let in whatever version they run: only the server
writes the roads.

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


## Development tests

Start with the [test-framework setup and migration guide](ProceduralRoads.SystemTests/README.md#quick-start-and-migration). Existing Roads unit tests remain here; synthetic terrain and external system-test infrastructure use [ValheimTesting](https://github.com/tvongaza/ValheimTesting). The optional adapter, test packages and ValheimCLI are not required by a normal Roads installation. Native test scenarios use disposable fixtures, separately from the fast unit suite. Coding agents working on the test tooling should start with [AGENTS.md](AGENTS.md).
