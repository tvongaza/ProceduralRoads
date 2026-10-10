using ProceduralRoads.SystemTests;
using Valheim.Testing.Game;
using Valheim.Testing.GameSessions;

// The toolkit's pinned dedicated-server runner (PinnedServerRun) with the Roads plan, modes and scenarios. The runner
// owns the lifecycle: plan checks, fixture copies, provenance, the owned session and its startup events, teardown,
// the report and the result banner.
string mode = Mode(args, out int modeIndex);
if (mode is "prepare-bridge" or "prepare-terrain")
{
    args = (string[])args.Clone();
    args[modeIndex] = "run"; // The toolkit owns the same lifecycle; this mode authors a fixture, not acceptance.
}
int result = await PinnedServerRun.MainAsync(args, new PinnedServerRunOptions<RunPlan>
{
    Name = "roads-system-test",
    ReadPlan = path => { var plan = RunPlan.Read(path); plan.Validate(); return plan; },
    Mod = new ModDeclaration(RunPlan.SessionCapability, RunPlan.SessionTokenVariable),
    CheckPlan = plan => RunPlan.CheckMode(mode, plan),
    Provenance = (plan, provenance) =>
    {
        if (mode is "prepare-bridge" or "prepare-terrain") provenance["purpose"] = "fixture-preparation-not-acceptance";
        if (plan.Scenario == "terrain-persistence") provenance["paintProfile"] = PersistentTerrainScenario.ProfileName(PersistentTerrainScenario.WidthFor(plan));
        if (plan.Client != null) provenance["clientMode"] = plan.Client.Mode;
    },
    Scenario = (session, plan) => mode switch
    {
        "prepare-terrain" => Sync(() => PersistentTerrainScenario.Prepare(session.Server!.Game, session.Report, session.Output, PersistentTerrainScenario.WidthFor(plan))),
        "prepare-bridge" => RoadsScenarios.PrepareBridgeZones(session.Server!.Game, plan.Expected, session.Report, session.Cancellation),
        _ => plan.Scenario switch
        {
            "terrain-calibration" => Sync(() => TerrainCalibrationScenario.Run(session.Server!.Game, session.Report, session.Output)),
            "terrain-persistence" => Sync(() => TerrainClientScenario.Run(plan, session.Server!.Game, session.Server,
                () => session.OpenClient(plan.Client!), session.Report, session.Output, session.Cancellation)),
            "empty-save" => RoadsScenarios.EmptyNetworkReplacesOld(session.Server!.Game, () => session.Server.Game.Execute("road_generate"), session.Server.Restart, session.Report, cancellation: session.Cancellation),
            "metadata-prefab" => Sync(() => RoadsScenarios.MetadataPrefabRetainsView(session.Server!.Game, session.Report)),
            _ => RoadsScenarios.BridgeAppendSurvivesRespawn(session.Server!.Game, () => session.Server.Game.Execute(plan.Append), session.Server.Restart, plan.Expected, session.Report, session.Cancellation),
        },
    },
});

if (result == 0 && mode is "prepare-bridge" or "prepare-terrain")
    Console.WriteLine("PREPARED (fixture authoring only; not a Roads acceptance result)");
return result;

static Task Sync(Action scenario) { scenario(); return Task.CompletedTask; }

static string Mode(string[] args, out int index)
{
    index = 0;
    while (index < args.Length)
    {
        if (args[index] == "--inventory") { index += 2; continue; }
        if (args[index] == "--in-place") { index++; continue; }
        break;
    }
    return index < args.Length ? args[index] : "";
}
