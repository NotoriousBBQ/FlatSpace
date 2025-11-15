using UnityEngine;

[CreateAssetMenu(fileName = "ShipData", menuName = "Scriptable Objects/Ships/ShipData")]
public class ShipData : ScriptableObject
{

    public string shipName;
    public float shipSpeed;
    public float shipHealth;
    public float shipHealthMax;
    public float shipDefense;
    public float shipDefenseMax;
    public float shipOffense;
    public float shipOffenseMax;
    public Sprite shipIcon;
}
