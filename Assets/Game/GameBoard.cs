using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
public class Gameboard : MonoBehaviour
{
    [SerializeField] 
    private string _intialBoardState;

    [SerializeField] private float _minCameraSize = 1;
    [SerializeField] private float _maxCameraSize = 15;
    [SerializeField] private float _cameraSizeStep = 0.1f;
   
    private MapInputActions _mapInputActions;
    private Camera _camera;
    
    private List<Planet> _planetList = new List<Planet>();
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void Awake()
    {
        _camera = Camera.main;
        _planetList.Clear();
        if (string.IsNullOrEmpty(_intialBoardState))
        {
            CreateDefaultBoardState();
        }

        InitializeInputActions();

    }
    
    private void CreateDefaultBoardState()
    {
        Planet planet1 = this.AddComponent<Planet>() as Planet;
        planet1.Init(Planet.PlanetType.PlanetTypePrime, this.transform, new Vector3(0,0,0));
        _planetList.Add(planet1);
        
        Planet planet2 = this.AddComponent<Planet>() as Planet;
        planet2.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(200,0,0));
        _planetList.Add(planet2);

        Planet planet3 = this.AddComponent<Planet>() as Planet;
        planet3.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(-200,0,0));
        _planetList.Add(planet3);

        Planet planet4 = this.AddComponent<Planet>() as Planet;
        planet4.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(0,200,0));
        _planetList.Add(planet4);

        Planet planet5 = this.AddComponent<Planet>() as Planet;
        planet5.Init(Planet.PlanetType.PlanetTypeNormal, this.transform, new Vector3(0,-200,0));
        _planetList.Add(planet5);
    
/*       var prefab = AssetDatabase.LoadAssetAtPath(_planetPrefab, typeof(GameObject)) as GameObject;
       prefab.transform.localPosition = new Vector3(100, 100, 0);
       Debug.Log("Prefab local pos " + prefab.transform.localPosition);
       var planetUi = Instantiate(prefab, this.transform);
       planetUi.transform.localPosition = new Vector3(100, 100, 0);
       Debug.Log("GO local pos " + planetUi.transform.localPosition);
   }*/
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
        }
    }

    private void OnDisable()
    {
        _mapInputActions.Disable();
    }

    private void OnScrollPerformed(InputAction.CallbackContext context)
    {
        var scrollValue = context.ReadValue<float>();
        if (scrollValue != 0)
        {
            float targetSize = _camera.orthographicSize + (scrollValue * -_cameraSizeStep);
            _camera.orthographicSize = Math.Clamp(targetSize, _minCameraSize, _maxCameraSize);
            Debug.Log($"ortho size " + _camera.orthographicSize);
        }
    }
}