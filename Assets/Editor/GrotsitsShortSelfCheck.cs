using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FlatSpace.AI;

public static class GrotsitsShortSelfCheck
{
    [MenuItem("FlatSpace/AI/Run Grotsits Short Self-Check")]
    public static void Run() => RunChecks();

    public static bool RunChecks()
    {
        var ok = RunTransitionCheck();
        ok &= RunGoneAndClearCheck();
        Debug.Log(ok
            ? "[GrotsitsShortSelfCheck] ALL PASSED"
            : "[GrotsitsShortSelfCheck] FAILURES (see errors above)");
        return ok;
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[GrotsitsShortSelfCheck] FAIL: {label}");
        return condition;
    }

    private static List<GrotsitsShortTracker.Change> Step(GrotsitsShortTracker tracker,
        params (string planet, bool isShort)[] populated)
        => tracker.Update(populated);

    private static string Describe(List<GrotsitsShortTracker.Change> changes)
        => string.Join(",", changes.Select(c => (c.Started ? "+" : "-") + c.Planet));

    // Only transitions are reported, in planet-name order, so a planet that stays short is logged once, not every turn.
    public static bool RunTransitionCheck()
    {
        var ok = true;
        var t = new GrotsitsShortTracker();

        ok &= Check(Describe(Step(t, ("B", true), ("A", false), ("C", true))) == "+B,+C",
            "the first turn reports each short planet as started (B, C), in name order, and nothing for A");
        ok &= Check(t.IsShort("B") && t.IsShort("C") && !t.IsShort("A"), "IsShort reflects the tracked state");
        ok &= Check(Step(t, ("B", true), ("A", false), ("C", true)).Count == 0,
            "an unchanged state reports nothing (no line per turn while a planet stays short)");
        ok &= Check(Describe(Step(t, ("B", false), ("A", false), ("C", true))) == "-B",
            "B recovering is reported as ended");
        ok &= Check(Describe(Step(t, ("B", true), ("A", true), ("C", false))) == "+A,+B,-C",
            "several changes in one turn are all reported, sorted by name (A and B start, C ends)");
        ok &= Check(Describe(Step(t, ("B", true), ("A", true), ("C", false), ("D", false))) == "",
            "a newly populated planet that is not short reports nothing");
        return ok;
    }

    // A planet that stops being populated while short ends its episode; Clear forgets everything.
    public static bool RunGoneAndClearCheck()
    {
        var ok = true;
        var t = new GrotsitsShortTracker();
        Step(t, ("A", true), ("B", true));
        ok &= Check(Describe(Step(t, ("B", true))) == "-A",
            "a short planet that is no longer populated (A) is reported as ended; B, still short, is not repeated");
        ok &= Check(!t.IsShort("A") && t.IsShort("B"), "A is forgotten, B is still short");
        ok &= Check(Describe(Step(t, ("A", true), ("B", true))) == "+A",
            "A populated and short again starts a new episode");

        t.Clear();
        ok &= Check(!t.IsShort("A") && !t.IsShort("B"), "Clear forgets every episode");
        ok &= Check(Describe(Step(t, ("A", true))) == "+A", "after Clear a short planet is reported as started again");
        ok &= Check(Step(t).Count == 1 && !t.IsShort("A"), "an empty populated set ends every open episode");
        return ok;
    }
}
