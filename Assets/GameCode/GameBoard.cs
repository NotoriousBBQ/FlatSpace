using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
public class Gameboard : MonoBehaviour
{
    [SerializeField] 
    private string _intialBoardState;

    [SerializeField]
    private string _planetPrefab;

    [SerializeField] private float _minCameraSize = 1;
    [SerializeField] private float _maxCameraSize = 15;
    [SerializeField] private float _cameraSizeStep = 0.1f;
   
    private MapInputActions _mapInputActions;
    private Camera _camera;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
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
        if (string.IsNullOrEmpty(_intialBoardState))
        {
            CreateDefaultBoardState();
        }

        InitializeInputActions();

    }
    
    private void CreateDefaultBoardState()
    {
        if (!string.IsNullOrEmpty(_planetPrefab))
        {
            var prefab = AssetDatabase.LoadAssetAtPath(_planetPrefab, typeof(GameObject)) as GameObject;
            prefab.transform.localPosition = new Vector3(100, 100, 0);
            Debug.Log("Prefab local pos " + prefab.transform.localPosition);
            var planetUi = Instantiate(prefab, this.transform);
            planetUi.transform.localPosition = new Vector3(100, 100, 0);
            Debug.Log("GO local pos " + planetUi.transform.localPosition);
        }
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