using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    public enum AIStrategy
    {
        AIStrategyExpand,
        AIStrategyConsolidate,
        AIStrategyAmass
    }
    public AIStrategy Strategy { get; set; } = AIStrategy.AIStrategyExpand;
    
    public static List<Color32> PlayerColors = new List<Color32> 
    {
        {new Color32(128, 0,0, 137 )},
        {new Color32(0, 128,0, 137 )},
        {new Color32(128, 128,0, 137 )},
        {new Color32(128, 0,128, 137 )},
        {new Color32(0,128,128, 137 )}

    };
    
    public static Color32 NoPlayerColor = new Color32(0, 0, 255, 137);
    public int score;
    public bool aiDriven = true;

    public void Clear()
    {
        score = 0;
        aiDriven = true;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
