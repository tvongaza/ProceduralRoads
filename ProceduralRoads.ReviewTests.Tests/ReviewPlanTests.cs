using System.Text.Json;
using System.Text.Json.Nodes;
using ProceduralRoads.ReviewTests;
using Valheim.Testing.Game;
using Xunit;

namespace ProceduralRoads.ReviewTests.Tests;

public sealed class ReviewPlanTests
{
    [Fact]
    public void Scenario_preflight_refuses_missing_commit_and_holds_the_exact_plan_bytes()
    {
        string? originalPlan = Environment.GetEnvironmentVariable("ROADS_REVIEW_PLAN");
        string? originalCommit = Environment.GetEnvironmentVariable("ROADS_REVIEW_SOURCE_COMMIT");
        string file = Path.Combine(Path.GetTempPath(), "roads-preflight-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new RoadsReviewPlan { WorldUid = "123", Sites =
                [new() { Id = "hill", X = 10, Z = 20, Height = 35 }] });
            File.WriteAllBytes(file, bytes);
            Environment.SetEnvironmentVariable("ROADS_REVIEW_PLAN", file);
            Environment.SetEnvironmentVariable("ROADS_REVIEW_SOURCE_COMMIT", null);
            var scenario = new RoadsReviewScenario();
            Assert.Contains("ROADS_REVIEW_SOURCE_COMMIT", Assert.Throws<InvalidDataException>(scenario.ValidateInputs).Message);

            Environment.SetEnvironmentVariable("ROADS_REVIEW_SOURCE_COMMIT", new string('a', 40));
            object prepared = scenario.ValidateInputs();
            File.Delete(file);
            Assert.Equal(bytes, Assert.IsType<byte[]>(prepared.GetType().GetProperty("PlanBytes")!.GetValue(prepared)));
            Assert.Equal(new string('a', 40), prepared.GetType().GetProperty("SourceCommit")!.GetValue(prepared));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROADS_REVIEW_PLAN", originalPlan);
            Environment.SetEnvironmentVariable("ROADS_REVIEW_SOURCE_COMMIT", originalCommit);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void A_wet_or_duplicated_site_is_refused_before_any_visit()
    {
        var plan = new RoadsReviewPlan { WorldUid = "123", Sites =
        [
            new() { Id = "hill", X = 12, Z = 20, Height = 31.4f },
        ] };
        Assert.Throws<InvalidDataException>(plan.Validate);
        plan.Sites[0].Height = 35;
        plan.Sites.Add(new RoadsReviewSite { Id = "HILL", X = 30, Z = 40, Height = 35 });
        Assert.Throws<InvalidDataException>(plan.Validate);
    }

    [Fact]
    public void Status_evidence_requires_a_responsive_game_thread()
    {
        Assert.Throws<InvalidDataException>(() => RoadsReviewScenario.StatusEvidence(new Dictionary<string, string>()));
        Assert.Throws<InvalidDataException>(() => RoadsReviewScenario.StatusEvidence(new Dictionary<string, string>
        { ["mainThreadIdleMs"] = "5000" }));
        Assert.Equal("75", RoadsReviewScenario.StatusEvidence(new Dictionary<string, string>
        { ["mainThreadIdleMs"] = "75" })["mainThreadIdleMs"]);
    }

    [Fact]
    public void A_site_with_points_nearby_but_not_on_the_road_is_refused()
    {
        var site = new RoadsReviewSite { Id = "road", X = 10, Z = 20, Height = 35 };
        Observation Reading(float nearest) => new("roads-memory", true, JsonSerializer.SerializeToElement(new
        { x = 10f, z = 20f, radius = 32f, points = 3, nearestMeters = nearest, routeSha256 = new string('a', 64) }));
        Assert.Throws<InvalidDataException>(() => RoadsSiteEvidence.RequireRoute(Reading(6), site));
        RoadsSiteEvidence.RequireRoute(Reading(4), site);
    }

