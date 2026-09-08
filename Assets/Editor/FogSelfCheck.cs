using UnityEditor;
using UnityEngine;
using FlatSpace.Fog;

public static class FogSelfCheck
{
    [MenuItem("FlatSpace/Fog/Run Self-Check")]
    public static void Run()
    {
        var ok = RunFogGridChecks();
        Debug.Log(ok ? "[FogSelfCheck] ALL PASSED" : "[FogSelfCheck] FAILURES (see errors above)");
    }

    static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[FogSelfCheck] FAIL: {label}");
        return condition;
    }

    public static bool RunFogGridChecks()
    {
        var ok = true;

        // Bounds from two planets 400 apart, cellSize 25, margin 50.
        var raw = new Bounds();
        raw.SetMinMax(new Vector3(0, 0, 0), new Vector3(400, 200, 0));
        var grid = new FogGrid(raw, cellSize: 25f, margin: 50f);

        ok &= Check(grid.Cols == Mathf.CeilToInt(500f / 25f), "Cols = ceil((400+2*50)/25) = 20");
        ok &= Check(grid.Rows == Mathf.CeilToInt(300f / 25f), "Rows = ceil((200+2*50)/25) = 12");
        ok &= Check(grid.CellCount == grid.Cols * grid.Rows, "CellCount = Cols*Rows");
        ok &= Check(grid.Origin == new Vector2(-50f, -50f), "Origin = rawMin - margin");

        // Round trip: a world point maps to a cell whose center is within one cell of it.
        var p = new Vector2(123f, 77f);
        var idx = grid.WorldToCellIndex(p);
        var c = grid.CellCenter(idx);
        ok &= Check(Vector2.Distance(p, c) <= 25f, "WorldToCellIndex/CellCenter round trip within one cell");

        // Clamping: far outside maps to a valid in-range index.
        var farIdx = grid.WorldToCellIndex(new Vector2(99999f, -99999f));
        ok &= Check(farIdx >= 0 && farIdx < grid.CellCount, "out-of-range world clamps to a valid index");

        // Emptiness: a cell on a planet is populated (openness ~0); a cell far from
        // everything with threshold below its distance is open (openness ~1).
        var planets = new[] { new Vector2(0, 0), new Vector2(400, 0) };
        var segments = new (Vector2, Vector2)[] { (new Vector2(0, 0), new Vector2(400, 0)) };
        grid.BakeEmptiness(planets, segments, threshold: 60f, falloff: 60f);

        var onPlanet = grid.Openness(grid.WorldToCellIndex(new Vector2(0, 0)));
        ok &= Check(onPlanet < 0.01f, $"openness on a planet ~0 (was {onPlanet})");

        var inVoid = grid.Openness(grid.WorldToCellIndex(new Vector2(200, 190)));
        ok &= Check(inVoid > 0.99f, $"openness far from planet/segment ~1 (was {inVoid})");

        // A cell right on the segment (midpoint) is not open.
        var onSegment = grid.Openness(grid.WorldToCellIndex(new Vector2(200, 0)));
        ok &= Check(onSegment < 0.01f, $"openness on a connection segment ~0 (was {onSegment})");

        return ok;
    }
}
