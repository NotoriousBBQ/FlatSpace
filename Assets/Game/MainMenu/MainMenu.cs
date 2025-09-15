using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _saveButton;
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _optionsButton;

    public void StartGame()
    {
        SaveLoadSystem.LoadNewGame();
    }

    public void SaveGame()
    {
        SaveLoadSystem.SaveGame();
    }

    public void LoadGame()
    {
        SaveLoadSystem.LoadGame();
    }

    private void Awake()
    {
        if(_saveButton != null)
            _saveButton.interactable = false;
        if (_optionsButton != null)
            _optionsButton.interactable = false;
    }
}
