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
            public List<int> workers; // four worker counts in the order food, grotsits, industry, research (assignWorkers)
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
    }
}
