using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProceduralRoads.Study;

/// <summary>
/// The generator's own answers to "is this a road location", "what is it
/// worth" and "how many may this island have", reached by reflection so the
/// study reports what the mod decides rather than a paraphrase of it that
/// could drift.
/// </summary>
internal static class Eligible
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly MethodInfo IsRoadLocationMethod =
        typeof(RoadNetworkGenerator).GetMethod("IsRoadLocation", Private)
        ?? throw new MissingMethodException("RoadNetworkGenerator.IsRoadLocation");

    private static readonly MethodInfo PriorityMethod =
        typeof(RoadNetworkGenerator).GetMethod("GetLocationPriority", Private)
        ?? throw new MissingMethodException("RoadNetworkGenerator.GetLocationPriority");

    private static readonly MethodInfo MaxLocationsMethod =
        typeof(RoadNetworkGenerator).GetMethod("GetMaxLocationsForIsland", Private)
        ?? throw new MissingMethodException("RoadNetworkGenerator.GetMaxLocationsForIsland");

    private static readonly FieldInfo BossNamesField =
        typeof(RoadNetworkGenerator).GetField("BossLocationNames", Private)
        ?? throw new MissingFieldException("RoadNetworkGenerator.BossLocationNames");

    public static bool IsRoadLocation(string name) =>
        (bool)IsRoadLocationMethod.Invoke(null, new object[] { name })!;

    public static int Priority(string name) =>
        (int)PriorityMethod.Invoke(null, new object[] { name })!;

    public static int MaxLocationsFor(Island island) =>
        (int)MaxLocationsMethod.Invoke(null, new object[] { island })!;

    public static bool IsBoss(string name) =>
        ((HashSet<string>)BossNamesField.GetValue(null)!).Contains(name);
}
