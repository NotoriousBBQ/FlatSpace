using System;
using System.Collections.Generic;
using System.IO;
using FlatSpace.AI;
using FlatSpace.Game;
using FlatSpace.Tools;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using SimpleFileBrowser;
using UnityEngine.Events;


public class SaveLoadSystem : MonoBehaviour
{
    public static SaveLoadSystem Instance { get; private set; }
    private List<AsyncOperationHandle> loadingList = new List<AsyncOperationHandle>();
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        SetFileBrowserFilters();
    }
    
    #region SaveLoadGame
    [Serializable]
    public class GameSave
    {
        [Serializable]
        public struct PlanetSave
        {
            public string name;
            public Planet.PlanetType planetType;
            public Planet.PlanetStrategy planetStrategy;
            public float food;
            public int[] population;
            public float grotsits;
            public float morale;
            public int owner;
            public List<int> populationTransferInProgress;
            public bool foodTransferInProgress;
            public bool grotsitsTransferInProgress;
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
            public int playerId;
        }

        [Serializable]
        public struct PlayerSave
        {
            public int playerId;
            public PlayerAI.AIStrategy strategy;
        }
        
        public int turnNumber;
        public string initialBoardStatePath;
        public string boardDesignDataPath;
        public List<PlayerSave> players = new List<PlayerSave>();
        public List<OrderSave> orders = new List<OrderSave>();
        public List<PlanetSave> planetStatuses = new List<PlanetSave>();
        public GameSave(GameAI gameAI)
        {
            turnNumber = Gameboard.Instance.TurnNumber;
            initialBoardStatePath = Gameboard.Instance.IntialBoardState != null ? Gameboard.Instance.IntialBoardState.name : null;
            boardDesignDataPath = Gameboard.Instance._boardDesignPath;

            for(var i = 0; i < Gameboard.Instance.players.Count; i++)
            {
                players.Add(
                    new PlayerSave
                    { 
                        playerId = i,
                        strategy = Gameboard.Instance.players[i].GetStrategy()    
                    });
            }
            
            foreach (var planet in gameAI.GameAIMap.PlanetList)
            {
                var planetSave = new PlanetSave
                {         
                    name = planet.PlanetName,
                    planetType = planet.Type,
                    planetStrategy = planet.CurrentStrategy,
                    food = planet.Food,
                    grotsits = planet.Grotsits,
                    morale = planet.Morale,
                    owner = planet.Owner,
                    populationTransferInProgress = planet.IncomingPopulationSource,
                    foodTransferInProgress = planet.FoodShipmentIncoming,
                    grotsitsTransferInProgress = planet.GrotsitsShipmentIncoming,
                    population = new int[Gameboard.Instance.players.Count]
                };
                for (var i = 0; i < Gameboard.Instance.players.Count; i++)
                {
                    var popByPlayer = planet.Population.FindAll(x => x.Player == i).Count;
                    planetSave.population[i] = popByPlayer;
                }
                planetStatuses.Add(planetSave);
                   
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
                        dataType = order.Data is float ? "float" : "int", 
                        playerId = order.PlayerId
                    });
            }
        }
    }
    private static void SaveGame(GameAI gameAI, string filePath)
    {
        var gameSave = new GameSave(gameAI);
        if (!File.Exists(filePath)) 
            File.Create(filePath).Dispose();
        
        var  temp = JsonUtility.ToJson(gameSave, true);
        File.WriteAllText(filePath, temp);
    }

    private static bool LoadSavedGame(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var readText = File.ReadAllText(filePath);
        var saveConfig = JsonUtility.FromJson<GameSave>(readText);
        if (saveConfig == null)
            return false;
        var gameScene = SceneManager.GetSceneByName("Flatspace");
        if (!gameScene.isLoaded)
        {
            var bSaveGameInialized = false;
            LoadGameScene("Flatspace", (scene, sceneMode) =>
            {
                bSaveGameInialized = Gameboard.Instance.InitGameFromGaveSave(saveConfig);
                SceneManager.SetActiveScene(scene);
            });   
            return bSaveGameInialized;
        }
       
        return Gameboard.Instance.InitGameFromGaveSave(saveConfig);
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
    #endregion
    #region BoardDesignerSave

    [Serializable]
    public class BoardDesignerSave
    {
        [Serializable]
        public struct BoardDesignerEntry
        {
            public string name;
            public Planet.PlanetType planetType;
            public Vector2 position;
        }

        public List<BoardDesignerEntry> planetEntries = new List<BoardDesignerEntry>();

        public BoardDesignerSave(List<PlanetDesigner> planetList)
        {
            planetEntries.Clear();
            foreach (var planet in planetList)
            {
                planetEntries.Add(new BoardDesignerEntry
                {
                    name = planet.planetName,
                    planetType = planet.type,
                    position = planet.transform.localPosition,
                });
            }
            
        }
    }
    private static void SaveDesignerConfig(BoardDesignerSave saveData, string filePath)
    {
        if (!File.Exists(filePath)) 
            File.Create(filePath).Dispose();
        
        var  temp = JsonUtility.ToJson(saveData, true);
        File.WriteAllText(filePath, temp); 
    }
    public static bool LoadDesignerContent(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var readText = File.ReadAllText(filePath);
        var saveConfig = JsonUtility.FromJson<BoardDesignerSave>(readText);
        if (saveConfig == null) return false;

        var gameScene = SceneManager.GetSceneByName("Flatspace");
        if (!gameScene.isLoaded)
        {
            LoadGameScene("Flatspace", (scene, sceneMode) =>
            {
                Gameboard.Instance.InitGameFromDesignerConfig(filePath, saveConfig);
                SceneManager.SetActiveScene(scene);
            });
        }
        else
        {
            Gameboard.Instance.InitGameFromDesignerConfig(filePath, saveConfig);
            SceneManager.SetActiveScene(gameScene);
        }

        return true;
    }
    
    public void LoadAIConstantsAddressable(string address, Action<AsyncOperationHandle<GameAIConstants>> loadCompleteDelegate)
    {
        var loadRequest = Addressables.LoadAssetAsync<GameAIConstants>(address);
        loadRequest.Completed += AIConstantsLoadComplete;
        loadRequest.Completed += loadCompleteDelegate;
        loadingList.Add(loadRequest);
        var loadComplete = loadRequest.WaitForCompletion();
        Addressables.Release(loadComplete);
    }

    private void AIConstantsLoadComplete(AsyncOperationHandle<GameAIConstants> loadRequest)
    {
        loadRequest.Completed -= AIConstantsLoadComplete;
        loadingList.Remove(loadRequest);
    }

    #endregion

    #region MenuFunctions

    public static void LoadGame()
    {
        ShowLoadGameFileBrowserAndStartGame();
    }

    public static void SaveGame()
    {
        ShowSaveGameFileBrowser();
    }
    
    public static void LoadDesigner()
    {
        var scene = SceneManager.GetSceneByName("MapDesigner");
        if (!scene.isLoaded)
        {
            LoadGameScene("MapDesigner", SceneManagerOnDesignerSceneLoaded);   
            return;
        }

        SceneManagerOnDesignerSceneLoaded(scene, LoadSceneMode.Single);        
    }

    public static void StartNewGame()
    {        
        ShowConfigFileBrowserAndStartNewGame();
    }
    private static void LoadGameScene(string sceneName, UnityAction<Scene, LoadSceneMode> loadedCallback)
    {
        SceneManager.LoadScene(sceneName);
        SceneManager.sceneLoaded += loadedCallback;
    }

    private static void SceneManagerOnDesignerSceneLoaded(Scene scene, LoadSceneMode sceneMode)
    {
        SceneManager.SetActiveScene(scene);
    }
    #endregion
    
    #region FilePicker

    private static void SetFileBrowserFilters()
    {
        FileBrowser.SetFilters(false, ".json");
    }
    private static void ShowLoadGameFileBrowserAndStartGame()
    {
        FileBrowser.ShowLoadDialog((paths) => LoadSavedGame(paths[0]), 
            ()=> Debug.Log("Load Cancelled"), FileBrowser.PickMode.Files, false, @"C:\Temp\");
    }
    
    private static void ShowSaveGameFileBrowser()
    {
        FileBrowser.ShowSaveDialog((paths) => SaveGame(Gameboard.Instance.GameAI, paths[0]), 
            ()=> Debug.Log("Save Cancelled"), FileBrowser.PickMode.Files, false, @"C:\Temp\");
    }

    public static void SaveBoardDesign(BoardDesigner designer)
    {
        var planetList = new List<PlanetDesigner>();
        foreach (Transform child in designer.transform)
        {
            var planetDesign = child.GetComponent<PlanetDesigner>();
            if (planetDesign) 
                planetList.Add(planetDesign);
        }
        var designData = new BoardDesignerSave(planetList);

        if (designData.planetEntries.Count == 0)
            return;
        ShowSaveGameConfigFileBrowser(designData);

    }
   
    private static void ShowConfigFileBrowserAndStartNewGame()
    {
        FileBrowser.ShowLoadDialog((paths) => LoadDesignerContent(paths[0]), 
            ()=> LoadGameScene("Flatspace",(scene, sceneMode) =>
            {
                SceneManager.SetActiveScene(scene);
            }), FileBrowser.PickMode.Files, false, @"C:\BoardConfig\");
    }
    
    private static void ShowSaveGameConfigFileBrowser(BoardDesignerSave saveData)
    {
        FileBrowser.ShowSaveDialog((paths) => SaveDesignerConfig(saveData, paths[0]), 
            ()=> Debug.Log("Save Cancelled"), FileBrowser.PickMode.Files, false, @"C:\BoardConfig\");
    }
    #endregion
}
