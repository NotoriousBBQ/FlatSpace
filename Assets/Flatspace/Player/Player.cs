using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using FlatSpace.AI;
using FlatSpace.Game;

public class Player : MonoBehaviour
{
 
    public static readonly List<Color32> PlayerColors = new List<Color32> 
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
    public PlayerAI playerAI = null;
    public int playerID = 0;
    public void Clear()
    {
        score = 0;
        aiDriven = true;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        if (aiDriven == true)
        {
            playerAI = this.AddComponent<PlayerAI>();
            playerAI.Player = this;
            playerAI.AIMap = Gameboard.Instance.GameAI.GameAIMap;
        }
    }

    public PlayerAI.AIStrategy GetStrategy()
    {
        return (!aiDriven ? PlayerAI.AIStrategy.AIStrategyNone : playerAI.Strategy);
    }

    public void SetStrategy(PlayerAI.AIStrategy strategy)
    {
        if (strategy == PlayerAI.AIStrategy.AIStrategyNone)
        {
            aiDriven = false;
            return;
        }

        if (!playerAI)
        {
            playerAI = this.AddComponent<PlayerAI>();
            if (playerAI)
            {
                playerAI.Player = this;
                playerAI.AIMap = Gameboard.Instance.GameAI.GameAIMap;
            }
        }
        playerAI.Strategy = strategy;
        aiDriven = true;

    }

    public void ProcessResults(List<Planet.PlanetUpdateResult> results, List<GameAI.GameAIOrder> orders)
    {
        if (playerAI && aiDriven)
        {
            playerAI.ProcessResults(results, orders);
        }
    }

    // UpdatePlanet is called once per frame
    void Update()
    {
        
    }
}
