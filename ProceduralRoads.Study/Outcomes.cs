using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralRoads.Tests;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// One row per place in the world, and what became of it.
///
/// The trail is kept in separate columns on purpose - placed, on an island,
/// eligible, selected, attempted, connected - because merging any two of them
/// hides a different finding. A place that lost its island's quota, a place
/// that was never eligible, and a place the pathfinder could not reach are
/// three different answers to "why is there no road here".
/// </summary>
internal static class Outcomes
{
    /// <summary>An attempt endpoint this close to a place is that place.</summary>
    public const float EndpointRadius = 32f;

    public static int Run(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine(
                "usage: roads-study outcomes <island-grid.csv> <locations.csv> <selection.csv> <attempts.csv> [--out FILE]");
            return 2;
        }

        string gridPath = args[1];
        string locationsPath = args[2];
        string selectionPath = args[3];
        string attemptsPath = args[4];
        string outPath = Options.Value(args, "--out") ?? "outcomes.csv";

        CsvWorld world = new CsvWorld().LoadIslandGrid(gridPath);
        WorldGenerator.instance = world;
        List<Program.Location> places = Program.ReadLocations(locationsPath);
        List<Island> islands = IslandDetector.DetectIslands();

        Dictionary<(string, int, int), Selection> selections = ReadSelection(selectionPath);
        List<Attempt> attempts = ReadAttempts(attemptsPath);

        int placed = places.Count, onIsland = 0, eligible = 0, selected = 0, attempted = 0, connected = 0;

        using StreamWriter writer = new(outPath);
        writer.WriteLine("name,x,z,radius,priority,island_id,eligible,selected,attempts,connected,outcome,closest_approach_m");

        foreach (Program.Location place in places)
        {
            Island? island = islands.FirstOrDefault(i => i.ContainsPoint(place.Position));
            bool isEligible = island != null && Eligible.IsRoadLocation(place.Name);
            selections.TryGetValue(Key(place), out Selection? selection);
            List<Attempt> mine = attempts.Where(a => a.Touches(place)).ToList();
            bool isConnected = mine.Any(a => a.Connected);

            if (island != null) onIsland++;
            if (isEligible) eligible++;
            if (selection is { Selected: true }) selected++;
            if (mine.Count > 0) attempted++;
            if (isConnected) connected++;

            string outcome =
                island == null ? "not on a detected island"
                : !isEligible ? "not a road location"
                : selection == null ? "island never reached selection"
                : !selection.Selected ? "lost the island's quota"
                : mine.Count == 0 ? "selected but never attempted"
                : isConnected ? "connected"
                : mine[0].Outcome;

            float closest = mine.Count > 0 ? mine.Min(a => a.ClosestApproach) : float.NaN;

            writer.WriteLine(string.Join(",",
                "\"" + place.Name.Replace("\"", "\"\"") + "\"",
                F(place.Position.x), F(place.Position.z), F(place.Radius),
                Eligible.Priority(place.Name),
                island?.Id.ToString(CultureInfo.InvariantCulture) ?? "",
                isEligible ? "true" : "false",
                selection is { Selected: true } ? "true" : "false",
                mine.Count,
                isConnected ? "true" : "false",
                "\"" + outcome + "\"",
                float.IsNaN(closest) ? "" : F(closest)));
        }

        Console.WriteLine($"placed:     {placed}");
        Console.WriteLine($"on island:  {onIsland}");
        Console.WriteLine($"eligible:   {eligible}");
        Console.WriteLine($"selected:   {selected}");
        Console.WriteLine($"attempted:  {attempted}");
        Console.WriteLine($"connected:  {connected}");
        Console.WriteLine($"wrote:      {outPath}");
        return 0;
    }

    private static string F(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static (string, int, int) Key(Program.Location place) =>
        (place.Name, Mathf.RoundToInt(place.Position.x), Mathf.RoundToInt(place.Position.z));

    internal sealed class Selection
    {
        public bool Selected;
    }

    private static Dictionary<(string, int, int), Selection> ReadSelection(string path)
    {
        Dictionary<(string, int, int), Selection> selections = new();
        foreach (string[] cells in Csv.Rows(path, out string[] columns))
        {
            int name = Array.IndexOf(columns, "name");
            int x = Array.IndexOf(columns, "x");
            int z = Array.IndexOf(columns, "z");
            int selected = Array.IndexOf(columns, "selected");
            (string, int, int) key = (
                cells[name].Trim('"'),
                Mathf.RoundToInt(Csv.Number(cells[x])),
                Mathf.RoundToInt(Csv.Number(cells[z])));
            selections[key] = new Selection { Selected = cells[selected].Trim() == "true" };
        }

        return selections;
    }

    internal sealed class Attempt
    {
        public Vector2 Start, End;
        public bool Connected;
        public string Outcome = "";
        public float ClosestApproach;

        public bool Touches(Program.Location place)
        {
            float reach = Mathf.Max(EndpointRadius, place.Radius);
            Vector2 at = new(place.Position.x, place.Position.z);
            return Vector2.Distance(Start, at) <= reach || Vector2.Distance(End, at) <= reach;
        }
    }

    private static List<Attempt> ReadAttempts(string path)
    {
        List<Attempt> attempts = new();
        foreach (string[] cells in Csv.Rows(path, out string[] columns))
        {
            int sx = Array.IndexOf(columns, "start_x"), sz = Array.IndexOf(columns, "start_z");
            int ex = Array.IndexOf(columns, "end_x"), ez = Array.IndexOf(columns, "end_z");
            int connected = Array.IndexOf(columns, "connected");
            int outcome = Array.IndexOf(columns, "outcome");
            int closest = Array.IndexOf(columns, "closest_approach");
            attempts.Add(new Attempt
            {
                Start = new Vector2(Csv.Number(cells[sx]), Csv.Number(cells[sz])),
                End = new Vector2(Csv.Number(cells[ex]), Csv.Number(cells[ez])),
                Connected = cells[connected].Trim() == "true",
                Outcome = cells[outcome].Trim('"'),
                ClosestApproach = Csv.Number(cells[closest]),
            });
        }

        return attempts;
    }
}

internal static class Csv
{
    public static IEnumerable<string[]> Rows(string path, out string[] columns)
    {
        using StreamReader reader = new(path);
        string? header = reader.ReadLine() ?? throw new InvalidDataException($"{path}: empty");
        columns = Split(header);
        List<string[]> rows = new();
        string? line;
        while ((line = reader.ReadLine()) != null)
            if (line.Length > 0)
                rows.Add(Split(line));
        return rows;
    }

    public static float Number(string cell) =>
        float.Parse(cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string[] Split(string line)
    {
        List<string> cells = new();
        bool quoted = false;
        System.Text.StringBuilder cell = new();
        foreach (char c in line)
        {
            if (c == '"') { quoted = !quoted; cell.Append(c); }
            else if (c == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells.ToArray();
    }
}
