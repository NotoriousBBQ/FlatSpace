using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class AllAISelfChecks
{
    [MenuItem("FlatSpace/AI/Run All AI Self-Checks")]
    public static void Run()
    {
        // Every suite runs even after a failure, so one run shows everything that is broken.
        var suites = new List<(string name, System.Func<bool> run)>
        {
            ("Player Knowledge", PlayerKnowledgeSelfCheck.RunChecks),
            ("PlayerAI Resource", PlayerAIResourceSelfCheck.RunChecks),
            ("Ship Transport", ShipTransportSelfCheck.RunChecks),
            ("Distribution Center", DistributionCenterSelfCheck.RunChecks),
            ("Warship", WarshipSelfCheck.RunChecks),
        };

        var failed = new List<string>();
        foreach (var (name, run) in suites)
        {
            bool passed;
            try
            {
                passed = run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AllAISelfChecks] {name} threw: {e}");
                passed = false;
            }
            if (!passed) failed.Add(name);
        }

        Debug.Log(failed.Count == 0
            ? $"[AllAISelfChecks] ALL {suites.Count} SUITES PASSED"
            : $"[AllAISelfChecks] {failed.Count} of {suites.Count} SUITES FAILED: {string.Join(", ", failed)}");
    }
}
