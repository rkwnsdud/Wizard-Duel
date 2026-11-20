using System.Collections.Generic;
using UnityEngine;

public static class EnvironmentDestruction
{
    public static IEnumerable<IEnvironmentDestructible> AllBlocks()
    {
        foreach (ProceduralMap map in Object.FindObjectsByType<ProceduralMap>(FindObjectsSortMode.None))
            foreach (IEnvironmentDestructible block in map.Destructibles) yield return block;
        foreach (GridMap map in Object.FindObjectsByType<GridMap>(FindObjectsSortMode.None))
            foreach (IEnvironmentDestructible block in map.Destructibles) yield return block;
    }

    public static bool Apply(int x, int z, bool burning)
    {
        foreach (ProceduralMap map in Object.FindObjectsByType<ProceduralMap>(FindObjectsSortMode.None))
            if (map.ApplyEnvironmentDestruction(x, z, burning)) return true;
        foreach (GridMap map in Object.FindObjectsByType<GridMap>(FindObjectsSortMode.None))
            if (map.ApplyEnvironmentDestruction(x, z, burning)) return true;
        return false;
    }
}
