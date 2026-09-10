namespace ProceduralRoads.Study;

/// <summary>
/// Which build the numbers came out of.
///
/// It is here because it turned out to matter more than any planner change in
/// the study: the same generation, on the same inputs, takes 3.3 s under a
/// Release build and 20.5 s under a Debug one. Two runs recorded without it
/// were compared to each other and produced a 21x claim that was a compiler
/// switch.
/// </summary>
internal static class Build
{
#if DEBUG
    public const string Configuration = "Debug";
#else
    public const string Configuration = "Release";
#endif
}
