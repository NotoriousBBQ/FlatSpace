using System;
using System.Collections.Generic;
using System.IO;
using FlatSpace.Game;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using SimpleFileBrowser;
using UnityEngine.Events;

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
        public string boardConfigurationPath;
        public List<OrderSave> orders = new List<OrderSave>();
        public List<PlanetSave> planetStatuses = new List<PlanetSave>();
        public SaveConfig(GameAI gameAI)
        {
            strategy = gameAI.Strategy;
            turnNumber = Gameboard.Instance.TurnNumber;
            boardConfigurationPath = Gameboard.Instance.IntialBoardState.name;
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

    public static SaveLoadSystem Instance { get; private set; }
    private List<AsyncOperationHandle> loadingList = new List<AsyncOperationHandle>();
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        SetFileBrowserFilters();
    }

    private static void SaveGame(GameAI gameAI, string filePath)
    {
        var saveConfig = new SaveConfig(gameAI);
        if (!File.Exists(filePath)) 
            File.Create(filePath).Dispose();
        
        string temp = JsonUtility.ToJson(saveConfig);
        File.WriteAllText(filePath, temp);
    }

    private static bool LoadGame(GameAI gameAI, string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var readText = File.ReadAllText(filePath);
        var saveConfig = JsonUtility.FromJson<SaveConfig>(readText);
        if (saveConfig == null)
            return false;
        return Gameboard.Instance.InitGameFromSaveConfig(saveConfig);
    }

    public void LoadBoardConfigAddressable(string address, Action<AsyncOperationHandle<BoardConfiguration>> loadCompleteDelegate)
    {
        var loadRequest = Addressables.LoadAssetAsync<BoardConfiguration>(address);
        loadRequest.Completed += BoardConfigurationLoadComplete;
        loadRequest.Completed += loadCompleteDelegate;
        loadingList.Add(loadRequest);
    }

    private void BoardConfigurationLoadComplete(AsyncOperationHandle<BoardConfiguration> loadRequest)
    {
        loadRequest.Completed -= BoardConfigurationLoadComplete;
        loadingList.Remove(loadRequest);
    }
    #region MainMenuFunctions

    public static void LoadGame()
    {
        var gameScene = SceneManager.GetSceneByName("Flatspace");
        if (!gameScene.isLoaded)
        {
            LoadGameInternal(SceneManagerOnGameLoadingSceneLoaded);   
            return;
        }

        SceneManagerOnGameLoadingSceneLoaded(gameScene, LoadSceneMode.Single);
    }

    public static void SaveGame()
    {
        ShowSaveGameFileBrowser();
    }

    public static void LoadNewGame()
    {
        LoadGameInternal(SceneManagerOnNewGameSceneLoaded);
    }
    private static void LoadGameInternal(UnityAction<Scene, LoadSceneMode> loadedCallback)
    {
        SceneManager.LoadScene("Flatspace");
        SceneManager.sceneLoaded += loadedCallback;
    }
    private static void SceneManagerOnNewGameSceneLoaded(Scene scene, LoadSceneMode sceneMode)
    {
        SceneManager.SetActiveScene(scene);
    }

    private static void SceneManagerOnGameLoadingSceneLoaded(Scene scene, LoadSceneMode sceneMode)
    {
        SceneManager.SetActiveScene(scene);
        ShowLoadGameFileBrowser();
    }
    #endregion
    
    #region FilePicker

    private static void SetFileBrowserFilters()
    {
        FileBrowser.SetFilters(false, ".json");
    }
    private static void ShowLoadGameFileBrowser()
    {
        FileBrowser.ShowLoadDialog((paths) => LoadGame(Gameboard.Instance.GameAI, paths[0]), 
            ()=> Debug.Log("Load Cancelled"), FileBrowser.PickMode.Files, false, @"C:\Temp\");
    }
    
    private static void ShowSaveGameFileBrowser()
    {
        FileBrowser.ShowSaveDialog((paths) => SaveGame(Gameboard.Instance.GameAI, paths[0]), 
            ()=> Debug.Log("Save Cancelled"), FileBrowser.PickMode.Files, false, @"C:\Temp\");
    }
   
    #endregion
}
