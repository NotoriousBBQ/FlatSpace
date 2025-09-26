using System;
using System.Collections;
using System.Collections.Generic;
using FlatSpace.Pathing;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace FlatSpace
{
    namespace Game
    {
        public class Gameboard : MonoBehaviour
        {
            private static Gameboard _instance;
            public static Gameboard Instance => _instance;
            [SerializeField] public BoardConfiguration IntialBoardState;

            [SerializeField] private float _minCameraOrtho = 1;
            [SerializeField] private float _maxCameraOrtho = 15;
            [SerializeField] private float _cameraOrthoStep = 0.1f;
            public GameAI GameAI {get; private set;}
            [SerializeField] private GameAIConstants gameAIConstants;
            [SerializeField] private LineDrawObject _lineDrawObjectPrefab;
            [SerializeField]private LineDrawObject _orderLineDrawObjectPrefab;

            public PlanetUIObject _PlanetUIPrefab;

            private MapInputActions _mapInputActions;
            private Camera _camera;

            private readonly List<PlanetUIObject> _planetUIObjects = new List<PlanetUIObject>();
 
            public int TurnNumber { get; private set; }= 0;

            void Start()
            {

            }

            // Update is called once per frame
            void Update()
            {

            }

            private void Awake()
            {
                if (_instance == null)
                    _instance = this;

                _camera = Camera.main;
                _planetUIObjects.Clear();
                if (IntialBoardState)
                {
                    InitGame();
                }
                InitializeInputActions();

            }

            public bool InitGameFromDesignerConfig(SaveLoadSystem.BoardDesignerSave boardData)
            {
                if (boardData == null)
                    return false;

                IntialBoardState = null;
                ClearExistingGameState();
                InitGame(boardData);
                PlanetaryUIUpdate();
                BoardUIUpdate();

                return true;
                
            }
            public bool InitGameFromSaveConfig(SaveLoadSystem.GameSave gameSave)
            {
                if (gameSave == null || string.IsNullOrEmpty(gameSave.boardConfigurationPath))
                    return false;
                
                SaveLoadSystem.Instance.LoadBoardConfigAddressable(gameSave.boardConfigurationPath, asyncOp =>
                {
                    if (asyncOp.Status == AsyncOperationStatus.Succeeded)
                    {
                        IntialBoardState = Instantiate<BoardConfiguration>(asyncOp.Result) ;
                        ClearExistingGameState();
                        InitGame();
                        SetSimulationStatus(gameSave);
                        PlanetaryUIUpdate();
                        BoardUIUpdate();
                    }                     
                });

                return true;
            }

            private void SetSimulationStatus(SaveLoadSystem.GameSave gameSave)
            {
                
                TurnNumber = gameSave.turnNumber;
                GameAI.SetSimulationStats(gameSave);
            }

            private void ClearExistingGameState()
            {
                if (GameAI != null)
                    GameAI.ClearGameAI();
                ClearGraphics();
            }

            private void ClearGraphics()
            {
                foreach(var planetUIObject in _planetUIObjects)
                    Destroy(planetUIObject.gameObject);
                _planetUIObjects.Clear();
                var lineDrawObjects = GetComponentsInChildren<LineDrawObject>();
                foreach (var linedrawObject in lineDrawObjects)
                    Destroy(linedrawObject.gameObject);
            }

            private void InitGame()
            {
                if (GameAI == null)
                    GameAI = this.AddComponent<GameAI>() as GameAI;

                GameAI.InitGameAI(IntialBoardState._planetSpawnData, gameAIConstants);
                InitPlanetGraphics(IntialBoardState._planetSpawnData);
                InitPathGraphics();
            }

            private bool InitGame(SaveLoadSystem.BoardDesignerSave boardData)
            {
                if (GameAI == null)
                    GameAI = this.AddComponent<GameAI>() as GameAI;

                var planetSpawnData = new List<PlanetSpawnData>();
                var gameAIConstants = new GameAIConstants();
                GameAI.InitGameAI(planetSpawnData, gameAIConstants);
                InitPlanetGraphics(IntialBoardState._planetSpawnData);
                InitPathGraphics();
                
                return true;
            }

            private void InitPlanetGraphics(List<PlanetSpawnData> spawnDataList)
            {
                foreach (var spawnData in spawnDataList)
                {
                    InitUIElement(spawnData, transform);   
                    
                }
            }
            
                
            private void InitUIElement(PlanetSpawnData spawnData, Transform parentTransform)
            {
                var prefab = Gameboard.Instance._PlanetUIPrefab;
                if (!prefab)
                    return;
            
                var uiObject = Instantiate(prefab,parentTransform) as PlanetUIObject;
                uiObject.transform.localPosition += spawnData._planetPosition;
                InitializeUIObject(uiObject, spawnData);
            }
            
            private void InitializeUIObject(PlanetUIObject uiObject , PlanetSpawnData spawnData)
            {

                if (uiObject == null)
                    return;
                uiObject._nameTextField.text = spawnData._planetName;
                uiObject._planetName = spawnData._planetName;
                uiObject.SetPlanetColor(spawnData._planetType);
                uiObject.UIUpdate();        
                _planetUIObjects.Add(uiObject); 
            }

            private void InitPathGraphics()
            {
                List<(Vector3, Vector3)> connectionPoints = new List<(Vector3, Vector3)>();
                PathingSystem.Instance.ConnectionVectors(connectionPoints);

                var prefab = _lineDrawObjectPrefab;
                if (!prefab)
                    return;

                foreach (var linePoints in connectionPoints)
                {
                    LineDrawObject lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        lineDrawObject.SetPoints(linePoints);
                    }
                }
            }

            private static bool OrderHasGraphic(GameAI.GameAIOrder order)
            {
                var hasGraphicList = new GameAI.GameAIOrder.OrderType[3]
                {
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport, 
                    GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                };
                
                return Array.Exists(hasGraphicList, t => t == order.Type);

            }

            private static Color32 ColorForOrderType(GameAI.GameAIOrder.OrderType type)
            {
                Color color;
                switch (type)
                {
                    case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                        color = new Color32(0, 255,0, 255 );
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                        color = new Color32(0, 255, 255, 255);
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        color = new Color32(210, 105, 30, 255);
                        break;
                    default:
                        color = new Color32(255, 255, 255, 255);
                        break;
                }
                return color;
            }
            private void DisplayOrderGraphics(List<GameAI.GameAIOrder> orders)
            {
                var prefab = _orderLineDrawObjectPrefab;
                if (!prefab)
                    return;
                
                var lineDrawObjects = GetComponentsInChildren<LineDrawObject>();
                var taggedChildren = new List<LineDrawObject>();
                foreach (var linedrawObject in lineDrawObjects)
                {
                    if(linedrawObject.CompareTag("OrderLineDraw"))
                        taggedChildren.Add(linedrawObject);
                }
                foreach (var child in taggedChildren)
                    Destroy(child.gameObject);
                
                foreach (var order in orders)
                {
                    if(!OrderHasGraphic(order))
                        continue;
                    var lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        var point1 = _planetUIObjects.Find(x => x._planetName == order.Origin).transform.localPosition;
                        var point2 = _planetUIObjects.Find(x => x._planetName == order.Target).transform.localPosition;
                        float offset = 10.0f;
                        if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport)
                            offset = -10.0f;
                        else if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport)
                            offset = 20.0f;
                        var linePoints = (new Vector3(point1.x + offset, point1.y + offset, 0.0f), new Vector3(point2.x + offset, point2.y + offset, 0.0f) );
                        var progressAmount =
                            Math.Clamp(
                                Convert.ToSingle(order.TotalDelay - (order.TimingDelay)) /
                                Convert.ToSingle(order.TotalDelay), 0.15f, 0.85f);
                        lineDrawObject.SetPoints(linePoints,progressAmount );
                        lineDrawObject.SetColor(ColorForOrderType(order.Type));
                    }
                }
            }

            public Planet GetPlanet(string planetName)
            {
                return GameAI.GetPlanet(planetName);
            }
            private void InitializeInputActions()
            {
                _mapInputActions = new MapInputActions();
            }

            private void OnEnable()
            {
                if (_mapInputActions != null)
                {
                    _mapInputActions.Enable();
                    _mapInputActions.MapActions.MapZoom.performed += OnScrollPerformed;
                    _mapInputActions.MapActions.OpenMainMenu.performed += OnOpenMainMenuButtonPressed;
                }
            }

            private void OnDisable()
            {
                _mapInputActions?.Disable();
            }

            #region Input
            private void OnScrollPerformed(InputAction.CallbackContext context)
            {
                var scrollValue = context.ReadValue<float>();
                if (scrollValue != 0)
                {
                    float targetSize = _camera.orthographicSize + (scrollValue * -_cameraOrthoStep);
                    _camera.orthographicSize = Math.Clamp(targetSize, _minCameraOrtho, _maxCameraOrtho);
                }
            }
            private void OnOpenMainMenuButtonPressed(InputAction.CallbackContext context)
            {
                Debug.Log("Open Main Menu");
                var gameButtonHandler = GetComponentInChildren<GameButtonHandler>();
                if (gameButtonHandler)
                    gameButtonHandler.EscapeButtonPressed();
            }
            #endregion
            #region Update Functions

            public void SingleUpdate()
            {
                // add Ai Actions here
                GameAI.GameAIUpdate();
                PlanetaryUIUpdate();
                BoardUIUpdate();
                TurnNumber++;

            }

            private void PlanetaryUIUpdate()
            {
                foreach (var planetUI in _planetUIObjects)
                {
                    planetUI.UIUpdate();
                }
            }

            private void BoardUIUpdate()
            {
                DisplayOrderGraphics(GameAI.CurrentAIOrders);
            }

            private bool _timedUpdateRunning = false;

            public void StartTimedUpdate()
            {
                _timedUpdateRunning = true;
                StartCoroutine(TimedUpdate(0.5f));
            }

            IEnumerator TimedUpdate(float waitTime)
            {
                while (_timedUpdateRunning)
                {
                    SingleUpdate();
                    Debug.Log("Timed Update");
                    yield return new WaitForSeconds(waitTime);
                    Debug.Log("Coroutine Looping");
                }
            }

            public void StopTimedUpdate()
            {
                _timedUpdateRunning = false;
            }

            #endregion
        }
    }
}
