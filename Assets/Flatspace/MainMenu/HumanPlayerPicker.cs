using System;
using System.Collections.Generic;
using UnityEngine;

// The popup shown after a board loads when the match setup asked for a human player: one button per player, labelled with
// that player's home planet, built in code under the game's Canvas (same style as RandomBoardPopup).
public class HumanPlayerPicker : MonoBehaviour
{
    private Action<int> _onPicked;

    public static HumanPlayerPicker Show(Canvas canvas, IReadOnlyList<(int id, string homePlanet)> players, Action<int> onPicked)
    {
        // Its own canvas on top of everything (the board's planet markers and the HUD would otherwise draw over a popup parented to the game canvas).
        var canvasObject = new GameObject("HumanPlayerPickerCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var topCanvas = canvasObject.GetComponent<Canvas>();
        topCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        topCanvas.sortingOrder = 5000;
        var go = new GameObject("HumanPlayerPicker", typeof(RectTransform));
        go.transform.SetParent(canvasObject.transform, false);
        RandomBoardPopup.Stretch((RectTransform)go.transform);
        var picker = go.AddComponent<HumanPlayerPicker>();
        picker._onPicked = onPicked;
        picker.Build(players);
        return picker;
    }

    private void Build(IReadOnlyList<(int id, string homePlanet)> players)
    {
        RandomBoardPopup.MakePanel(transform, "PickerPanel", 460f, 130f + players.Count * 60f, out var box, dimAlpha: 1f);   // opaque: the board behind is not yet the player's
        RandomBoardPopup.AddLabel(box, "Choose your player", 36f, 28f);
        foreach (var (id, homePlanet) in players)
        {
            var playerId = id;
            var row = RandomBoardPopup.AddRow(box, 48f);
            var label = string.IsNullOrEmpty(homePlanet) ? $"Player {playerId}" : $"Player {playerId}  (home: {homePlanet})";
            RandomBoardPopup.AddButton(row, label, () => Pick(playerId));
        }
    }

    private void Pick(int playerId)
    {
        Destroy(transform.parent != null ? transform.parent.gameObject : gameObject);
        _onPicked?.Invoke(playerId);
    }
}
