using System.Text.Json;

namespace ProceduralRoads.ReviewTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "validate")
        {
            try
            {
                var plan = RoadsReviewPlan.Read(args[1]);
                Console.WriteLine($"Roads review plan ready: world {plan.WorldUid}, {plan.Sites.Count} site(s).");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Roads review plan refused: " + error.Message);
                return 1;
            }
        }
        if (args.Length != 4 || args[0] != "compare")
        {
            Console.Error.WriteLine("Usage: dotnet ProceduralRoads.ReviewTests.dll validate SITE_PLAN.json | compare BASELINE_RUN CANDIDATE_RUN NEW_OUTPUT_DIR");
            return 2;
        }
        try
        {
            string output = Path.GetFullPath(args[3]);
            if (Path.Exists(output)) throw new IOException("Comparison output must be new: " + output);
            var comparison = RoadsReviewComparison.Compare(args[1], args[2]);
            Directory.CreateDirectory(output);
            string file = Path.Combine(output, "comparison.json");
            File.WriteAllText(file, JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine("Comparison: " + file);
            Console.WriteLine("Visual verdict: " + comparison.VisualVerdict);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Roads comparison refused: " + error.Message);
            return 1;
        }
    }
}
