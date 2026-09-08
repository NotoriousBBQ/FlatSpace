using UnityEditor;
using UnityEngine;
using FlatSpace.Fog;

public static class FogSelfCheck
{
    [MenuItem("FlatSpace/Fog/Run Self-Check")]
    public static void Run()
    {
        var ok = RunFogGridChecks();
        ok &= RunVisibilityGridChecks();
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

    public static bool RunVisibilityGridChecks()
    {
        var ok = true;
        var g = new FlatSpace.Fog.VisibilityGrid(10);

        g.Observe(3, 0.9f, exploredCutoff: 0.5f);
        g.Observe(3, 0.2f, exploredCutoff: 0.5f); // must not lower it
        ok &= Check(Mathf.Approximately(g.VisibleStrength[3], 0.9f), "Observe keeps the max strength");
        ok &= Check(g.IsExplored(3), "strength above cutoff marks explored");

        g.Observe(4, 0.3f, exploredCutoff: 0.5f);
        ok &= Check(!g.IsExplored(4), "strength below cutoff does not mark explored");

        g.ClearVisible();
        ok &= Check(Mathf.Approximately(g.VisibleStrength[3], 0f), "ClearVisible zeroes strength");
        ok &= Check(g.IsExplored(3), "ClearVisible keeps explored history");

        // Pack / unpack round trip.
        g.Observe(0, 1f, 0.5f);
        g.Observe(9, 1f, 0.5f);
        var packed = g.GetExploredPacked();
        var g2 = new FlatSpace.Fog.VisibilityGrid(10);
        g2.SetExploredPacked(packed);
        ok &= Check(g2.IsExplored(0) && g2.IsExplored(3) && g2.IsExplored(9), "unpack restores explored bits");
        ok &= Check(g2.ExploredCount() == g.ExploredCount(), "explored count survives round trip");

        // Wrong-length data is ignored.
        var g3 = new FlatSpace.Fog.VisibilityGrid(10);
        g3.SetExploredPacked(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        ok &= Check(g3.ExploredCount() == 0, "mismatched packed length is ignored");

        return ok;
    }
}
