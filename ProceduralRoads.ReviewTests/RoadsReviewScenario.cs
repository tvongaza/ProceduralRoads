using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Valheim.Testing.Game;
using Valheim.Testing.GameSessions;

namespace ProceduralRoads.ReviewTests;

/// <summary>One Roads-owned observation pass; valheim-test owns actors, profile, pins and teardown.</summary>
public sealed class RoadsReviewScenario : IOneShotServerScenario
{
    public string Name => "roads-review-sites";
    public bool RequiresClient => true;

    public async Task RunAsync(GameSession session, OneShotServerContext context)
    {
        string path = Environment.GetEnvironmentVariable("ROADS_REVIEW_PLAN")
            ?? throw new InvalidDataException("Set ROADS_REVIEW_PLAN to a Roads site-plan JSON file before server-load.");
        string pinnedPlan = Path.Combine(session.Output, "roads-review-sites.json");
        if (File.Exists(pinnedPlan)) throw new IOException("This run already contains a Roads site plan.");
        File.Copy(path, pinnedPlan);
        if (FileHash.Sha256(path) != FileHash.Sha256(pinnedPlan))
            throw new InvalidDataException("Roads site plan changed while it was being captured.");
        var plan = RoadsReviewPlan.Read(pinnedPlan);
        var clientPlan = context.ClientPlan ?? throw new InvalidDataException("Roads review needs the prepared clean client; omit --server-only.");
        var server = session.Server?.Game ?? throw new InvalidOperationException("Roads review needs the prepared dedicated server.");
        if (!session.CampaignClients.Contains("client"))
            throw new InvalidDataException("Roads review needs the prepared 'client' actor in the one-shot campaign.");
        IGameHost captureHost = session.ClientHost("client");
        if (captureHost.Kind != GameHostKind.Local)
            throw new NotSupportedException("Roads review currently needs the one-shot client on the runner's machine for screenshot fetch.");
        if (plan.WorldUid != context.WorldUid || !context.ServerPlan.Pinned || !clientPlan.Pinned)
            throw new InvalidDataException("The site plan's world UID and both actors' strict pins must match the prepared fixture.");
        var shell = OperatingSystem.IsWindows() ? HostShell.WindowsPowerShell : HostShell.Bash;
        var shots = plan.Sites.Select(site => Shot(plan, site, session.Output, context, clientPlan)).ToArray();
        foreach (var shot in shots) ReviewCapture.Validate(shot, shell.Kind);
        session.Report.Provenance["roadsReviewPlanSha256"] = FileHash.Sha256(pinnedPlan);
        session.Report.Provenance["roadsReviewPlan"] = pinnedPlan;
        session.Report.Provenance["roadsReviewSiteIds"] = string.Join(",", plan.Sites.Select(s => s.Id));
        session.Report.Provenance["roadsFixtureManifestSha256"] = FixtureManifestDigest(context.ServerPlan.World.Sha256);
        var runtime = context.ServerPlan.RuntimePins ?? throw new InvalidDataException("The server runtime is not pinned.");
        session.Report.Provenance["roadsGameSha256"] = runtime.Game;
        session.Report.Provenance["roadsLoaderSha256"] = runtime.Loader;
        session.Report.Provenance["roadsPatchersSha256"] = runtime.Patchers;
        session.Report.Provenance["roadsReviewVisualVerdict"] = "required from a person; screenshots are evidence only";

        var networkCapability = server.RequireCapability("roads.testing/network");
        Observation network = null!;
        session.Report.Step("Roads network complete and fixture ready", () =>
        {
            network = server.ObserveComplete(networkCapability, "roads-memory");
            if (network.Data.GetProperty("points").GetInt32() <= 0 || network.Data.GetProperty("cells").GetInt32() <= 0 ||
                network.Data.GetProperty("pendingZones").GetInt32() != 0)
                throw new InvalidDataException("Roads' completed network has no routes or still has pending bridge append zones.");
        });
        bool loaded = network.Data.GetProperty("loadedFromSave").GetBoolean();
        if (plan.RequireFreshGeneration && loaded)
            throw new InvalidDataException("The review plan requires fresh road generation, but the network loaded from a save.");
        // The kept boot logs are written at teardown. Read the live loader log while the server is still running.
        string? generationLine = loaded ? null : await GenerationLineAsync(session.Server!, session.Cancellation);
        if (!loaded && generationLine == null)
            throw new InvalidDataException("Roads generated a network without a complete generation timing line.");
        double? generationSeconds = ParseGenerationSeconds(generationLine);
        double? serverStartupSeconds = session.Report.Steps
            .LastOrDefault(step => step.Name == "start and verify owned dedicated fixture")?.Seconds;
        session.Report.Attach(new RoadsNetworkEvidence(context.WorldUid, network.Data, generationLine,
            generationSeconds, serverStartupSeconds, DateTimeOffset.UtcNow, plan.RequireFreshGeneration));

        var siteCapability = server.RequireCapability("roads.testing/site-metrics");
        new ClientRounds
        {
            Client = clientPlan, WorldUid = context.WorldUid, Report = session.Report, Output = session.Output,
            OwnedServer = session.Server!, Rounds = ["review"], Cancellation = session.Cancellation,
        }.Run(server, () => session.OpenClient(clientPlan, "client"), round =>
        {
            for (int index = 0; index < plan.Sites.Count; index++)
            {
                var site = plan.Sites[index];
                var target = new HeightExpectation(site.X, site.Z, site.Height);
                // A road can move the loaded ground between builds. Arrive against the client's measured
                // ground first; ReviewCapture currently owns its own second arrival and waits until a new hop is safe.
                round.Step(site.Id + ": protected arrival on loaded ground", () =>
                {
                    PlayerPlacement.Protect(round.Client);
                    target = PlayerPlacement.Arrive(round.Server, round.Client, target,
                        TimeSpan.FromSeconds(clientPlan.ArrivalSeconds), session.Cancellation, loadedGround: true).Target;
                });
                round.Step(site.Id + ": reproducible still", () =>
                    session.Report.Attach(ReviewCapture.Capture(round.Server, round.Client, captureHost,
                        shots[index] with { Arrival = target }, TimeSpan.FromSeconds(clientPlan.ArrivalSeconds),
                        TimeSpan.FromSeconds(60), session.Cancellation).Evidence));
                round.Step(site.Id + ": three stationary support readings", () =>
                    PlayerPlacement.RequireSupported(round.Client, target, cancellation: session.Cancellation));
                round.Step(site.Id + ": complete route and object census", () =>
                {
                    var route = round.Server.ObserveComplete(siteCapability, "roads-memory",
                        site.X.ToString("R", CultureInfo.InvariantCulture), site.Z.ToString("R", CultureInfo.InvariantCulture),
                        site.Radius.ToString(CultureInfo.InvariantCulture));
                    RoadsSiteEvidence.RequireRoute(route, site);
                    var objects = AreaObjectSnapshot.Capture(round.Server, round.Client, site.Id, context.WorldUid,
                        (int)MathF.Round(site.X), (int)MathF.Round(site.Z), site.Radius,
                        TimeSpan.FromSeconds(90), includeSupport: true, cancellation: session.Cancellation);
                    session.Report.Attach(objects);
                    session.Report.Attach(new RoadsSiteEvidence(site.Id, context.WorldUid, route.Data,
                        objects.SavedObjects, objects.LoadedPrefabs, objects.Pieces, DateTimeOffset.UtcNow));
                });
            }
        });
    }

