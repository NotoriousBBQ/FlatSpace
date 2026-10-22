using UnityEditor;
using UnityEngine;

// Creates / updates the Siegeship and Bunker ShipData assets and wires them into the active GameAIConstants asset.
// Idempotent: existing assets are updated in place, never duplicated.
public static class PlanetDefenseAssetSetup
{
    private const string ShipsFolder = "Assets/Flatspace/Objects/Ships";
    private const string WarShipPath = ShipsFolder + "/WarShip.asset";
    private const string SiegeshipPath = ShipsFolder + "/Siegeship.asset";
    private const string BunkerPath = ShipsFolder + "/Bunker.asset";
    private const string ConstantsPath = "Assets/GameAIConstantsProductionTypes.asset";

    [MenuItem("FlatSpace/Setup/Create Planet Defense Assets")]
    public static void Run()
    {
        var warship = AssetDatabase.LoadAssetAtPath<ShipData>(WarShipPath);
        var constants = AssetDatabase.LoadAssetAtPath<GameAIConstants>(ConstantsPath);
        if (warship == null || constants == null)
        {
            Debug.LogError($"[PlanetDefenseAssetSetup] Missing {(warship == null ? WarShipPath : ConstantsPath)}; nothing changed.");
            return;
        }

        warship.shipBombard = 1f;
        warship.shipBombardMax = 3f;
        EditorUtility.SetDirty(warship);

        var siege = LoadOrCreate(SiegeshipPath);
        Fill(siege, "Siegeship", warship.shipIcon, 120f, 100f, 200f, 3f, 9f, 3f, 9f, 20f, 60f);
        var bunker = LoadOrCreate(BunkerPath);
        Fill(bunker, "Bunker", warship.shipIcon, 0f, 150f, 300f, 8f, 20f, 12f, 36f, 0f, 0f);

        constants.siegeShipData = siege;
        constants.bunkerData = bunker;
        EditorUtility.SetDirty(constants);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlanetDefenseAssetSetup] Siegeship and Bunker assets ready; WarShip bombard 1/3; GameAIConstants wired.");
    }

    private static ShipData LoadOrCreate(string path)
    {
        var data = AssetDatabase.LoadAssetAtPath<ShipData>(path);
        if (data != null) return data;
        data = ScriptableObject.CreateInstance<ShipData>();
        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    private static void Fill(ShipData d, string shipName, Sprite icon, float speed, float hp, float hpMax,
        float def, float defMax, float off, float offMax, float bombard, float bombardMax)
    {
        d.shipName = shipName;
        d.shipSpeed = speed;
        d.shipHealth = hp; d.shipHealthMax = hpMax;
        d.shipDefense = def; d.shipDefenseMax = defMax;
        d.shipOffense = off; d.shipOffenseMax = offMax;
        d.shipBombard = bombard; d.shipBombardMax = bombardMax;
        if (d.shipIcon == null) d.shipIcon = icon;   // placeholder art until real icons exist
        EditorUtility.SetDirty(d);
    }
}
