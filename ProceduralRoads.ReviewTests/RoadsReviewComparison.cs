using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProceduralRoads.ReviewTests;

/// <summary>Checks that two independent one-shot runs used the same inputs before displaying measured differences.</summary>
public static class RoadsReviewComparison
{
    public sealed record SiteReading(string Id, double GroundHeight, string RouteSha256, int RoadPoints, int SavedObjects,
        int LoadedPrefabs, int Pieces, IReadOnlyDictionary<string, int> SavedByType,
        IReadOnlyDictionary<string, int> LoadedByType, string StillSha256);
    public sealed record Arm(string Directory, string SourceCommit, string ModSha256, string FixtureSha256, string PlanSha256,
        string GameSha256, string LoaderSha256, string PatchersSha256, string OtherPluginsSha256,
        string WorldUid, double? GenerationSeconds, double? ServerStartupSeconds, IReadOnlyList<SiteReading> Sites);
    public sealed record SiteDifference(string Id, bool SameRoute, double GroundHeightChange, int RoadPoints, int SavedObjects,
        int LoadedPrefabs, int Pieces, IReadOnlyList<ObjectTypeDifference> ObjectTypes,
        string BaselineStillSha256, string CandidateStillSha256);
    public sealed record ObjectTypeDifference(string Layer, string Type, int Baseline, int Candidate);
    public sealed record Comparison(Arm Baseline, Arm Candidate, double? GenerationSecondsChange,
        double? ServerStartupSecondsChange, IReadOnlyList<SiteDifference> Sites, string VisualVerdict);

    public static Comparison Compare(string baseline, string candidate)
    {
        var before = Read(baseline);
        var after = Read(candidate);
        if (before.ModSha256 == after.ModSha256)
            throw new InvalidDataException("Baseline and candidate use the same Roads build hash; this is not a before/after comparison.");
        if (before.FixtureSha256 != after.FixtureSha256 || before.WorldUid != after.WorldUid ||
            before.PlanSha256 != after.PlanSha256 || before.GameSha256 != after.GameSha256 ||
            before.LoaderSha256 != after.LoaderSha256 || before.PatchersSha256 != after.PatchersSha256 ||
            before.OtherPluginsSha256 != after.OtherPluginsSha256 ||
            !before.Sites.Select(site => site.Id).SequenceEqual(after.Sites.Select(site => site.Id)))
            throw new InvalidDataException("Fixture, game, loader, other plugins, world UID or site plan differ between arms.");
        var sites = before.Sites.Zip(after.Sites, (b, a) => new SiteDifference(b.Id,
            b.RouteSha256 == a.RouteSha256, a.GroundHeight - b.GroundHeight, a.RoadPoints - b.RoadPoints,
            a.SavedObjects - b.SavedObjects, a.LoadedPrefabs - b.LoadedPrefabs, a.Pieces - b.Pieces,
            Differences("saved", b.SavedByType, a.SavedByType)
                .Concat(Differences("loaded", b.LoadedByType, a.LoadedByType)).ToArray(),
            b.StillSha256, a.StillSha256)).ToArray();
        double? Delta(double? a, double? b) => a.HasValue && b.HasValue ? b - a : null;
        return new(before, after, Delta(before.GenerationSeconds, after.GenerationSeconds),
            Delta(before.ServerStartupSeconds, after.ServerStartupSeconds), sites,
            "not assessed: inspect every paired PNG and record a separate human verdict");
    }

