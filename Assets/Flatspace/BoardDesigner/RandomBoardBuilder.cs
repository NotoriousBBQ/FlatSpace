using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlatSpace
{
    namespace Tools
    {
        // Pure helpers behind "start a game from a random board" (MainMenu -> RandomBoardPopup): the
        // allowed ranges, a generated board turned into the same BoardDesignerSave a designer config
        // loads as, and the JSON file it is written to (saved games reload a board by file path).
        // No Gameboard.Instance, so Assets/Editor/RandomBoardSelfCheck.cs drives it directly.
        public static class RandomBoardBuilder
        {
            public const int MinPlayers = 2;
            public const int MinPlanets = 10;
            public const int MaxPlanets = 400;
            public const int DefaultPlayers = 2;
            public const int DefaultPlanets = 20;

            // One player colour per player, so that list is the real ceiling today.
            public static int MaxPlayers => Player.PlayerColors.Count;

            public static int ClampPlayers(int players) => Mathf.Clamp(players, MinPlayers, MaxPlayers);
            public static int ClampPlanets(int planets) => Mathf.Clamp(planets, MinPlanets, MaxPlanets);

            // Blank or unparsable text gives the fallback; a value past int range is clamped to it.
            public static int ParseInt(string text, int fallback)
            {
                if (!long.TryParse(text, out var value))
                    return fallback;
                return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
            }

            // A positive seed; MapGenerator treats 0 as "pick one", so a typed 0 or a negative also lands here.
            public static int NewSeed() => new System.Random().Next(1, int.MaxValue);
            public static int ResolveSeed(int typed) => typed > 0 ? typed : NewSeed();

            // Generates from a runtime copy of the template (its type weights and geometry), so the
            // asset on disk is never touched.
            public static GenerationResult Generate(MapGenSettings template, int players, int planets, int seed)
            {
                if (!template)
                    return GenerationResult.Fail("no MapGenSettings assigned", seed);
                var settings = Object.Instantiate(template);
                try
                {
                    settings.playerCount = ClampPlayers(players);
                    settings.totalPlanetCount = ClampPlanets(planets);
                    settings.seed = ResolveSeed(seed);
                    return MapGenerator.Generate(settings);
                }
                finally
                {
                    Object.DestroyImmediate(settings);
                }
            }

            public static SaveLoadSystem.BoardDesignerSave ToBoardSave(GenerationResult result)
            {
                var save = new SaveLoadSystem.BoardDesignerSave();
                foreach (var planet in result.Planets)
                {
                    save.planetEntries.Add(new SaveLoadSystem.BoardDesignerSave.BoardDesignerEntry
                    {
                        name = planet.Name,
                        planetType = planet.Type,
                        position = planet.Position,
                        connections = new System.Collections.Generic.List<string>(planet.Connections ?? new System.Collections.Generic.List<string>())
                    });
                }
                return save;
            }

            public static string FileName(int seed, int players, int planets) =>
                $"Random_s{seed}_{players}p_{planets}.json";

            public static string BoardDirectory() =>
                Path.Combine(Application.persistentDataPath, "BoardConfigs", "Random");

            // Writes the board and returns its full path.
            public static string WriteBoardFile(SaveLoadSystem.BoardDesignerSave save, string directory, string fileName)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, fileName);
                File.WriteAllText(path, JsonUtility.ToJson(save, true));
                return path;
            }
        }
    }
}
