
using UnityEngine;

// A one-button message over the game board (defeat, turn limit reached), built in code like the other popups. It does not stop the
// game: the player can close it and keep watching.
public class MatchEndPopup : MonoBehaviour
{
    public static MatchEndPopup Show(Canvas canvas, string title, string message)
    {
        var go = new GameObject("MatchEndPopup", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        RandomBoardPopup.Stretch((RectTransform)go.transform);
        var popup = go.AddComponent<MatchEndPopup>();
        RandomBoardPopup.MakePanel(go.transform, "MatchEndPanel", 460f, 220f, out var box);
        RandomBoardPopup.AddLabel(box, title, 40f, 30f);
        RandomBoardPopup.AddLabel(box, message, 60f, 22f);
        RandomBoardPopup.AddButton(RandomBoardPopup.AddRow(box, 48f), "Close", popup.Close);
        return popup;
    }

    private void Close() => Destroy(gameObject);
}
