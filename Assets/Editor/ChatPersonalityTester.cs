using System.Collections.Generic;
using System.Text;
using FlatSpace.AI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// FlatSpace -> Chat -> Personality Tester. Pick a personality and an emotion, type a message and read the model's reply, without starting a match.
/// It builds the messages with <see cref="ChatClient.BuildMessages"/> (the same call the game makes) and posts them to Ollama itself,
/// polling the request from EditorApplication.update, so it works outside Play mode. The emotion is turned into a hostility inside its band,
/// because ChatClient derives the emotion from the pair's hostility.
/// </summary>
public class ChatPersonalityTester : EditorWindow
{
    // Defaults match ChatClient's serialized fields (they are private there); edit them here if the game's differ.
    private string _url = "http://localhost:11434/api/chat";
    private string _model = "gemmaChat:latest";
    private int _timeoutSeconds = 60;

    private ChatClient.Personality _personality = ChatClient.Personality.Enigmatic;
    private ChatClient.Emotion _emotion = ChatClient.Emotion.Neutral;
    private Stance _stance = Stance.Peace;
    private string _me = "Player 1";
    private string _player = "Player 0";
    private string _message = "What are you going to do about it?";

    private string _reply = "";
    private string _status = "";
    private string _sentPrompt = "";
    private bool _showPrompt;
    private Vector2 _scroll;

    private UnityWebRequest _request;
    private double _startedAt;

    [MenuItem("FlatSpace/Chat/Personality Tester")]
    public static void Open() => GetWindow<ChatPersonalityTester>("Personality Tester");

    // A hostility in the middle of each band; the band edges live in ChatClient.EmotionOf.
    private static float HostilityFor(ChatClient.Emotion emotion)
    {
        switch (emotion)
        {
            case ChatClient.Emotion.Angry: return 80f;
            case ChatClient.Emotion.Happy: return 10f;
            default: return 50f;
        }
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);
        _personality = (ChatClient.Personality)EditorGUILayout.EnumPopup("Personality", _personality);
        _emotion = (ChatClient.Emotion)EditorGUILayout.EnumPopup("Emotion", _emotion);
        _stance = (Stance)EditorGUILayout.EnumPopup("Stance", _stance);
        _me = EditorGUILayout.TextField("Speaker (me)", _me);
        _player = EditorGUILayout.TextField("Spoken to", _player);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Message", EditorStyles.boldLabel);
        _message = EditorGUILayout.TextArea(_message, GUILayout.MinHeight(48));

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_request != null))
        {
            if (GUILayout.Button(_request != null ? "Waiting for reply..." : "Send", GUILayout.Height(28))) Send();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Reply", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(_reply, EditorStyles.textArea, GUILayout.MinHeight(64));
        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

        _showPrompt = EditorGUILayout.Foldout(_showPrompt, "Messages sent", true);
        if (_showPrompt) EditorGUILayout.SelectableLabel(_sentPrompt, EditorStyles.textArea, GUILayout.MinHeight(220));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
        _url = EditorGUILayout.TextField("Ollama URL", _url);
        _model = EditorGUILayout.TextField("Model", _model);
        _timeoutSeconds = EditorGUILayout.IntField("Timeout (s)", _timeoutSeconds);

        EditorGUILayout.EndScrollView();
    }

    private void Send()
    {
        var pair = new DiplomacyState.Pair { Stance = _stance, Hostility = HostilityFor(_emotion) };
        var messages = ChatClient.BuildMessages(_me, _player, _personality, pair, _message);

        var payload = new OllamaChatPayload { model = _model, stream = false, messages = messages };
        _sentPrompt = Describe(messages);
        _reply = "";
        _status = "Sent...";

        _request = new UnityWebRequest(_url, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = _timeoutSeconds,
        };
        _request.SetRequestHeader("Content-Type", "application/json");
        _request.SendWebRequest();
        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update += Poll;
    }

    private void Poll()
    {
        if (_request == null || !_request.isDone) return;

        EditorApplication.update -= Poll;
        var seconds = EditorApplication.timeSinceStartup - _startedAt;
        if (_request.result != UnityWebRequest.Result.Success)
        {
            _status = $"Failed ({_request.result}) after {seconds:F1}s: {_request.error} {_request.downloadHandler?.text}";
        }
        else
        {
            var response = JsonUtility.FromJson<OllamaResponse>(_request.downloadHandler.text);
            if (response?.message == null || string.IsNullOrEmpty(response.message.content))
                _status = $"No message content ({seconds:F1}s): {_request.downloadHandler.text}";
            else
            {
                _reply = response.message.content.Trim();
                _status = $"OK in {seconds:F1}s";
            }
        }

        _request.Dispose();
        _request = null;
        Repaint();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Poll;
        if (_request != null)
        {
            _request.Abort();
            _request.Dispose();
            _request = null;
        }
    }

    private static string Describe(List<OllamaMessage> messages)
    {
        var text = new StringBuilder();
        foreach (var m in messages) text.Append('[').Append(m.role).Append("]\n").Append(m.content).Append("\n\n");
        return text.ToString();
    }
}