    private static ReviewCapturePlan Shot(RoadsReviewPlan plan, RoadsReviewSite site, string output,
        OneShotServerContext context, ClientRunPlan client) => new(site.Id,
        new HeightExpectation(site.X, site.Z, site.Height), plan.Weather, plan.TimeOfDay,
        plan.CameraDistance, plan.CameraHeight, Path.Combine(output, "capture-host-" + site.Id),
        Path.Combine(output, "review-" + site.Id), context.WorldUid,
        context.ServerPlan.RuntimePins?.Game ?? throw new InvalidDataException("Server game build is not pinned."),
        client.Pins, CameraAzimuthDegrees: plan.CameraAzimuthDegrees);

    private static readonly Regex Generation = new(@"Roads done: .* in (?<seconds>\d+(?:\.\d+)?)s\.", RegexOptions.CultureInvariant);

    public static async Task<string?> GenerationLineAsync(ServerActor server, CancellationToken cancellation)
    {
        char separator = server.Host?.Shell.Kind == HostShellKind.PowerShell ? '\\' : '/';
        string log = server.RuntimeDirectory.TrimEnd('/', '\\') + separator + "BepInEx" + separator + "LogOutput.log";
        if (server.Host is { } host)
            return (await host.WaitForLogAsync(log, 0, Generation, null, TimeSpan.FromSeconds(10), cancellation)).EnsureMatched().Trim();
        if (!File.Exists(log)) return null;
        return File.ReadLines(log).LastOrDefault(Generation.IsMatch)?.Trim();
    }

    public static double? ParseGenerationSeconds(string? line)
    {
        if (line == null) return null;
        var match = Generation.Match(line);
        if (!match.Success || !double.TryParse(match.Groups["seconds"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Roads generation log has no complete non-negative duration.");
        return seconds;
    }

    public static string FixtureManifestDigest(IReadOnlyDictionary<string, string> files)
    {
        if (files.Count == 0) throw new InvalidDataException("The fixture has no pinned files.");
        string canonical = string.Join("\n", files.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key.Replace('\\', '/') + "=" + pair.Value.ToLowerInvariant()));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record RoadsNetworkEvidence(string WorldUid, JsonElement Network, string? GenerationLogLine,
    double? GenerationSeconds, double? ServerStartupSeconds, DateTimeOffset CapturedUtc,
    bool FreshGenerationRequired) : IEvidence
{
    public string Kind => "roads-network";
    public string Site => "world";
}

public sealed record RoadsSiteEvidence(string Site, string WorldUid, JsonElement Route,
    int SavedObjects, int LoadedPrefabs, int Pieces, DateTimeOffset CapturedUtc) : IEvidence
{
    public string Kind => "roads-site";

    public static void RequireRoute(Observation route, RoadsReviewSite site)
    {
        route.RequireComplete("roads-memory");
        var data = route.Data;
        if (data.GetProperty("x").GetSingle() != site.X || data.GetProperty("z").GetSingle() != site.Z ||
            data.GetProperty("radius").GetSingle() != site.Radius || data.GetProperty("points").GetInt32() <= 0 ||
            data.GetProperty("nearestMeters").GetSingle() < 0 ||
            !Regex.IsMatch(data.GetProperty("routeSha256").GetString() ?? "", "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Road route reading is missing, empty, or for the wrong site.");
    }
}
