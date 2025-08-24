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
        Gameboard.Instance.TriggerSingleUpdate();
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
}
