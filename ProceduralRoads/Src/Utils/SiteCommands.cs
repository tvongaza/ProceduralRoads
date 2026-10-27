namespace ProceduralRoads;

/// <summary>
/// Console commands for the places placed before their roads and the approaches laid in play:
/// road_sites_dump and road_placed_sites_dump (for offline tools) and road_site_approaches.
/// Registered only with PROCEDURALROADS_DEV_COMMANDS on (<see cref="ConsoleCommands.DevCommands"/>);
/// off by default. Read-only.
/// </summary>
public static class SiteCommands
{
    public static void Register()
    {
        new Terminal.ConsoleCommand(
            "road_sites_dump",
            "Write every location prefab's levelling (radius, height, smoothing) to BepInEx/config/ProceduralRoads.sites.csv, for offline tools. Read-only.",
            args =>
            {
                if (ZoneSystem.instance == null) { args.Context.AddString("Error: no world loaded"); return; }
                string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "ProceduralRoads.sites.csv");
                string csv = LocationPrefabLevelling.DescribeCsv();
                System.IO.File.WriteAllText(path, csv);
                args.Context.AddString($"Wrote {csv.Split('\n').Length - 2} row(s) to {path}");
            }, isCheat: true);

        new Terminal.ConsoleCommand(
            "road_placed_sites_dump",
            "Write each place placed this session for its approach (turn, root, levelling, rocks) to BepInEx/config/ProceduralRoads.placed_sites.csv, for offline tools. Read-only.",
            args =>
            {
                string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "ProceduralRoads.placed_sites.csv");
                System.IO.File.WriteAllText(path, PlacedSites.Write(PlacedSites.All));
                args.Context.AddString($"Wrote {PlacedSites.Count} placed site(s) to {path}");
            }, isCheat: true);

        new Terminal.ConsoleCommand("road_site_approaches",
            "Approach staircases to places: how many are still owed, what became of the others, and where the time went (count, mean and worst, ms) this session. Read-only.",
            args =>
            {
                foreach (string line in SiteApproachStats.Describe(SiteApproachQueue.Count, SiteApproachRunner.Busy))
                    args.Context.AddString(line);
            }, isCheat: true);
    }
}
