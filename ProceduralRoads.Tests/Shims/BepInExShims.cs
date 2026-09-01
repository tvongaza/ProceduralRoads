// Minimal logging stand-ins so mod sources compile without BepInEx.

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogDebug(object data) => Write("DEBUG", data);
        public void LogInfo(object data) => Write("INFO ", data);
        public void LogWarning(object data) => Write("WARN ", data);
        public void LogError(object data) => Write("ERROR", data);

        private static void Write(string level, object data) =>
            System.Console.WriteLine($"[{level}] {data}");
    }
}

namespace ProceduralRoads
{
    /// <summary>Shim for the plugin class; only the logger is needed.</summary>
    public static class ProceduralRoadsPlugin
    {
        public static BepInEx.Logging.ManualLogSource ProceduralRoadsLogger { get; } = new();
    }
}
