using UnityEngine;
using System.Collections.Generic;
public class BoardDesigner : MonoBehaviour
{
    [SerializeField] private LineDrawObject lineDrawObjectPrefab;
    private List<LineDrawObject> _lineDrawObjects = new List<LineDrawObject>();

    [ContextMenu("Clear Connections")]
    public void ClearConnections()
    {
        for (var i = _lineDrawObjects.Count - 1; i >= 0; i--)
        {
            if (!_lineDrawObjects[i]) continue;
            _lineDrawObjects[i].transform.SetParent(null);
            _lineDrawObjects[i].gameObject.SetActive(false);
            Destroy(_lineDrawObjects[i].gameObject);
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

        foreach (var planet in planetList)
        {
            var planetPosition = planet.transform.position;
            foreach (var possibleNeighbor in planetList )
            {
                if (possibleNeighbor == planet)
                    continue;
                
                var distance = Vector2.Distance(new Vector2(possibleNeighbor.transform.localPosition.x,possibleNeighbor.transform.localPosition.y), 
                    new Vector2(planet.transform.localPosition.x,planet.transform.localPosition.y));
                if (distance <= MaxConnectionSize)
                {
                    planet.Connections.Add(new PlanetDesigner.DesignerConnection(possibleNeighbor, distance));
                }
            }
        }
        DrawConnections(planetList);
    }

    private void DrawConnections(List<PlanetDesigner> planetList)
    {
        ClearConnections();
        var connectionPoints = new List<(Vector3, Vector3)>();
        GetConnectionVectors(planetList,connectionPoints);

        var prefab = lineDrawObjectPrefab;
        if (!prefab)
            return;

        foreach (var linePoints in connectionPoints)
        {
            var lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

            if (lineDrawObject)
            {
                lineDrawObject.SetPoints(linePoints);
                _lineDrawObjects.Add(lineDrawObject);
            }
        }
    }

    private void GetConnectionVectors(List<PlanetDesigner> planets, List<(Vector3, Vector3)>  connectionPoints)
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
                var p2 = new Vector3(connection.Target.transform.localPosition.x, connection.Target.transform.localPosition.y, 0.0f);
                connectionPoints.Add((p1, p2));
            }
        }
        
    }

    [ContextMenu("Save Board Config")]
    public void SaveBoardConfig()
    {
        
    }
    public float MaxConnectionSize { get; set; } = 400.0f;
}
