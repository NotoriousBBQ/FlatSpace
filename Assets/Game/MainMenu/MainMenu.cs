using UnityEngine;
public class MainMenu : MonoBehaviour
{

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
}
