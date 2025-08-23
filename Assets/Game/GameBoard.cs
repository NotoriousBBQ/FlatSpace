using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Gameboard : MonoBehaviour
{
    private static Gameboard _instance;
    public static Gameboard Instance => _instance;
    [SerializeField] 
    private BoardConfiguration _intialBoardState;

    [SerializeField] private float _minCameraSize = 1;
    [SerializeField] private float _maxCameraSize = 15;
    [SerializeField] private float _cameraSizeStep = 0.1f;

    private MapInputActions _mapInputActions;
    private Camera _camera;
    
    private List<Planet> _planetList = new List<Planet>();
    private List<Planet.PlanetUpdateResult> _resultList = new List<Planet.PlanetUpdateResult>(); 
    
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
            InitBoard();
        }
        else
        {
            CreateTestBoardState(); 
        }

        InitializeInputActions();

    }

    private void InitBoard()
    {
        foreach (var planetSpawnData in _intialBoardState._planetSpawnData)
        {
            Planet planet = this.AddComponent<Planet>() as Planet;
            planet.Init(planetSpawnData, this.transform);
            _planetList.Add(planet);
            
        }
    }
    
    private void CreateTestBoardState()
    {
        Planet planet = this.AddComponent<Planet>() as Planet;
        planet.Init(Planet.PlanetType.PlanetTypePrime, this.transform, new Vector3(0,0,0));
        _planetList.Add(planet);
        
        planet = this.AddComponent<Planet>() as Planet;
        planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(200,0,0));
        _planetList.Add(planet);

        planet = this.AddComponent<Planet>() as Planet;
        planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(-200,0,0));
        _planetList.Add(planet);

        planet = this.AddComponent<Planet>() as Planet;
        planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(0,200,0));
        _planetList.Add(planet);

        planet = this.AddComponent<Planet>() as Planet;
        planet.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(0,-200,0));
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

    private void OnScrollPerformed(InputAction.CallbackContext context)
    {
        var scrollValue = context.ReadValue<float>();
        if (scrollValue != 0)
        {
            float targetSize = _camera.orthographicSize + (scrollValue * -_cameraSizeStep);
            _camera.orthographicSize = Math.Clamp(targetSize, _minCameraSize, _maxCameraSize);
        }
    }

    private void OnMapButtonPressPerformed(InputAction.CallbackContext context)
    {
        
        Debug.Log($"Button press");
    }

    #region Update Functions

    private void DEBUG_LogResults()
    {
        Debug.Log($"Turn: {_turnNumber} Results count: {_resultList.Count}");
        foreach (var result in _resultList)
        {
            Debug.Log($"{result._name}: {result._resultType.ToString()} {result._resultData?.ToString()}");
        }
    }
    public void TriggerSingleUpdate()
    {
        PlanetaryUpdate();

        DEBUG_LogResults();
        // add Ai Actions here
        
        PlanetUIUpdate();
        _turnNumber++;

    }

    private void PlanetaryUpdate()
    {
        _resultList.Clear();
        foreach (Planet planet in _planetList)
        {
            planet.PlanetUpdate(_resultList);
        }
    }

    private void PlanetUIUpdate()
    {
        foreach (Planet planet in _planetList)
        {
            planet.UpdateMapUI();
        }
    }

    private bool _timedUpdateRunning = false;
    public void StartTimedUpdate()
    {
        _timedUpdateRunning = true;
        StartCoroutine(TimedUpdate(2.0f));
    }

    IEnumerator TimedUpdate(float waitTime)
    {
        while (_timedUpdateRunning)
        {
            TriggerSingleUpdate();
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