    [Fact]
    public void One_shot_build_inputs_retain_the_mod_and_linked_observer_sources()
    {
        string root = Path.Combine(Path.GetTempPath(), "roads-review-inputs-" + Guid.NewGuid().ToString("N"));
        try
        {
            string projectDir = Path.Combine(root, "ProceduralRoads.TestAdapter");
            string fixtureDir = Path.Combine(root, "ProceduralRoads.TestFixtures");
            string roadsDir = Path.Combine(root, "ProceduralRoads");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(fixtureDir);
            Directory.CreateDirectory(roadsDir);
            string project = Path.Combine(projectDir, "ProceduralRoads.TestAdapter.csproj");
            File.WriteAllText(project, "<Project><Import Project=\"../ProceduralRoads/environment.props\" /><ItemGroup><Compile Include=\"../ProceduralRoads.TestFixtures/FlatRoadFixture.cs\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(projectDir, "Observer.cs"), "internal class Observer { }");
            string linked = Path.Combine(fixtureDir, "FlatRoadFixture.cs");
            File.WriteAllText(linked, "internal class Fixture { }");
            File.WriteAllText(Path.Combine(roadsDir, "environment.props"), "<Project />");
            string mod = Path.Combine(roadsDir, "ProceduralRoads.dll");
            File.WriteAllBytes(mod, [1, 2, 3]);
            string run = Path.Combine(root, "run");
            string build = Path.Combine(run, "adapter-build");
            Directory.CreateDirectory(build);
            File.WriteAllText(Path.Combine(build, "inputs.json"), JsonSerializer.Serialize(new
            { project, mod = new { file = mod, sha256 = FileHash.Sha256(mod) } }));
            string observerSha = RoadsReviewComparison.CaptureObserverSource(run);
            string modSha = RoadsReviewComparison.CaptureModInput(run);
            Assert.True(File.Exists(Path.Combine(build, "source", "_external", "parent", "ProceduralRoads.TestFixtures", "FlatRoadFixture.cs")));
            Assert.True(File.Exists(Path.Combine(build, "source", "_external", "parent", "ProceduralRoads", "environment.props")));
            File.AppendAllText(linked, "// later edit");
            File.WriteAllBytes(mod, [4, 5, 6]);
            Assert.Equal(observerSha, RoadsReviewComparison.CaptureObserverSource(run));
            Assert.Equal(modSha, RoadsReviewComparison.CaptureModInput(run));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Comparison_refuses_changed_fixture_and_missing_still_but_accepts_measured_differences()
    {
        string root = Path.Combine(Path.GetTempPath(), "roads-review-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseline = FakeRun(Path.Combine(root, "baseline"), (byte)'a', new string('1', 64), 10);
            string candidate = FakeRun(Path.Combine(root, "candidate"), (byte)'b', new string('1', 64), 12);
            var comparison = RoadsReviewComparison.Compare(baseline, candidate);
            Assert.Equal(2, Assert.Single(comparison.Sites).RoadPoints);
            Assert.NotEqual(comparison.Baseline.AdapterSha256, comparison.Candidate.AdapterSha256);
            Assert.Equal(0, Assert.Single(comparison.Sites).GroundHeightChange);
            Assert.Contains(Assert.Single(comparison.Sites).ObjectTypes,
                type => type is { Layer: "saved", Type: "Rock_4", Baseline: 8, Candidate: 8 });
            Assert.Equal("not assessed: inspect every paired PNG and record a separate human verdict", comparison.VisualVerdict);
            // Rewriting otherwise identical JSON must not change the toolkit identity.
            MutateResult(candidate, _ => { });
            Assert.Equal(2, Assert.Single(RoadsReviewComparison.Compare(baseline, candidate).Sites).RoadPoints);
            string changed = FakeRun(Path.Combine(root, "changed-fixture"), (byte)'c', new string('2', 64), 12);
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changed));
            string noCleanup = FakeRun(Path.Combine(root, "no-cleanup"), (byte)'d', new string('1', 64), 12, cleanup: false);
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, noCleanup));
            string changedClient = FakeRun(Path.Combine(root, "changed-client"), (byte)'e', new string('1', 64), 12);
            MutateResult(changedClient, result => result["Provenance"]!["roadsClientLoaderSha256"] = new string('f', 64));
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changedClient));
            string changedScenario = FakeRun(Path.Combine(root, "changed-scenario"), (byte)'f', new string('1', 64), 12);
            MutateResult(changedScenario, result => result["Provenance"]!["oneShotScenarioSha256"] = new string('e', 64));
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changedScenario));
            string missingModPin = FakeRun(Path.Combine(root, "missing-mod-pin"), (byte)'g', new string('1', 64), 12);
            MutateResult(missingModPin, result => result["Plugins"]!["server"]!["warpalicious.ProceduralRoads"] = "absent");
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, missingModPin));
            string changedObserver = FakeRun(Path.Combine(root, "changed-observer"), (byte)'h', new string('1', 64), 12);
            File.AppendAllText(Path.Combine(changedObserver, "adapter-build", "source", "Observer.cs"), "// changed");
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changedObserver));
            MutateResult(changedObserver, result => result["Provenance"]!["roadsObserverSourceSha256"] =
                RoadsReviewComparison.ObserverSourceDigest(Path.Combine(changedObserver, "adapter-build", "source")));
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changedObserver));
            string changedToolkit = FakeRun(Path.Combine(root, "changed-toolkit"), (byte)'i', new string('1', 64), 12);
            MutateResult(changedToolkit, result => result["Toolkit"]!["RunnerSha256"] = new string('f', 64));
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changedToolkit));
            string changedStillGame = FakeRun(Path.Combine(root, "changed-still-game"), (byte)'j', new string('1', 64), 12);
            string sidecar = Path.Combine(changedStillGame, "evidence", "review-hill", "hill.json");
            var metadata = JsonNode.Parse(File.ReadAllText(sidecar))!;
            metadata["gameBuild"] = new string('f', 64);
            File.WriteAllText(sidecar, metadata.ToJsonString());
            MutateResult(changedStillGame, result =>
            {
                var still = result["Evidence"]!.AsArray().Single(entry => entry!["Kind"]!.GetValue<string>() == "review-still");
                still!["Sha256"] = FileHash.Sha256(sidecar);
            });
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changedStillGame));
            File.Delete(Path.Combine(candidate, "evidence", "review-hill", "hill.png"));
            Assert.ThrowsAny<Exception>(() => RoadsReviewComparison.Compare(baseline, candidate));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void MutateResult(string run, Action<JsonNode> change)
    {
        string path = Path.Combine(run, "evidence", "result.json");
        var result = JsonNode.Parse(File.ReadAllText(path))!;
        change(result);
        File.WriteAllText(path, result.ToJsonString());
    }

    private static string FakeRun(string root, byte modByte, string fixture, int points, bool cleanup = true)
    {
        string output = Path.Combine(root, "evidence");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(root, "adapter-build"));
        string adapterDir = Path.Combine(root, "adapter-build", "bin");
        Directory.CreateDirectory(adapterDir);
        string modFile = Path.Combine(adapterDir, "ProceduralRoads.dll");
        File.WriteAllBytes(modFile, [modByte]);
        string mod = FileHash.Sha256(modFile);
        string modMd5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(modFile))).ToLowerInvariant();
        string adapterFile = Path.Combine(adapterDir, "ProceduralRoads.TestAdapter.dll");
        File.WriteAllBytes(adapterFile, [modByte]);
        string adapterSha = FileHash.Sha256(adapterFile);
        string adapterMd5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(adapterFile))).ToLowerInvariant();
        string source = Path.Combine(root, "adapter-build", "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "ProceduralRoads.TestAdapter.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(source, "Observer.cs"), "internal class Observer { }");
        string observerSource = RoadsReviewComparison.ObserverSourceDigest(source);
        File.WriteAllText(Path.Combine(root, "adapter-build", "inputs.json"),
            JsonSerializer.Serialize(new { project = Path.Combine(source, "ProceduralRoads.TestAdapter.csproj"),
                mod = new { file = modFile, sha256 = mod }, adapter = new { sha256 = adapterSha } }));
        string planFile = Path.Combine(output, "roads-review-sites.json");
        File.WriteAllText(planFile, JsonSerializer.Serialize(new RoadsReviewPlan { WorldUid = "123", Sites =
        [new() { Id = "hill", X = 10, Z = 20, Height = 35 }] }));
        var report = new ScenarioReport("roads-review-sites");
        report.Provenance["roadsReviewPlanSha256"] = FileHash.Sha256(planFile);
        report.Provenance["roadsObserverSourceSha256"] = observerSource;
        report.Provenance["roadsModSha256"] = mod;
        report.Provenance["roadsSourceCommit"] = modByte == (byte)'a' ? new string('a', 40) : new string('b', 40);
        report.Provenance["roadsReviewSiteIds"] = "hill";
        report.Provenance["roadsFixtureManifestSha256"] = fixture;
        report.Provenance["roadsGameSha256"] = new string('3', 64);
        report.Provenance["roadsLoaderSha256"] = new string('4', 64);
        report.Provenance["roadsPatchersSha256"] = new string('5', 64);
        report.Provenance["roadsClientGameSha256"] = new string('6', 64);
        report.Provenance["roadsClientLoaderSha256"] = new string('7', 64);
        report.Provenance["roadsClientPatchersSha256"] = new string('8', 64);
        report.Provenance["oneShotScenarioSha256"] = new string('9', 64);
        report.RecordPlugins("server", new Dictionary<string, string>
        { ["warpalicious.ProceduralRoads"] = modMd5,
          ["testing.proceduralroads.adapter"] = adapterMd5,
          ["valheimCLI.valheimCLI"] = new string('b', 32) });
        report.RecordPlugins("client", new Dictionary<string, string>
        { ["warpalicious.ProceduralRoads"] = "absent", ["valheimCLI.valheimCLI"] = new string('b', 32) });
        report.Step("reviewed", () => { });
        if (cleanup) report.Step(StepPhase.Cleanup, "owned environment retired", () => { });
        var route = JsonSerializer.SerializeToElement(new { source = "roads-memory", complete = true,
            x = 10, z = 20, radius = 32, points, routeSha256 = new string('c', 64) });
        report.Attach(new RoadsNetworkEvidence("123", JsonSerializer.SerializeToElement(new { points = 100 }),
            "Roads done: 1 road(s), in 20s.", 20, 25, new Dictionary<string, string>
            { ["mainThreadIdleMs"] = "75", ["busy"] = "none" }, DateTimeOffset.UtcNow, true));
        report.Attach(new RoadsSiteEvidence("hill", "123", route, 8, 6, 2, DateTimeOffset.UtcNow));
        report.Attach(new AreaObjectSnapshot("hill", "123", 10, 20, 32, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 8, -1, 6, 2,
            [new ObservedCommand("server", "cli_zdos_at 10 20 32", DateTimeOffset.UtcNow,
                Enumerable.Repeat("ZDO Rock_4 id=1:2 pos=10.000,35.000,20.000", 8).ToArray()),
             new ObservedCommand("client", "cli_prefabs_at 10 35 20 32", DateTimeOffset.UtcNow,
                Enumerable.Repeat("PREFAB name=PineTree distance=1.0 pos=10.000,35.000,20.000", 6).ToArray())]));
        string review = Path.Combine(output, "review-hill");
        Directory.CreateDirectory(review);
        string png = Path.Combine(review, "hill.png");
        File.WriteAllBytes(png, [1, 2, 3]);
        string sidecar = Path.Combine(review, "hill.json");
        File.WriteAllText(sidecar, JsonSerializer.Serialize(new { worldUid = "123", visualVerdict = "not asserted",
            location = new { x = 10, y = 35.0, z = 20 },
            gameBuild = new string('6', 64),
            imageSha256 = FileHash.Sha256(png) }));
        report.Attach(new EvidenceReference("review-still", "hill", "123", sidecar, FileHash.Sha256(sidecar)));
        report.Write(output);
        return root;
    }
}
