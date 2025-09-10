using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using FlatSpace.Game;
public class GameButtonHandler : MonoBehaviour
{ 
    public UIDocument uiDocument;
    
    private Button _nextTurnButton;
    private Button _startRunButton;
    private Button _stopRunButton;
    private Button _testSaveButton;
    private Button _testLoadButton;
   
    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;

        _nextTurnButton = root.Q<Button>("NextTurnButton"); 
        if (_nextTurnButton != null)
        {
            _nextTurnButton.clicked += OnNextTurnButtonClicked;
        }

        _startRunButton = root.Q<Button>("StartRunButton"); 
        if (_startRunButton != null)
        {
            _startRunButton.clicked += OnStartRunButtonClicked;
        }
        
        _stopRunButton = root.Q<Button>("StopRunButton"); 
        if (_stopRunButton != null)
        {
            _stopRunButton.clicked += OnStopRunButtonClicked;
            _stopRunButton.SetEnabled(false);
        }
        
        _testSaveButton = root.Q<Button>("TestSaveButton"); 
        if (_testSaveButton != null)
        {
            _testSaveButton.clicked += OnTestSaveButtonClicked;
        }
        
        _testLoadButton = root.Q<Button>("TestLoadButton"); 
        if (_testLoadButton != null)
        {
            _testLoadButton.clicked += OnTestLoadButtonClicked;
        }

    }

    void OnDisable()
    {
        if (_nextTurnButton != null)
        {
            _nextTurnButton.clicked -= OnNextTurnButtonClicked;
        }
        
        if (_startRunButton != null)
        {
            _startRunButton.clicked -= OnStartRunButtonClicked;
        }
        
        if (_stopRunButton != null)
        {
            _stopRunButton.clicked -= OnStopRunButtonClicked;
        }
        
        if (_testSaveButton != null)
        {
            _testSaveButton.clicked -= OnTestSaveButtonClicked;
        }
        
        if (_testLoadButton != null)
        {
            _testLoadButton.clicked -= OnTestLoadButtonClicked;
        }
    }

    private void OnNextTurnButtonClicked()
    {
        Gameboard.Instance.SingleUpdate();
    }

    private void OnStartRunButtonClicked()
    {
        _nextTurnButton.SetEnabled(false);
        _startRunButton.SetEnabled(false);
        _stopRunButton.SetEnabled(true);
        Gameboard.Instance.StartTimedUpdate();
    }

    private void OnStopRunButtonClicked()
    {
        _nextTurnButton.SetEnabled(true);
        _startRunButton.SetEnabled(true);
        _stopRunButton.SetEnabled(false);
        Gameboard.Instance.StopTimedUpdate();
    }

    private static void OnTestSaveButtonClicked()
    {
        SaveLoadSystem.Instance.SaveGame(Gameboard.Instance.GameAI, @"C:\Temp\TestSave.json");
    }

    private static  void OnTestLoadButtonClicked()
    {
        SaveLoadSystem.Instance.LoadGame(Gameboard.Instance.GameAI, @"C:\Temp\TestSave.json");
    }
}
