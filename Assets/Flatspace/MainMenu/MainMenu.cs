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

    public void StartGame()
    {
        SaveLoadSystem.StartNewGame();
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
