using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

public static partial class RoadNetworkGenerator
{
    public static bool IsGenerating => RoadLifecycleManager.IsGenerating;
}
