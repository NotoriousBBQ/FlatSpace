using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace FlatSpace
{
    namespace Tools
    {

        public class BoardDesigner : MonoBehaviour
        {
            [SerializeField] private LineDrawObject lineDrawObjectPrefab;
            [SerializeField] private MapGenSettings mapGenSettings;
            private List<LineDrawObject> _lineDrawObjects = new List<LineDrawObject>();

            public void ClearConnections()
            {
                for (var i = _lineDrawObjects.Count - 1; i >= 0; i--)
                {
                    if (!_lineDrawObjects[i]) continue;
                    _lineDrawObjects[i].transform.SetParent(null);
                    _lineDrawObjects[i].gameObject.SetActive(false);
                    DestroyImmediate(_lineDrawObjects[i].gameObject);
                }

                _lineDrawObjects.Clear();
            }

            [ContextMenu("Generate Connections")]
            public void GenerateStarConnections()
            {
                var planetList = new List<PlanetDesigner>();
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<PlanetDesigner>())
                        planetList.Add(child.GetComponent<PlanetDesigner>());
                }

                if (planetList.Any(p => string.IsNullOrEmpty(p.planetName)))
                {
                    Debug.LogError("[BoardDesigner] Generate Connections needs planet names; run 'Generate Names And Strategies' first.");
                    return;
                }

                // Build the within-range name lists (symmetric).
                var names = new Dictionary<PlanetDesigner, List<string>>();
                foreach (var planet in planetList)
                    names[planet] = new List<string>();

                for (var i = 0; i < planetList.Count; i++)
                {
                    for (var j = i + 1; j < planetList.Count; j++)
                    {
                        var a = planetList[i];
                        var b = planetList[j];
                        var distance = Vector2.Distance(
                            new Vector2(a.transform.localPosition.x, a.transform.localPosition.y),
                            new Vector2(b.transform.localPosition.x, b.transform.localPosition.y));
                        if (distance <= MaxConnectionSize)
                        {
                            names[a].Add(b.planetName);
                            names[b].Add(a.planetName);
                        }
                    }
                }

                foreach (var planet in planetList)
                    planet.SetConnectionNames(names[planet]);

                RebuildAllConnectionCaches(planetList);
                DrawConnections(planetList);
            }

            private void RebuildAllConnectionCaches(List<PlanetDesigner> planetList)
            {
                var byName = new Dictionary<string, PlanetDesigner>();
                foreach (var planet in planetList)
                    if (!string.IsNullOrEmpty(planet.planetName))
                        byName[planet.planetName] = planet;
                foreach (var planet in planetList)
                    planet.RebuildConnectionsFromNames(byName);
            }

            [ContextMenu("Generate Names And Strategies")]
            public void GenerateNames()
            {
                var planetList = new List<PlanetDesigner>();
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<PlanetDesigner>())
                        planetList.Add(child.GetComponent<PlanetDesigner>());
                }

                Debug.Log($"Total Num Planets {planetList.Count}");

                foreach (var type in PlanetTypeDefaults.AllTypes)
                {
                    var count = 0;
                    foreach (var planet in planetList.FindAll(x => x.type == type))
                    {
                        planet.name = $"{PlanetTypeDefaults.ShortName(type)} {count}";
                        planet.planetName = planet.name;
                        planet.strategy = PlanetTypeDefaults.StrategyFor(type);
                        planet.UpdateGraphic();
                        count++;
                    }
                    Debug.Log($"{count} {PlanetTypeDefaults.ShortName(type)}");
                }
            }

            [ContextMenu("Map Gen: Dry Run")]
            public void MapGenDryRun()
            {
                if (!mapGenSettings)
                {
                    Debug.LogError("[BoardDesigner] assign a MapGenSettings asset first");
                    return;
                }
                var result = FlatSpace.Tools.MapGenerator.Generate(mapGenSettings);
                Debug.Log(result.Success
                    ? $"[BoardDesigner] dry run OK: {result.Planets.Count} planets, seed {result.EffectiveSeed}"
                    : $"[BoardDesigner] dry run failed: {result.Error}");
            }

            private void DrawConnections(List<PlanetDesigner> planetList)
            {
                ClearConnections();
                var connectionPoints = new List<(Vector3, Vector3)>();
                GetConnectionVectors(planetList, connectionPoints);

                var prefab = lineDrawObjectPrefab;
                if (!prefab)
                    return;

                foreach (var linePoints in connectionPoints)
                {
                    var lineDrawObject = Instantiate<LineDrawObject>(prefab, transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        lineDrawObject.SetPoints(linePoints);
                        _lineDrawObjects.Add(lineDrawObject);
                    }
                }
            }

            private void GetConnectionVectors(List<PlanetDesigner> planets, List<(Vector3, Vector3)> connectionPoints)
            {
                var alreadySeen = new List<PlanetDesigner>();
                foreach (var planet in planets)
                {
                    alreadySeen.Add(planet);
                    foreach (var connection in planet.Connections)
                    {
                        if (alreadySeen.Contains(connection.Target))
                            continue;
                        var p1 = new Vector3(planet.transform.localPosition.x, planet.transform.localPosition.y, 0.0f);
                        var p2 = new Vector3(connection.Target.transform.localPosition.x,
                            connection.Target.transform.localPosition.y, 0.0f);
                        connectionPoints.Add((p1, p2));
                    }
                }

            }

            public void SaveBoardConfig()
            {
                SaveLoadSystem.SaveBoardDesign(this);
            }

            public float MaxConnectionSize { get; set; } = 400.0f;
        }
    }
}