using FlatSpace.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// The two start-game popups, built in code under the menu's Canvas (no scene or prefab edits):
// "Use Saved Board" (Yes / No) and, after No, the random board settings (OK / Back).
// OK generates a board from the seed and starts the game on it through the same path a picked
// board config takes (SaveLoadSystem.StartGameFromBoard); the designer scene is never loaded.
public class RandomBoardPopup : MonoBehaviour
{
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color Box = new Color(0.15f, 0.15f, 0.18f, 1f);
    private static readonly Color Field = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color ButtonColor = new Color(0.3f, 0.35f, 0.5f, 1f);

    private MapGenSettings _settings;
    private GameObject _savedBoardPanel;
    private GameObject _randomPanel;
    private TMP_InputField _playersField;
    private TMP_InputField _planetsField;
    private TMP_InputField _seedField;
    private TextMeshProUGUI _errorLabel;

    public static RandomBoardPopup Create(Canvas canvas, MapGenSettings settings)
    {
        var go = new GameObject("RandomBoardPopup", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)go.transform);
        var popup = go.AddComponent<RandomBoardPopup>();
        popup._settings = settings;
        popup.Build();
        return popup;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        ShowSavedBoardPanel();
    }

    private void Hide() => gameObject.SetActive(false);

    private void ShowSavedBoardPanel()
    {
        _randomPanel.SetActive(false);
        _savedBoardPanel.SetActive(true);
    }

    private void ShowRandomPanel()
    {
        _playersField.text = RandomBoardBuilder.DefaultPlayers.ToString();
        _planetsField.text = RandomBoardBuilder.DefaultPlanets.ToString();
        _seedField.text = RandomBoardBuilder.NewSeed().ToString();
        _errorLabel.text = "";
        _savedBoardPanel.SetActive(false);
        _randomPanel.SetActive(true);
    }

    private void OnYes()
    {
        Hide();
        SaveLoadSystem.StartNewGame();
    }

    private void OnOk()
    {
        // Clamp what was typed and show the result, so the player sees the values actually used.
        var players = RandomBoardBuilder.ClampPlayers(RandomBoardBuilder.ParseInt(_playersField.text, RandomBoardBuilder.DefaultPlayers));
        var planets = RandomBoardBuilder.ClampPlanets(RandomBoardBuilder.ParseInt(_planetsField.text, RandomBoardBuilder.DefaultPlanets));
        var seed = RandomBoardBuilder.ResolveSeed(RandomBoardBuilder.ParseInt(_seedField.text, 0));
        _playersField.text = players.ToString();
        _planetsField.text = planets.ToString();
        _seedField.text = seed.ToString();

        var result = RandomBoardBuilder.Generate(_settings, players, planets, seed);
        if (!result.Success)
        {
            _errorLabel.text = $"Could not generate a board: {result.Error}";
            return;
        }

        var save = RandomBoardBuilder.ToBoardSave(result);
        string path;
        try
        {
            path = RandomBoardBuilder.WriteBoardFile(save, RandomBoardBuilder.BoardDirectory(),
                RandomBoardBuilder.FileName(result.EffectiveSeed, players, result.Planets.Count));
        }
        catch (System.Exception e)
        {
            _errorLabel.text = $"Could not write the board file: {e.Message}";
            return;
        }

        Hide();
        SaveLoadSystem.StartGameFromBoard(path, save);
    }

    #region Building the UI

    private void Build()
    {
        _savedBoardPanel = MakePanel("SavedBoardPanel", 420f, 200f, out var savedBox);
        AddLabel(savedBox, "Use Saved Board", 32f, 28f);
        var buttons = AddRow(savedBox, 56f);
        AddButton(buttons, "Yes", OnYes);
        AddButton(buttons, "No", ShowRandomPanel);

        _randomPanel = MakePanel("RandomBoardPanel", 520f, 380f, out var randomBox);
        AddLabel(randomBox, "Random Board", 32f, 28f);
        _playersField = AddField(randomBox, "Number of Players");
        _planetsField = AddField(randomBox, "Number of Planets");
        _seedField = AddField(randomBox, "Seed");
        _errorLabel = AddLabel(randomBox, "", 40f, 18f);
        _errorLabel.color = new Color(1f, 0.5f, 0.5f);
        var randomButtons = AddRow(randomBox, 56f);
        AddButton(randomButtons, "OK", OnOk);
        AddButton(randomButtons, "Back", ShowSavedBoardPanel);

        ShowSavedBoardPanel();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // A full-screen dimmer (it also swallows clicks meant for the menu behind) holding a centred box.
    private GameObject MakePanel(string panelName, float width, float height, out Transform box)
    {
        var dim = new GameObject(panelName, typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(transform, false);
        Stretch((RectTransform)dim.transform);
        dim.GetComponent<Image>().color = Dim;

        var boxObject = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        boxObject.transform.SetParent(dim.transform, false);
        var rect = (RectTransform)boxObject.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        boxObject.GetComponent<Image>().color = Box;
        var layout = boxObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        box = boxObject.transform;
        return dim;
    }

    private static TextMeshProUGUI AddLabel(Transform parent, string text, float height, float fontSize)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        return label;
    }

    private static Transform AddRow(Transform parent, float height)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        return go.transform;
    }

    private static void AddButton(Transform parent, string text, UnityAction onClick)
    {
        var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = ButtonColor;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var label = AddLabel(go.transform, text, 0f, 24f);
        Destroy(label.GetComponent<LayoutElement>());
        Stretch((RectTransform)label.transform);
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }

    // A labelled integer field: label on the left, input on the right.
    private static TMP_InputField AddField(Transform parent, string labelText)
    {
        var row = AddRow(parent, 44f);
        var label = AddLabel(row, labelText, 0f, 22f);
        Destroy(label.GetComponent<LayoutElement>());
        label.alignment = TextAlignmentOptions.MidlineLeft;

        var go = new GameObject(labelText + " Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(row, false);
        go.GetComponent<Image>().color = Field;

        var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(go.transform, false);
        var viewportRect = (RectTransform)viewport.transform;
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(8f, 4f);
        viewportRect.offsetMax = new Vector2(-8f, -4f);

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(viewport.transform, false);
        Stretch((RectTransform)textObject.transform);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = 22f;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = viewportRect;
        input.textComponent = text;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 10;
        // The field was enabled before its text parts were assigned; re-enable so it wires them up.
        input.enabled = false;
        input.enabled = true;
        return input;
    }

    #endregion
}
