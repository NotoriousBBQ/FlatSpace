using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlatSpace
{
    namespace Human
    {
        /// <summary>
        /// One thing the human asks for. Flat on purpose: JsonUtility has no polymorphism, so every command shares these fields and
        /// uses the ones its type needs (see HumanCommandProcessor for the type list and which fields each reads).
        /// </summary>
        [Serializable]
        public class HumanCommand
        {
            public string type;
            public string origin;     // a planet name (ships leave it, production is queued on it)
            public string target;     // a planet name
            public string item;       // a catalog item name
            public string kind;       // ship kind: "Warship", "Siegeship" or "ColonyShip"
            public string eventName;  // a HumanEventKind name (setPause)
            public int count;
            public int index;         // a queue index
            public int toIndex;
            public int rival;         // a player id
            public List<int> ships;   // dock indices at the origin (move)
            public bool flag;
        }

        [Serializable]
        public class HumanCommandBatch
        {
            public List<HumanCommand> commands = new List<HumanCommand>();
        }

        /// <summary>The outcome of one command: applied, or refused with a reason from the command's closed set.</summary>
        [Serializable]
        public class HumanCommandResult
        {
            public string type;
            public bool applied;
            public string reason;   // empty when applied
            public string detail;   // what was done, for the log
        }

        [Serializable]
        public class HumanCommandResultBatch
        {
            public int turn;
            public List<HumanCommandResult> results = new List<HumanCommandResult>();
        }

        /// <summary>
        /// The temporary command channel: commands.json is read each time a turn steps, then renamed commands.done.json, and the
        /// outcomes are written to results.json. The folder sits beside Assets/ in the editor (like AITuningLogs, so the asset
        /// database never indexes it) and under persistentDataPath in a player.
        /// </summary>
        public static class HumanCommandFile
        {
            public const string CommandsName = "commands.json";
            public const string DoneName = "commands.done.json";
            public const string ResultsName = "results.json";

            public static string Directory()
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "HumanCommands"));
#else
                return Path.Combine(Application.persistentDataPath, "HumanCommands");
#endif
            }

            /// <summary>Reads commands.json if it exists and moves it aside, so a command is never applied twice. False when there is none.</summary>
            public static bool TryTake(string directory, out string json)
            {
                json = null;
                try
                {
                    var path = Path.Combine(directory, CommandsName);
                    if (!File.Exists(path)) return false;
                    json = File.ReadAllText(path);
                    var done = Path.Combine(directory, DoneName);
                    if (File.Exists(done)) File.Delete(done);
                    File.Move(path, done);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HumanCommandFile] could not read the commands file: {e.Message}");
                    json = null;
                    return false;
                }
            }

            public static void WriteResults(string directory, int turn, List<HumanCommandResult> results)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(directory);
                    var batch = new HumanCommandResultBatch { turn = turn, results = results };
                    File.WriteAllText(Path.Combine(directory, ResultsName), JsonUtility.ToJson(batch, true));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HumanCommandFile] could not write the results file: {e.Message}");
                }
            }
        }
    }
}
