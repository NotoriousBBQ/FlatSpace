using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _saveButton;
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _designerButton;
    [SerializeField] private bool _logAIEvents = false;

    // Template for random boards (type weights and geometry); player count, planet count and seed come from the popup.
    [SerializeField] private MapGenSettings _randomBoardSettings;

    private RandomBoardPopup _startPopup;

    public void StartGame()
    {
        if (_startPopup == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null && _startButton != null)
                canvas = _startButton.GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[MainMenu] no Canvas found for the start popup; starting from a saved board");
                SaveLoadSystem.StartNewGame();
                return;
            }
            _startPopup = RandomBoardPopup.Create(canvas.rootCanvas, _randomBoardSettings);
        }
        MatchSettings.Reset();
        _startPopup.Show();
    }

    public void SaveGame()
    {
        SaveLoadSystem.SaveGame();
    }

    public void LoadGame()
    {
        SaveLoadSystem.LoadGame();
    }

    public void OpenDesigner()
    {
        SaveLoadSystem.LoadDesigner();
    }

    private void Awake()
    {
        if(_saveButton != null)
            _saveButton.interactable = false;
        AITuningLogger.EnabledViaMainMenu = _logAIEvents;
    }
}
