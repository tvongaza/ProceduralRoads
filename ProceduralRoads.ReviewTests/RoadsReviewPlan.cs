using System.Text.Json;
using System.Text.Json.Serialization;
using Valheim.Testing.Game;

namespace ProceduralRoads.ReviewTests;

/// <summary>Roads-owned site and camera choices. The toolkit owns the game, fixture, pins and cleanup.</summary>
public sealed class RoadsReviewPlan
{
    public string WorldUid { get; set; } = "";
    public List<RoadsReviewSite> Sites { get; set; } = [];
    public string Weather { get; set; } = "Clear";
    public float TimeOfDay { get; set; } = .45f;
    public float CameraDistance { get; set; } = 24f;
    public float CameraHeight { get; set; } = 12f;
    public float CameraAzimuthDegrees { get; set; } = 225f;
    public bool RequireFreshGeneration { get; set; } = true;

    public static RoadsReviewPlan Read(string path)
        => Parse(File.ReadAllBytes(path));

    public static RoadsReviewPlan Parse(ReadOnlySpan<byte> contents)
    {
        var plan = JsonSerializer.Deserialize<RoadsReviewPlan>(contents,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Empty Roads review plan.");
        plan.Validate();
        return plan;
    }

    public void Validate()
    {
        if (!long.TryParse(WorldUid, out _) || Sites is null || Sites.Count is < 1 or > 12 || Sites.Any(site => site is null))
            throw new InvalidDataException("Declare a numeric world UID and 1-12 sites.");
        if (Sites.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Sites.Count)
            throw new InvalidDataException("Roads review site IDs must be distinct even on a case-insensitive host.");
        foreach (var site in Sites) site.Validate();
        var shot = new Valheim.Testing.GameSessions.ReviewCapturePlan("plan-check",
            new HeightExpectation(Sites[0].X, Sites[0].Z, Sites[0].Height), Weather, TimeOfDay,
            CameraDistance, CameraHeight, OperatingSystem.IsWindows() ? @"C:\roads-review-plan" : "/roads-review-plan",
            Path.Combine(Path.GetTempPath(), "roads-review-plan-check"), WorldUid,
            new string('0', 64), new Dictionary<string, string> { ["roads"] = "plan-check" },
            CameraAzimuthDegrees: CameraAzimuthDegrees);
        Valheim.Testing.GameSessions.ReviewCapture.Validate(shot,
            OperatingSystem.IsWindows() ? Valheim.Testing.GameSessions.HostShellKind.PowerShell : Valheim.Testing.GameSessions.HostShellKind.Bash);
    }
}

public sealed class RoadsReviewSite
{
    public string Id { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float Height { get; set; }
    public int Radius { get; set; } = 32;
    public float MaxRoadDistanceMeters { get; set; } = 5;

    public void Validate()
    {
        if (Id is null || Id.Length is < 1 or > 40 || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ||
            !float.IsFinite(X) || !float.IsFinite(Z) || !float.IsFinite(Height) ||
            Math.Abs(X) > 20000 || Math.Abs(Z) > 20000 || Height < 31.5f || Radius is < 1 or > 64 ||
            !float.IsFinite(MaxRoadDistanceMeters) || MaxRoadDistanceMeters <= 0 || MaxRoadDistanceMeters > Radius)
            throw new InvalidDataException("Each site needs a short alphanumeric ID, finite dry arrival, world coordinates, 1-64 m radius and positive on-road distance within that radius.");
    }
}
