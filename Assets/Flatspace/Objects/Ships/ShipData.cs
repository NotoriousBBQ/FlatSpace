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
    // Planet bombardment strength (0 = cannot bombard). Raised by the "<Subtype> Bombard" research effect up to the max.
    public float shipBombard;
    public float shipBombardMax;
    public Sprite shipIcon;
}
