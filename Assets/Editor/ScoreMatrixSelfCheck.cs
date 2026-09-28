using UnityEditor;
using UnityEngine;

public static class ScoreMatrixSelfCheck
{
    [MenuItem("FlatSpace/AI/Run ScoreMatrix Self-Check")]
    public static void Run()
    {
        var ok = RunTieBreakDoesNotFalseDuplicateCheck();
        ok &= RunGenuineDuplicateStillDetectedCheck();
        ok &= RunHigherPriorityProcessesFirstCheck();
        Debug.Log(ok
            ? "[ScoreMatrixSelfCheck] ALL PASSED"
            : "[ScoreMatrixSelfCheck] FAILURES (see errors above)");
    }

    private static bool Check(bool condition, string label)
    {
        if (!condition) Debug.LogError($"[ScoreMatrixSelfCheck] FAIL: {label}");
        return condition;
    }

    // The comparer's tie-break (equal Priority) used to compare Target.CompareTo(Target) against
    // Priority itself instead of returning it directly, so two DIFFERENT-named rows tied at a
    // Priority that happened to numerically match the -1/0/+1 string.CompareTo returns (most
    // commonly Priority == 1) were wrongly reported as equal -- a real "Duplicate Key in Build
    // Resource Matrix" crash from two distinct planets both landing on a shortage of magnitude 1.
    public static bool RunTieBreakDoesNotFalseDuplicateCheck()
    {
        var comparer = new ScoreMatrixDecisionComparer();
        var a = new ScoreMatrixDecisionElement { Target = "Alpha", Priority = 1f };
        var b = new ScoreMatrixDecisionElement { Target = "Beta", Priority = 1f };
        return Check(comparer.Compare(a, b) != 0,
            "two different-named rows tied at Priority 1 must not compare as equal");
    }

    public static bool RunGenuineDuplicateStillDetectedCheck()
    {
        var comparer = new ScoreMatrixDecisionComparer();
        var a = new ScoreMatrixDecisionElement { Target = "Alpha", Priority = 1f };
        var aAgain = new ScoreMatrixDecisionElement { Target = "Alpha", Priority = 1f };
        return Check(comparer.Compare(a, aAgain) == 0,
            "two rows with the same Target and Priority must still compare as equal (a real duplicate)");
    }

    public static bool RunHigherPriorityProcessesFirstCheck()
    {
        var comparer = new ScoreMatrixDecisionComparer();
        var high = new ScoreMatrixDecisionElement { Target = "High", Priority = 20f };
        var low = new ScoreMatrixDecisionElement { Target = "Low", Priority = 5f };
        return Check(comparer.Compare(high, low) < 0,
            "a higher-priority row must sort before a lower-priority one");
    }
}
