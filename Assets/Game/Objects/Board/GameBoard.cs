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

            private readonly List<PlanetUIObject> _planetUIObjects = new List<PlanetUIObject>();
 
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
                _planetUIObjects.Clear();
                if (_intialBoardState)
                {
                    InitGame();
                }
                InitializeInputActions();

            }

            private void InitGame()
            {
                _gameAI = this.AddComponent<GameAI>() as GameAI;
                _gameAI.InitGameAI(_intialBoardState._planetSpawnData);

                InitPlanetGraphics(_intialBoardState._planetSpawnData);
                InitPathGraphics();
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
                var prefab = Gameboard.Instance.planetUIObjects[(int)spawnData._planetType];
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
                        var point1 = _planetUIObjects.Find(x => x._planetName == order.Origin).transform.localPosition;
                        var point2 = _planetUIObjects.Find(x => x._planetName == order.Target).transform.localPosition;
                        var linePoints = (new Vector3(point1.x + 5.0f, point1.y + 5.0f, 0.0f), new Vector3(point2.x + 5.0f, point2.y + 5.0f, 0.0f) );
                        lineDrawObject.SetPoints(linePoints);
                    }
                }
            }

            public Planet GetPlanet(string planetName)
            {
                return _gameAI.GetPlanet(planetName);
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

            public void SingleUpdate()
            {
                // add Ai Actions here
                _gameAI.GameAIUpdate();
                PlanetaryUIUpdate();
                BoardUIUpdate();
                _turnNumber++;

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
                DisplayOrderGraphics(_gameAI.CurrentAIOrders);
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
