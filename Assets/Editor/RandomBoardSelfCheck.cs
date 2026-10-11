using System.IO;
using System.Linq;
using FlatSpace.Tools;
using UnityEditor;
using UnityEngine;

public static class RandomBoardSelfCheck
{
    private static int _checkCount;

    [MenuItem("FlatSpace/Board/Run Random Board Self-Check")]
    public static void Run()
    {
        _checkCount = 0;
        var ok = RunRangeCheck();
        ok &= RunGenerateCheck();
        ok &= RunFileCheck();
        Debug.Log(ok
            ? $"[RandomBoardSelfCheck] ALL PASSED ({_checkCount} assertions ran)"
            : $"[RandomBoardSelfCheck] FAILURES (see errors above; {_checkCount} assertions ran)");
    }

    private static bool Check(bool condition, string label)
    {
        _checkCount++;
        if (!condition) Debug.LogError($"[RandomBoardSelfCheck] FAIL: {label}");
        return condition;
    }

    private static MapGenSettings MakeTemplate()
    {
        var settings = ScriptableObject.CreateInstance<MapGenSettings>();
        foreach (var type in new[]
                 {
                     Planet.PlanetType.PlanetTypeNormal, Planet.PlanetType.PlanetTypeFarm,
                     Planet.PlanetType.PlanetTypeVerdant, Planet.PlanetType.PlanetTypeIndustrial,
                     Planet.PlanetType.PlanetTypeDesolate, Planet.PlanetType.PlanetTypeOcean,
                     Planet.PlanetType.PlanetTypeDesert
                 })
            settings.typeWeights.Add(new TypeWeight { type = type, weight = 1f });
        return settings;
    }

    public static bool RunRangeCheck()
    {
        var ok = true;
        var max = RandomBoardBuilder.MaxPlayers;
        ok &= Check(max >= 2, "max players is at least the minimum");
        ok &= Check(RandomBoardBuilder.ClampPlayers(0) == 2, "players below range clamp to 2");
        ok &= Check(RandomBoardBuilder.ClampPlayers(3) == 3, "players in range are kept");
        ok &= Check(RandomBoardBuilder.ClampPlayers(max + 7) == max, "players above range clamp to the max");
        ok &= Check(RandomBoardBuilder.ClampPlanets(-5) == 10, "planets below range clamp to 10");
        ok &= Check(RandomBoardBuilder.ClampPlanets(80) == 80, "planets in range are kept");
        ok &= Check(RandomBoardBuilder.ClampPlanets(9999) == 400, "planets above range clamp to 400");
        ok &= Check(RandomBoardBuilder.ParseInt("12", 5) == 12, "parse reads a number");
        ok &= Check(RandomBoardBuilder.ParseInt("", 5) == 5, "parse of blank gives the fallback");
        ok &= Check(RandomBoardBuilder.ParseInt("abc", 5) == 5, "parse of junk gives the fallback");
        ok &= Check(RandomBoardBuilder.ParseInt("99999999999", 5) == int.MaxValue, "parse clamps past int range");
        ok &= Check(RandomBoardBuilder.ResolveSeed(0) > 0 && RandomBoardBuilder.ResolveSeed(-3) > 0,
            "a zero or negative seed resolves to a positive random one");
        ok &= Check(RandomBoardBuilder.ResolveSeed(1234) == 1234, "a typed seed is kept");
        return ok;
    }

    public static bool RunGenerateCheck()
    {
        var ok = true;
        var template = MakeTemplate();
        try
        {
            var a = RandomBoardBuilder.Generate(template, 3, 30, 4242);
            var b = RandomBoardBuilder.Generate(template, 3, 30, 4242);
            ok &= Check(a.Success && b.Success, $"generation succeeds ({a.Error} {b.Error})");
            if (!(a.Success && b.Success)) return false;

            ok &= Check(a.Planets.Count == 30, "planet count matches the request");
            ok &= Check(a.Planets.Count(p => p.Type == Planet.PlanetType.PlanetTypePrime) == 3,
                "one Prime per player");
            ok &= Check(template.playerCount == 2 && template.totalPlanetCount == 20 && template.seed == 0,
                "the template asset is not modified");

            var saveA = RandomBoardBuilder.ToBoardSave(a);
            var saveB = RandomBoardBuilder.ToBoardSave(b);
            ok &= Check(JsonUtility.ToJson(saveA) == JsonUtility.ToJson(saveB), "the same seed gives the same board");
            ok &= Check(saveA.planetEntries.Count == 30, "the save holds every planet");

            var names = saveA.planetEntries.Select(e => e.name).ToHashSet();
            ok &= Check(names.Count == 30, "planet names are unique");
            ok &= Check(saveA.planetEntries.All(e => e.connections != null && e.connections.All(names.Contains)),
                "every connection names a planet on the board");

            var different = RandomBoardBuilder.Generate(template, 3, 30, 4243);
            ok &= Check(different.Success && JsonUtility.ToJson(RandomBoardBuilder.ToBoardSave(different)) != JsonUtility.ToJson(saveA),
                "a different seed gives a different board");

            var noTemplate = RandomBoardBuilder.Generate(null, 2, 20, 1);
            ok &= Check(!noTemplate.Success && !string.IsNullOrEmpty(noTemplate.Error), "a missing template fails with a message");
        }
        finally
        {
            Object.DestroyImmediate(template);
        }
        return ok;
    }

    public static bool RunFileCheck()
    {
        var ok = true;
        var template = MakeTemplate();
        var dir = Path.Combine(Path.GetTempPath(), "FlatSpaceRandomBoardCheck_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var result = RandomBoardBuilder.Generate(template, 2, 20, 777);
            ok &= Check(result.Success, "generation succeeds for the file check");
            if (!result.Success) return false;

            var save = RandomBoardBuilder.ToBoardSave(result);
            var name = RandomBoardBuilder.FileName(result.EffectiveSeed, 2, result.Planets.Count);
            ok &= Check(name == $"Random_s{result.EffectiveSeed}_2p_20.json", "file name carries seed, players and planets");

            var path = RandomBoardBuilder.WriteBoardFile(save, dir, name);
            ok &= Check(File.Exists(path), "the board file is written, creating its folder");

            var loaded = JsonUtility.FromJson<SaveLoadSystem.BoardDesignerSave>(File.ReadAllText(path));
            ok &= Check(loaded != null && JsonUtility.ToJson(loaded) == JsonUtility.ToJson(save),
                "the file loads back as the same board (the path a saved game reloads through)");
        }
        finally
        {
            Object.DestroyImmediate(template);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        return ok;
    }
}
