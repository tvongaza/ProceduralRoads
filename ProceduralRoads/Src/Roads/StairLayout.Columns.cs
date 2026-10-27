using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Columns under pieces not sunk into the ground: a pillar column's whole base SupportBury under the lowest ground at its corners.
/// </summary>
public static partial class StairLayout
{
    /// <summary>The lowest pillar ends at least this far into the ground.</summary>
    public const float SupportBury = 0.25f;

    /// <summary>Half the width of a support column (stone and black marble are 1 m square).</summary>
    public const float SupportHalfWidth = 0.5f;
}
