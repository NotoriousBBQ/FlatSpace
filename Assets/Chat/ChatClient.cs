using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using FlatSpace.AI;
using FlatSpace.Game;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class OllamaMessage
{
    public string role; // "system", "user", or "assistant"
    public string content;
}

[Serializable]
public class OllamaChatPayload
{
    public string model;
    public List<OllamaMessage> messages;
    public bool stream;
}

// Data structure to parse the response coming back from Ollama
[Serializable]
public class OllamaResponse
{
    public OllamaMessage message;
}

public class ChatClient : MonoBehaviour
{   
    [Header("API Settings")]
    [SerializeField] private string ollamaUrl = "http://localhost:11434/api/chat";
    [SerializeField] private string modelName = "qwenChat:latest";
    // Seconds; generous because the first request after an idle spell has to load the model.
    [SerializeField] private int requestTimeoutSeconds = 60;

    private static ChatClient _instance;

    public static ChatClient Instance =>  _instance != null ? _instance : (_instance = Gameboard.Instance.gameObject.AddComponent<ChatClient>());

    public void RequestChatResponse(string message, string me, string player, DiplomacyState.Pair diplomacyState, Action<string> onReply = null)
    {
        var dynamicSystemPrompt = GenerateDynamicSystemPrompt(me, player, diplomacyState);
        StartCoroutine(SendChatRequestCoroutine(message, dynamicSystemPrompt, diplomacyState, onReply));
    }

    private enum Tone { Hostile, Fearful, Enigmatic }

    // The one place hostility (0..100) becomes a tone; the few-shot sample and the prompt's tone constraint both read it.
    private const float HostileFrom = 66f;
    private const float FearfulBelow = 33f;

    private static Tone ToneOf(DiplomacyState.Pair diplomacyState)
    {
        if (diplomacyState.Hostility >= HostileFrom) return Tone.Hostile;
        if (diplomacyState.Hostility < FearfulBelow) return Tone.Fearful;
        return Tone.Enigmatic;
    }

    private KeyValuePair<string, string> GetToneFromDiplomacyState(DiplomacyState.Pair diplomacyState)
    {
        const string question = "What are you going to do about it?";
        switch (ToneOf(diplomacyState))
        {
            case Tone.Hostile:
                return new KeyValuePair<string, string>(question, "I'll rip out your spleen!");
            case Tone.Fearful:
                return new KeyValuePair<string, string>(question, "I'm going to beg and plead and whine and cry");
            default:
                return new KeyValuePair<string, string>(question,
                    "The wind blows gently across the surface of the pond, splashing the frog in its face.");
        }
    }

    private IEnumerator SendChatRequestCoroutine(string message, string dynamicSystemPrompt, DiplomacyState.Pair diplomacyState, Action<string> onReply)
    {
        KeyValuePair<string, string> toneExample = GetToneFromDiplomacyState(diplomacyState);
        var messageList = new List<OllamaMessage>()
        {
            new OllamaMessage{role = "system", content = dynamicSystemPrompt},
            // The structural training example (Few-Shot)
            new OllamaMessage { role = "user", content = toneExample.Key },
            new OllamaMessage { role = "assistant", content = toneExample.Value },
            // the actual message
            new OllamaMessage{role = "user", content = message}
        };

        var payload = new OllamaChatPayload
        {
            model = modelName,
            stream = false,
            messages = messageList,
        };
        
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"ChatClient: request to {ollamaUrl} failed ({request.result}): {request.error} {request.downloadHandler?.text}");
                yield break;
            }

            OllamaResponse responseData = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
            if (responseData?.message == null || string.IsNullOrEmpty(responseData.message.content))
            {
                Debug.LogWarning($"ChatClient: reply had no message content: {request.downloadHandler.text}");
                yield break;
            }

            Debug.Log($"<b>NPC Dynamic Reply:</b> {responseData.message.content}");
            onReply?.Invoke(responseData.message.content.Trim());
        }

    }

    private static string GenerateDynamicSystemPrompt(string me, string player, DiplomacyState.Pair diplomacyState)
    {
        
        string systemPrompt = "\n\nCURRENT SITUATION CONTEXT:\n";
        systemPrompt += $"- You are {me}, talking to {player}\n";
        systemPrompt += $"- Your current hostility score with the player is: {diplomacyState.Hostility}. Do not state your hostility directly, use it to guide your word choice\n";
        systemPrompt += $"- Your current stance with the player is: {diplomacyState.Stance}. Do not state your stance directly, use it to guide your word choice\n";
        systemPrompt += "- You must strictly maintain your designated tone constraint while addressing this relationship and requested response.\n";
        systemPrompt += "\n";
        systemPrompt += "SYSTEM:\n";
        systemPrompt += "You are an advanced NPC dialogue generation engine operating inside a game loop.\n";
        systemPrompt +=
            "Your core priority is to process the tone and game variables provided in the system context and output speech that completely embodies the assigned tone profile.\n";
        systemPrompt += "\n";
        systemPrompt += "CRITICAL RULES:\n";
        systemPrompt += "1. Output ONLY the literal spoken dialogue of the character.\n";
        systemPrompt += "2. Never include meta-commentary, introductory text, or explanatory footnotes (e.g., Do NOT say 'Here is an aggressive response:').\n";
        systemPrompt += "3. Do not break character under any circumstances.\n"; 
        systemPrompt += "4. Keep responses punchy, direct, and under 3 structural sentences.\n";
        systemPrompt += "\n";
        systemPrompt += "CURRENT TONE CONSTRAINT:\n";
        switch (ToneOf(diplomacyState))
        {
            case Tone.Hostile: systemPrompt += "Hostility"; break;
            case Tone.Fearful: systemPrompt += "Fear"; break;
            default: systemPrompt += "Enigmatic"; break;
        }

        return systemPrompt;
    }
}
