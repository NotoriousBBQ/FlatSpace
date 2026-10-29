using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using FlatSpace.AI;
using FlatSpace.Game;
using Unity.VisualScripting;
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

    private static ChatClient _instance;

    public static ChatClient Instance =>  _instance != null ? _instance : (_instance = Gameboard.Instance.gameObject.AddComponent<ChatClient>());

    public void RequestChatResponse(string message, string me, string player, DiplomacyState.Pair diplomacyState)
    {
        var dynamicSystemPrompt = GenerateDynamicSystemPrompt(me, player, diplomacyState);
        StartCoroutine(SendChatRequestCoroutine(message, dynamicSystemPrompt, diplomacyState));
    }

    private KeyValuePair<string, string> GetToneFromDiplomacyState(DiplomacyState.Pair diplomacyState)
    {
        new KeyValuePair<string, string>();
        if (diplomacyState.Hostility >= 66)
        {
            // aggressive
            return new KeyValuePair<string, string>(
                "What are you going to do about it?", 
                "I'll rip out your spleen!"
                );
        }
        else if (diplomacyState.Hostility < 33)
        {            
            // passive
            return new KeyValuePair<string, string>(
                "What are you going to do about it?", 
                "I'm going to beg and plead and whine and cry"
            );
        }
        // enigmatic
        return new KeyValuePair<string, string>(
            "What are you going to do about it?", 
            "The wind blows gently across the surface of the pond, splashing the frog in its face"
        );

    }

    private IEnumerator SendChatRequestCoroutine(string message, string dynamicSystemPrompt, DiplomacyState.Pair diplomacyState)
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

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                OllamaResponse responseData = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
                Debug.Log($"<b>NPC Dynamic Reply:</b> {responseData.message.content}");
            }
        }

    }

    private static string GenerateDynamicSystemPrompt(string me, string player, DiplomacyState.Pair diplomacyState)
    {
        
        string systemPrompt = "\n\nCURRENT SITUATION CONTEXT:\n";
        systemPrompt += $"- You are {me}, talking to {player}\n";
        systemPrompt += $"- Your current hostility score with the player is: {diplomacyState.Hostility}. Do not state your hostility directly, use it to guide your word choice\n";
        systemPrompt += $"- Your current stance with the player is: {diplomacyState.Stance}. Do not state your stance directly, use it to guide your word choice\n";
        systemPrompt += "- You must strictly maintain your designated tone constraint while addressing this relationship and requested response.\n";
        systemPrompt += "- Do not reuse any sequence of more than 3 words from your previous responses when constructing this one\n";
        systemPrompt += "\n";
        systemPrompt += "SYSTEM:\n";
        systemPrompt += "You are an advanced NPC dialogue generation engine operating inside a game loop.\n";
        systemPrompt +=
            "Your core priority is to process the tone and game variables provided in the system context and output speech that completely embodies the assigned tone profile.\n";
        systemPrompt += "\n";
        systemPrompt += "CRITICAL RULES:\n";
        systemPrompt += "1. Output ONLY the literal spoken dialogue of the character.\n";
        systemPrompt += "2. Never include meta-commentary, introductory text, or explanatory footnotes (e.g., Do NOT say 'Here is an aggressive response:'').\n";
        systemPrompt += "3. Do not break character under any circumstances.\n"; 
        systemPrompt += "4. Keep responses punchy, direct, and under 3 structural sentences.\n";
        systemPrompt += "\n";
        systemPrompt += "CURRENT TONE CONSTRAINT:\n";
        if (diplomacyState.Hostility >= 66)
            systemPrompt += "Hostility";
        else if (diplomacyState.Hostility < 33)
            systemPrompt += "Fear";
        else
            systemPrompt += "Enigmatic";        

        return systemPrompt;
    }
}
