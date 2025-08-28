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
using UnityEngine.UI;

namespace FlatSpace
{
    namespace Game
    {
        public class Gameboard : MonoBehaviour
        {
            private static Gameboard _instance;
            public static Gameboard Instance => _instance;
            [SerializeField] private BoardConfiguration _intialBoardState;

            [SerializeField] private float _minCameraOrtho = 1;
            [SerializeField] private float _maxCameraOrtho = 15;
            [SerializeField] private float _cameraOrthoStep = 0.1f;
            [SerializeField] private GameAI _gameAI;
            [SerializeField] private LineDrawObject _lineDrawObjectPrefab;
            [SerializeField]private LineDrawObject _orderLineDrawObjectPrefab;

            public List<PlanetUIObject> planetUIObjects = new List<PlanetUIObject>();

            private MapInputActions _mapInputActions;
            private Camera _camera;

            private readonly List<Planet> _planetList = new List<Planet>();
            private readonly List<Planet.PlanetUpdateResult> _resultList = new List<Planet.PlanetUpdateResult>();
 
            private int _turnNumber = 0;

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
                _planetList.Clear();
                if (_intialBoardState)
                {
                    InitGame();
                }
                else
                {
                    CreateTestBoardState();
                }

                InitializeInputActions();

            }

            private void InitGame()
            {
                foreach (var planetSpawnData in _intialBoardState._planetSpawnData)
                {
                    var planet = this.AddComponent<Planet>() as Planet;
                    planet.Init(planetSpawnData, this.transform);
                    _planetList.Add(planet);

                }
                PathingSystem.Instance.InitializePathMap(_planetList);
                _gameAI = this.AddComponent<GameAI>() as GameAI;
                _gameAI.InitGameAI(_planetList);
                InitPathGraphics();
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
                    var lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        var point1 = _planetList.Find(x => x.PlanetName == order.Origin).Position;
                        var point2 = _planetList.Find(x => x.PlanetName == order.Target).Position;
                        var linePoints = (new Vector3(point1.x + 5.0f, point1.y + 5.0f, 0.0f), new Vector3(point2.x + 5.0f, point2.y + 5.0f, 0.0f) );
                        lineDrawObject.SetPoints(linePoints);
                    }
                }
            }

            private void CreateTestBoardState()
            {
                Planet planet = this.AddComponent<Planet>() as Planet;
                planet.Init(Planet.PlanetType.PlanetTypePrime, this.transform, new Vector3(0, 0, 0));
                _planetList.Add(planet);

                planet = this.AddComponent<Planet>() as Planet;
                planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(200, 0, 0));
                _planetList.Add(planet);

                planet = this.AddComponent<Planet>() as Planet;
                planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(-200, 0, 0));
                _planetList.Add(planet);

                planet = this.AddComponent<Planet>() as Planet;
                planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(0, 200, 0));
                _planetList.Add(planet);

                planet = this.AddComponent<Planet>() as Planet;
                planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(0, -200, 0));
                _planetList.Add(planet);

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
                    _mapInputActions.MapActions.MapButtonPress.performed += OnMapButtonPressPerformed;
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

            private void OnMapButtonPressPerformed(InputAction.CallbackContext context)
            {

                Debug.Log($"Button press");
            }
            #endregion
            #region Update Functions

            private void DEBUG_LogResults()
            {
                Debug.Log($"Turn: {_turnNumber} Results count: {_resultList.Count}");
                foreach (var result in _resultList)
                {
                    Debug.Log($"{result.Name}: {result.Result.ToString()} {result.Data?.ToString()}");
                }
            }

            public void SingleUpdate()
            {
                // add Ai Actions here
                _gameAI.GameAIUpdate(_planetList);
                PlanetaryUIUpdate();
                BoardUIUpdate();
                _turnNumber++;

            }

            private void PlanetaryUIUpdate()
            {
                foreach (var planet in _planetList)
                {
                    planet.UpdateMapUI();
                }
            }

            private void BoardUIUpdate()
            {
                var currentAIOrders = _gameAI.CurrentAIOrders;
                DisplayOrderGraphics(currentAIOrders);
            }

            private bool _timedUpdateRunning = false;

            public void StartTimedUpdate()
            {
                _timedUpdateRunning = true;
                StartCoroutine(TimedUpdate(1.0f));
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
