using System.Text.Json;
using ProceduralRoads.ReviewTests;
using Valheim.Testing.Game;
using Xunit;

namespace ProceduralRoads.ReviewTests.Tests;

public sealed class ReviewPlanTests
{
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
    public void Comparison_refuses_changed_fixture_and_missing_still_but_accepts_measured_differences()
    {
        string root = Path.Combine(Path.GetTempPath(), "roads-review-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseline = FakeRun(Path.Combine(root, "baseline"), new string('a', 64), new string('1', 64), 10);
            string candidate = FakeRun(Path.Combine(root, "candidate"), new string('b', 64), new string('1', 64), 12);
            var comparison = RoadsReviewComparison.Compare(baseline, candidate);
            Assert.Equal(2, Assert.Single(comparison.Sites).RoadPoints);
            Assert.Equal(0, Assert.Single(comparison.Sites).GroundHeightChange);
            Assert.Contains(Assert.Single(comparison.Sites).ObjectTypes,
                type => type is { Layer: "saved", Type: "Rock_4", Baseline: 8, Candidate: 8 });
            Assert.Equal("not assessed: inspect every paired PNG and record a separate human verdict", comparison.VisualVerdict);
            string changed = FakeRun(Path.Combine(root, "changed-fixture"), new string('c', 64), new string('2', 64), 12);
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, changed));
            string noCleanup = FakeRun(Path.Combine(root, "no-cleanup"), new string('d', 64), new string('1', 64), 12, cleanup: false);
            Assert.Throws<InvalidDataException>(() => RoadsReviewComparison.Compare(baseline, noCleanup));
            File.Delete(Path.Combine(candidate, "evidence", "review-hill", "hill.png"));
            Assert.ThrowsAny<Exception>(() => RoadsReviewComparison.Compare(baseline, candidate));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string FakeRun(string root, string mod, string fixture, int points, bool cleanup = true)
    {
        string output = Path.Combine(root, "evidence");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(root, "adapter-build"));
        File.WriteAllText(Path.Combine(root, "adapter-build", "inputs.json"),
            JsonSerializer.Serialize(new { mod = new { sha256 = mod } }));
        string planFile = Path.Combine(output, "roads-review-sites.json");
        File.WriteAllText(planFile, JsonSerializer.Serialize(new RoadsReviewPlan { WorldUid = "123", Sites =
        [new() { Id = "hill", X = 10, Z = 20, Height = 35 }] }));
        var report = new ScenarioReport("roads-review-sites");
        report.Provenance["roadsReviewPlanSha256"] = FileHash.Sha256(planFile);
        report.Provenance["roadsSourceCommit"] = mod[0] == 'a' ? new string('a', 40) : new string('b', 40);
        report.Provenance["roadsReviewSiteIds"] = "hill";
        report.Provenance["roadsFixtureManifestSha256"] = fixture;
        report.Provenance["roadsGameSha256"] = new string('3', 64);
        report.Provenance["roadsLoaderSha256"] = new string('4', 64);
        report.Provenance["roadsPatchersSha256"] = new string('5', 64);
        report.RecordPlugins("server", new Dictionary<string, string>
        { ["warpalicious.ProceduralRoads"] = new string('a', 32), ["valheimCLI.valheimCLI"] = new string('b', 32) });
        report.Step("reviewed", () => { });
        if (cleanup) report.Step(StepPhase.Cleanup, "owned environment retired", () => { });
        var route = JsonSerializer.SerializeToElement(new { source = "roads-memory", complete = true,
            x = 10, z = 20, radius = 32, points, routeSha256 = new string('c', 64) });
        report.Attach(new RoadsNetworkEvidence("123", JsonSerializer.SerializeToElement(new { points = 100 }),
            "Roads done: 1 road(s), in 20s.", 20, 25, DateTimeOffset.UtcNow, true));
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
            imageSha256 = FileHash.Sha256(png) }));
        report.Attach(new EvidenceReference("review-still", "hill", "123", sidecar, FileHash.Sha256(sidecar)));
        report.Write(output);
        return root;
    }
}
