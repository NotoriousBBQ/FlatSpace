using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FlatSpace.AI;

namespace FlatSpace
{
    namespace Tools
    {
        public class PlanetDesigner : MonoBehaviour
        {
            public struct DesignerConnection
            {
                public PlanetDesigner Target;
                public readonly float Cost;

                public DesignerConnection(PlanetDesigner target, float cost)
                {
                    Target = target;
                    Cost = cost;
                }
            }

            public Planet.PlanetType type = Planet.PlanetType.PlanetTypeNormal;

            public Planet.PlanetStrategy strategy = Planet.PlanetStrategy.PlanetStrategyBalanced;
            public string planetName = "";
            public Vector2 position = new Vector2(0.0f, 0.0f);
            public Dictionary<string, GameAIMap.DestinationToPathingListEntry> DistanceMapToPathingList;
            public List<DesignerConnection> Connections = new List<DesignerConnection>();

            // Serialized source of truth for this planet's connections in the
            // designer. The Connections list above is an in-memory cache rebuilt
            // from these names via RebuildConnectionsFromNames.
            public List<string> connectionNames = new List<string>();

            public void SetConnectionNames(IEnumerable<string> names)
            {
                connectionNames = new List<string>();
                foreach (var n in names)
                {
                    if (!string.IsNullOrEmpty(n) && n != planetName && !connectionNames.Contains(n))
                        connectionNames.Add(n);
                }
            }

            public void RebuildConnectionsFromNames(IReadOnlyDictionary<string, PlanetDesigner> byName)
            {
                Connections.Clear();
                foreach (var name in connectionNames)
                {
                    if (!byName.TryGetValue(name, out var target) || target == this)
                        continue;
                    var cost = Vector2.Distance(
                        new Vector2(transform.localPosition.x, transform.localPosition.y),
                        new Vector2(target.transform.localPosition.x, target.transform.localPosition.y));
                    Connections.Add(new DesignerConnection(target, cost));
                }
            }

            [SerializeField] public TextMeshProUGUI nameTextField;
            [SerializeField] public TextMeshProUGUI typeTextField;

            private readonly Dictionary<Planet.PlanetType, Color32> _planetColors =
                new Dictionary<Planet.PlanetType, Color32>
                {
                    { Planet.PlanetType.PlanetTypeDesolate, new Color32(196, 65, 19, 255) },
                    { Planet.PlanetType.PlanetTypeFarm, new Color32(91, 188, 93, 255) },
                    { Planet.PlanetType.PlanetTypeIndustrial, new Color32(205, 133, 65, 255) },
                    { Planet.PlanetType.PlanetTypeNormal, new Color32(135, 206, 250, 255) },
                    { Planet.PlanetType.PlanetTypePrime, new Color32(173, 173, 22, 255) },
                    { Planet.PlanetType.PlanetTypeVerdant, new Color32(0, 206, 0, 255) },
                    { Planet.PlanetType.PlanetTypeOcean, new Color32(0, 255, 255, 255) },
                    { Planet.PlanetType.PlanetTypeDesert, new Color32(255, 215, 0, 255) },
                    
                };


            private void OnValidate()
            {

                nameTextField.text = planetName;
                typeTextField.text = type.ToString();

                SetPlanetColor(type);
            }

            public void UpdateGraphic()
            {
                OnValidate();
            }

            public void SetPlanetColor(Planet.PlanetType planetType)
            {
                var sprintRenderer = GetComponent<SpriteRenderer>();
                sprintRenderer.color = _planetColors[planetType];
            }


            // Start is called once before the first execution of UpdatePlanet after the MonoBehaviour is created
            void Start()
            {

            }

            // UpdatePlanet is called once per frame
            void Update()
            {

            }
        }
    }
}