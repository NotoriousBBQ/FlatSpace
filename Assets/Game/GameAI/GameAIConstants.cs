using UnityEngine;

[CreateAssetMenu(fileName = "GameAIConstants", menuName = "Scriptable Objects/GameAIConstants")]
public class GameAIConstants : ScriptableObject
{
    public float MoraleStep;
    public float DefaultTravelSpeed;
    public int MaxPathNodesToSearch;
    public float ExpandPopultionTrigger;
}
