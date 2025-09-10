using System;
using System.Collections.Generic;
using System.IO;
using FlatSpace.Game;
using UnityEngine;

public class SaveLoadSystem : MonoBehaviour
{
    [Serializable]
    public class SaveConfig
    {
        [Serializable]
        public struct PlanetSave
        {
            public string name;
            public Planet.PlanetType planetType;
            public Planet.PlanetStrategy planetStrategy;
            public float food;
            public int population;
            public float grotsits;
            public float morale;
        }

        [Serializable]
        public struct OrderSave
        {
            public GameAI.GameAIOrder.OrderType type;
            public GameAI.GameAIOrder.OrderTimingType timingType;
            public int timingDelay;
            public int totalDelay;
            public float data;
            public string dataType;
            public string target;
            public string origin;            
        }
        
        public GameAI.AIStrategy strategy;
        public int turnNumber;
 //       public BoardConfiguration BoardConfiguration;
        public List<OrderSave> orders = new List<OrderSave>();
        public List<PlanetSave> planetStatuses = new List<PlanetSave>();
        public SaveConfig(GameAI gameAI)
        {
            strategy = gameAI.Strategy;
            turnNumber = Gameboard.Instance.TurnNumber;
 //           BoardConfiguration = Gameboard.Instance.IntialBoardState;
            foreach (var planet in gameAI.GameAIMap.PlanetList)
            {
                planetStatuses.Add(
                    new PlanetSave
                    {         
                        name = planet.PlanetName,
                        planetType = planet.Type,
                        planetStrategy = planet.CurrentStrategy,
                        food = planet.Food,
                        population = planet.Population,
                        grotsits = planet.Grotsits,
                        morale = planet.Morale
                    });
            }

            foreach (var order in gameAI.CurrentAIOrders)
            {
                orders.Add(
                    new OrderSave
                    {
                        origin = order.Origin,
                        target = order.Target,
                        type = order.Type,
                        timingType = order.TimingType,
                        timingDelay = order.TimingDelay,
                        totalDelay = order.TotalDelay,
                        data = Convert.ToSingle(order.Data),
                        dataType = order.Data is float ? "float" : "int"
                    });
            }
        }
    }
    
    private static SaveLoadSystem _instance;
    public static SaveLoadSystem Instance => _instance;

    private void Awake()
    {
        if (_instance == null)
            _instance = this;
    }

    public void SaveGame(GameAI gameAI, string filePath)
    {
        var saveConfig = new SaveConfig(gameAI);
        if (!File.Exists(filePath)) 
            File.Create(filePath);
        
        string temp = JsonUtility.ToJson(saveConfig);
        File.WriteAllText(filePath, temp);
    }

    public bool LoadGame(GameAI gameAI, string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var readText = File.ReadAllText(filePath);
        var saveConfigcfg = JsonUtility.FromJson<SaveConfig>(readText);
        return true;
    }
}
