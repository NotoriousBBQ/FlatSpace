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
        ok &= RunFogSystemChecks();
        ok &= RunPlanetTintCheck();
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

    public static bool RunFogSystemChecks()
    {
        var ok = true;
        var go = new GameObject("FogSelfCheckSystem");
        try
        {
            var sys = go.AddComponent<FlatSpace.Fog.FogOfWarSystem>();
            var settings = ScriptableObject.CreateInstance<FlatSpace.Fog.FogOfWarSettings>();
            settings.cellSize = 25f;
            settings.boundsMargin = 100f;
            settings.planetVisionRadius = 120f;
            settings.edgeSoftness = 20f;
            settings.visibleCutoff = 0.5f;
            settings.visibleThreshold = 0.35f;
            settings.openSpaceThreshold = 80f;
            settings.openSpaceFalloff = 80f;
            settings.openSpaceRadiusMultiplier = 2f;

            var positions = new[] { new Vector2(0, 0), new Vector2(600, 0) };
            var segments = new (Vector2, Vector2)[0];
            sys.InitForTest(positions, segments, settings, numPlayers: 2);

            sys.RecomputeFromSources(new[]
            {
                (0, new Vector2(0, 0), settings.planetVisionRadius),
                (1, new Vector2(600, 0), settings.planetVisionRadius),
            });

            sys.SetViewMode(FlatSpace.Fog.FogViewMode.Player, 0);
            ok &= Check(sys.Classify(new Vector2(0, 0)) == FlatSpace.Fog.FogVisibility.Visible,
                "player 0 view: on planet A is Visible");
            ok &= Check(sys.Classify(new Vector2(600, 0)) == FlatSpace.Fog.FogVisibility.Hidden,
                "player 0 view: planet B is Hidden");

            sys.SetViewMode(FlatSpace.Fog.FogViewMode.AllPlayers, 0);
            ok &= Check(sys.Classify(new Vector2(600, 0)) == FlatSpace.Fog.FogVisibility.Visible,
                "all-players view: planet B is Visible");

            sys.SetViewMode(FlatSpace.Fog.FogViewMode.NoFog, 0);
            ok &= Check(sys.Classify(new Vector2(9999, 9999)) == FlatSpace.Fog.FogVisibility.Visible,
                "no-fog view: everything Visible");

            sys.SetViewMode(FlatSpace.Fog.FogViewMode.Player, 0);
            sys.RecomputeFromSources(new (int, Vector2, float)[0]);
            ok &= Check(sys.Classify(new Vector2(0, 0)) == FlatSpace.Fog.FogVisibility.Explored,
                "player 0 view: planet A drops to Explored once vision leaves");

            // Re-init with FEWER players while a Player-n view is selected must not throw (regression: OOB in ResolveDisplay).
            sys.SetViewMode(FlatSpace.Fog.FogViewMode.Player, 1);
            var threw = false;
            try { sys.InitForTest(new[] { new Vector2(0, 0) }, new (Vector2, Vector2)[0], settings, numPlayers: 1); }
            catch { threw = true; }
            ok &= Check(!threw, "re-Init with fewer players (Player-1 view active) does not throw");
            ok &= Check(sys.Classify(new Vector2(0, 0)) == FlatSpace.Fog.FogVisibility.Visible,
                "after re-Init the view resets to NoFog (everything Visible)");

            Object.DestroyImmediate(settings);
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }

    public static bool RunPlanetTintCheck()
    {
        var ok = true;
        var go = new GameObject("FogTintCheckPlanet");
        try
        {
            var child = new GameObject("sprite");
            child.transform.SetParent(go.transform, false);
            var sr = child.AddComponent<SpriteRenderer>();
            sr.color = new Color(0.8f, 0.4f, 0.2f, 1f);
            var pui = go.AddComponent<PlanetUIObject>();

            const float dim = 0.45f;
            pui.SetFogState(FlatSpace.Fog.FogVisibility.Visible, dim);
            pui.SetFogState(FlatSpace.Fog.FogVisibility.Explored, dim);
            pui.SetFogState(FlatSpace.Fog.FogVisibility.Visible, dim);
            pui.SetFogState(FlatSpace.Fog.FogVisibility.Explored, dim);

            var expected = new Color(0.8f, 0.4f, 0.2f, 1f) * new Color(dim, dim, dim, 1f);
            var got = go.GetComponentInChildren<SpriteRenderer>().color;
            ok &= Check(Mathf.Abs(got.r - expected.r) < 0.001f && Mathf.Abs(got.g - expected.g) < 0.001f
                        && Mathf.Abs(got.b - expected.b) < 0.001f,
                $"planet tint does not decay over Visible/Explored cycles (got {got}, expected {expected})");

            pui.SetFogState(FlatSpace.Fog.FogVisibility.Visible, dim);
            var back = go.GetComponentInChildren<SpriteRenderer>().color;
            ok &= Check(Mathf.Abs(back.r - 0.8f) < 0.001f && Mathf.Abs(back.g - 0.4f) < 0.001f
                        && Mathf.Abs(back.b - 0.2f) < 0.001f,
                $"planet returns to full brightness when Visible again (got {back})");
        }
        finally { Object.DestroyImmediate(go); }
        return ok;
    }
}
