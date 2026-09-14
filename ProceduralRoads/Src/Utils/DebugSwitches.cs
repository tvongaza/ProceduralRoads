using System;
using BepInEx.Logging;

namespace ProceduralRoads;

/// <summary>
/// Switches that exist for development and validation rather than for play.
///
/// They are read from the environment rather than bound into the config file.
/// A config key cannot be taken back once it has been written to a user's
/// disk: renaming it, moving it or removing it leaves the old key sitting
/// there, and a setting that only ever mattered to whoever was debugging the
/// mod is not worth that. An environment variable costs the user nothing,
/// appears nowhere, and disappears when it is no longer set.
///
/// Every switch is named PROCEDURALROADS_&lt;NAME&gt; and defaults to the
/// behaviour the mod has without it, so an unset environment is ordinary play.
/// </summary>
internal static class DebugSwitches
{
    private const string Prefix = "PROCEDURALROADS_";

    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    /// <summary>
    /// A boolean switch. "1", "true", "yes" and "on" turn it on; "0", "false",
    /// "no" and "off" turn it off; anything else is reported and ignored, so a
    /// typo does not silently change how the mod behaves.
    /// </summary>
    internal static bool Flag(string name, bool fallback)
    {
        string variable = Prefix + name;
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        switch (value.Trim().ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "on":
                Log.LogInfo($"{variable}=on");
                return true;
            case "0": case "false": case "no": case "off":
                Log.LogInfo($"{variable}=off");
                return false;
            default:
                Log.LogWarning($"{variable} is '{value}', which is neither on nor off; using {fallback}");
                return fallback;
        }
    }

    /// <summary>
    /// A counting switch, for validation that has to make something happen a
    /// fixed number of times -- proving a failure path in game needs a way to
    /// cause exactly that many failures. Unset means the fallback, and a value
    /// that is not a count is reported and ignored rather than silently
    /// changing what the mod does.
    /// </summary>
    internal static int Count(string name, int fallback)
    {
        string variable = Prefix + name;
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (int.TryParse(value.Trim(), out int parsed) && parsed >= 0)
        {
            Log.LogInfo($"{variable}={parsed}");
            return parsed;
        }
        Log.LogWarning($"{variable} is '{value}', which is not a count; using {fallback}");
        return fallback;
    }

    /// <summary>
    /// A zone switch, "x,z" as zone indices. Anything else is reported and
    /// treated as unset, so a typo names no zone rather than a wrong one.
    /// </summary>
    internal static bool Zone(string name, out int x, out int z)
    {
        x = 0; z = 0;
        string variable = Prefix + name;
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string[] parts = value.Trim().Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out z))
        {
            Log.LogInfo($"{variable}={x},{z}");
            return true;
        }
        Log.LogWarning($"{variable} is '{value}', which is not a zone \"x,z\"; ignored");
        x = 0; z = 0;
        return false;
    }
}
