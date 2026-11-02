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
    [SerializeField] private string modelName = "gemmaChat:latest";
    // Seconds; generous because the first request after an idle spell has to load the model.
    [SerializeField] private int requestTimeoutSeconds = 60;

    private static ChatClient _instance;

    public static ChatClient Instance =>  _instance != null ? _instance : (_instance = Gameboard.Instance.gameObject.AddComponent<ChatClient>());

    public void RequestChatResponse(string message, string me, string player, Personality personality, DiplomacyState.Pair diplomacyState, Action<string> onReply = null)
    {
        var messageList = BuildMessages(me, player, personality, diplomacyState, message);
        StartCoroutine(SendChatRequestCoroutine(messageList, onReply));
    }

    public enum Personality { Aggressive, Fearful, Enigmatic }// personality types for npc
    public enum Emotion {Angry, Neutral, Happy }// emotional content for npc based on game state

    // The one place hostility (0..100) becomes a tone; the few-shot sample and the prompt's tone constraint both read it.
    private const float AngryFrom = 66f;
    private const float HappyBelow = 33f;

    public static Emotion EmotionOf(DiplomacyState.Pair diplomacyState)
    {
        if (diplomacyState.Hostility >= AngryFrom) return Emotion.Angry;
        if (diplomacyState.Hostility < HappyBelow) return Emotion.Happy;
        return Emotion.Neutral;
    }

    private static string GetPersonalityBlock(Personality personality)
    {
        switch (personality)
        {
            case Personality.Aggressive:
                return "You are angry use aggressive and confrontational terms. Your rage is barely kept in check";
            case Personality.Enigmatic:
                return
                    "Use astronomy or spiritual metaphors and profound zen-like phrasing to obfuscate your meaning. don't use 'river' or 'ocean'";
            case Personality.Fearful:
                return "Beg and plead and whine and cry.  You are very timid and easily frightened";
        }

        return "No Personality Found";

    }

    private static KeyValuePair<string, string> GetEmotionFromDiplomacyState(DiplomacyState.Pair diplomacyState)
    {
        const string question = "What are you going to do about it?";
        switch (EmotionOf(diplomacyState))
        {
            // needs to be emotion few shot
            case Emotion.Angry:
                return new KeyValuePair<string, string>(question, "I'll rip out your spleen!");
            case Emotion.Happy:
                return new KeyValuePair<string, string>(question, "I'm going to beg and plead and whine and cry");
            default:
                return new KeyValuePair<string, string>(question,
                    "Use astronomy or spiritual metaphors and profound zen-like phrasing to obfuscate your meaning. don't use 'river' or 'ocean'");
        }
    }

    private IEnumerator SendChatRequestCoroutine(List<OllamaMessage> messageList, Action<string> onReply)
    {
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
    public static List<OllamaMessage> BuildMessages(string me, string player, Personality personality,
        DiplomacyState.Pair diplomacyState, string message)
    {
        var dynamicSystemPrompt = GenerateDynamicSystemPrompt(me, player, diplomacyState);
        KeyValuePair<string, string> emotionExample = GetEmotionFromDiplomacyState(diplomacyState);
        var personalityBlock = GetPersonalityBlock(personality);
        var messageList = new List<OllamaMessage>()
        {
            new OllamaMessage{role = "system", content = dynamicSystemPrompt},
            // The structural training example (Few-Shot)
            new OllamaMessage { role = "user", content = emotionExample.Key },
            new OllamaMessage { role = "assistant", content = emotionExample.Value },
            new OllamaMessage { role = "user", content = personality.ToString() },
            new OllamaMessage { role = "assistant", content = personalityBlock},
            // the actual message
            new OllamaMessage{role = "user", content = message}
        };

        return messageList;
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
        switch (EmotionOf(diplomacyState))
        {
            case Emotion.Angry: systemPrompt += "Angry"; break;
            case Emotion.Happy: systemPrompt += "Happy"; break;
            default: systemPrompt += "Neutral"; break;
        }

        return systemPrompt;
    }
}
