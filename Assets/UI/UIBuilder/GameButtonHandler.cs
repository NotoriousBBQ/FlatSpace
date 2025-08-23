using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
public class GameButtonHandler : MonoBehaviour
{ 
    public UIDocument uiDocument;
    
    private Button _nextTurnButton;
    private Button _startRunButton;
    private Button _stopRunButton;
    
    public Gameboard _gameboard;
    
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
    }

    private void OnNextTurnButtonClicked()
    {
        _gameboard.TriggerSingleUpdate();
    }

    private void OnStartRunButtonClicked()
    {
        _nextTurnButton.SetEnabled(false);
        _startRunButton.SetEnabled(false);
        _stopRunButton.SetEnabled(true);
        _gameboard.StartTimedUpdate();
    }

    private void OnStopRunButtonClicked()
    {
        _nextTurnButton.SetEnabled(true);
        _startRunButton.SetEnabled(true);
        _stopRunButton.SetEnabled(false);
        _gameboard.StopTimedUpdate();
    }
}