    public static Arm Read(string directory)
    {
        directory = Path.GetFullPath(directory);
        string evidence = Path.Combine(directory, "evidence");
        string resultPath = Path.Combine(evidence, "result.json");
        using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
        var root = result.RootElement;
        if (!root.GetProperty("Passed").GetBoolean() ||
            !root.GetProperty("ScenarioPassed").GetBoolean() ||
            !root.GetProperty("CleanupVerified").GetBoolean() ||
            root.GetProperty("Pinning").GetString() != "strict")
            throw new InvalidDataException("Roads comparison needs a passing scenario with verified cleanup and strict pins: " + resultPath);
        var provenance = root.GetProperty("Provenance");
        string Input(string name) => RequiredHash(provenance.GetProperty(name).GetString(), name);
        string sourceCommit = provenance.GetProperty("roadsSourceCommit").GetString() ?? "";
        if (!Regex.IsMatch(sourceCommit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The Roads source commit is missing or malformed.");
        string fixture = Input("roadsFixtureManifestSha256");
        string planHash = Input("roadsReviewPlanSha256");
        string game = Input("roadsGameSha256"), loader = Input("roadsLoaderSha256"), patchers = Input("roadsPatchersSha256");
        string sitePlan = Path.Combine(evidence, "roads-review-sites.json");
        if (Valheim.Testing.Game.FileHash.Sha256(sitePlan) != planHash)
            throw new InvalidDataException("Captured Roads site-plan bytes changed after the run.");
        var plan = RoadsReviewPlan.Read(sitePlan);
        string[] ids = provenance.GetProperty("roadsReviewSiteIds").GetString()!.Split(',');
        if (!ids.SequenceEqual(plan.Sites.Select(site => site.Id)))
            throw new InvalidDataException("Roads site list in the result differs from its captured plan.");
        string inputs = Path.Combine(directory, "adapter-build", "inputs.json");
        using var adapter = JsonDocument.Parse(File.ReadAllText(inputs));
        string mod = RequiredHash(adapter.RootElement.GetProperty("mod").GetProperty("sha256").GetString(), "Roads mod");
        string otherPlugins = OtherPluginsDigest(root.GetProperty("Plugins"));
        var entries = root.GetProperty("Evidence").EnumerateArray().ToArray();
        foreach (string kind in new[] { "roads-site", "area-objects", "review-still" })
            if (entries.Count(item => item.GetProperty("Kind").GetString() == kind) != plan.Sites.Count)
                throw new InvalidDataException("Wrong number of " + kind + " readings for the declared sites.");
        if (entries.Count(item => item.GetProperty("Kind").GetString() == "roads-network") != 1)
            throw new InvalidDataException("The world network evidence is missing or duplicated.");
        var network = Item("roads-network", "world");
        if (network.GetProperty("WorldUid").GetString() != plan.WorldUid)
            throw new InvalidDataException("Network evidence names the wrong world UID.");
        var status = network.GetProperty("Status");
        RoadsReviewScenario.StatusEvidence(status.EnumerateObject().ToDictionary(
            field => field.Name, field => field.Value.GetString() ?? "", StringComparer.Ordinal));
        double? generation = NullableSeconds(network, "GenerationSeconds");
        double? startup = NullableSeconds(network, "ServerStartupSeconds");
        var sites = new List<SiteReading>();
        foreach (var id in ids)
        {
            var site = Item("roads-site", id);
            var area = Item("area-objects", id);
            var still = Item("review-still", id);
            if (site.GetProperty("WorldUid").GetString() != plan.WorldUid ||
                area.GetProperty("WorldUid").GetString() != plan.WorldUid ||
                still.GetProperty("worldUid").GetString() != plan.WorldUid ||
                still.GetProperty("visualVerdict").GetString() != "not asserted")
                throw new InvalidDataException("Site evidence names the wrong world UID: " + id);
            var route = site.GetProperty("Route");
            if (area.GetProperty("SavedObjects").GetInt32() != site.GetProperty("SavedObjects").GetInt32() ||
                area.GetProperty("LoadedPrefabs").GetInt32() != site.GetProperty("LoadedPrefabs").GetInt32() ||
                area.GetProperty("Pieces").GetInt32() != site.GetProperty("Pieces").GetInt32())
                throw new InvalidDataException("Site and area-object counts disagree at " + id + ".");
            string image = Path.Combine(evidence, "review-" + id, id + ".png");
            string imageSha = RequiredHash(still.GetProperty("imageSha256").GetString(), "review image");
            if (Valheim.Testing.Game.FileHash.Sha256(image) != imageSha)
                throw new InvalidDataException("Review image differs from its sidecar at " + id + ".");
            double ground = still.GetProperty("location").GetProperty("y").GetDouble();
            if (!double.IsFinite(ground) || ground < 31.5)
                throw new InvalidDataException("Review still has no dry loaded-ground height at " + id + ".");
            var savedByType = CensusByType(area, "cli_zdos_at ", @"^ZDO (?<type>\S+) ",
                site.GetProperty("SavedObjects").GetInt32());
            var loadedByType = CensusByType(area, "cli_prefabs_at ", @"^PREFAB name=(?<type>\S+) ",
                site.GetProperty("LoadedPrefabs").GetInt32());
            sites.Add(new(id, ground, RequiredHash(route.GetProperty("routeSha256").GetString(), "route"),
                route.GetProperty("points").GetInt32(), site.GetProperty("SavedObjects").GetInt32(),
                site.GetProperty("LoadedPrefabs").GetInt32(), site.GetProperty("Pieces").GetInt32(),
                savedByType, loadedByType, imageSha));
        }
        return new(directory, sourceCommit, mod, fixture, planHash, game, loader, patchers, otherPlugins,
            plan.WorldUid, generation, startup, sites);

        JsonElement Entry(string kind, string site)
        {
            var found = entries.Where(item => item.GetProperty("Kind").GetString() == kind &&
                item.GetProperty("Site").GetString() == site).ToArray();
            if (found.Length != 1) throw new InvalidDataException($"Expected one {kind} reading at {site}; found {found.Length}.");
            return found[0];
        }
        JsonElement Item(string kind, string site)
        {
            var entry = Entry(kind, site);
            string relative = entry.GetProperty("File").GetString() ?? "";
            string full = Path.GetFullPath(Path.Combine(evidence, relative));
            if (Path.IsPathRooted(relative) || !full.StartsWith(evidence + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                Valheim.Testing.Game.FileHash.Sha256(full) != RequiredHash(entry.GetProperty("Sha256").GetString(), kind))
                throw new InvalidDataException("Evidence file is outside the run or changed: " + relative);
            using var document = JsonDocument.Parse(File.ReadAllText(full));
            return document.RootElement.Clone();
        }
    }

    private static double? NullableSeconds(JsonElement item, string name) =>
        item.GetProperty(name).ValueKind == JsonValueKind.Null ? null : item.GetProperty(name).GetDouble();

    private static IReadOnlyDictionary<string, int> CensusByType(JsonElement area, string commandPrefix,
        string linePattern, int expected)
    {
        var commands = area.GetProperty("Commands").EnumerateArray()
            .Where(command => command.GetProperty("Command").GetString()?.StartsWith(commandPrefix, StringComparison.Ordinal) == true)
            .ToArray();
        if (commands.Length != 1) throw new InvalidDataException("Expected one complete " + commandPrefix.Trim() + " census.");
        var rows = commands[0].GetProperty("Reply").EnumerateArray().Select(line => line.GetString() ?? "")
            .Where(line => line.StartsWith(commandPrefix == "cli_zdos_at " ? "ZDO " : "PREFAB ", StringComparison.Ordinal))
            .ToArray();
        if (rows.Length != expected) throw new InvalidDataException("Object census rows disagree with its recorded total.");
        var regex = new Regex(linePattern, RegexOptions.CultureInvariant);
        if (rows.Any(line => !regex.IsMatch(line))) throw new InvalidDataException("Object census has an unrecognized type row.");
        return rows.GroupBy(line => regex.Match(line).Groups["type"].Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static IEnumerable<ObjectTypeDifference> Differences(string layer, IReadOnlyDictionary<string, int> baseline,
        IReadOnlyDictionary<string, int> candidate) => baseline.Keys.Concat(candidate.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new ObjectTypeDifference(layer, name,
                baseline.GetValueOrDefault(name), candidate.GetValueOrDefault(name)));

    private static string RequiredHash(string? value, string label) =>
        Regex.IsMatch(value ?? "", "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) ? value! :
        throw new InvalidDataException("Missing or malformed SHA-256 for " + label + ".");

    private static string OtherPluginsDigest(JsonElement actors)
    {
        var lines = new List<string>();
        foreach (var actor in actors.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            foreach (var pin in actor.Value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                if (pin.Name != "warpalicious.ProceduralRoads") lines.Add(actor.Name + "/" + pin.Name + "=" + pin.Value.GetString());
        if (lines.Count == 0) throw new InvalidDataException("No other plugin pins were recorded.");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines)))).ToLowerInvariant();
    }
}
