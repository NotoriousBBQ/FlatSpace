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

    // The one place hostility (0..100) becomes an emotion; the few-shot sample and the prompt's emotion block both read it.
    private const float AngryFrom = 66f;
    private const float HappyBelow = 33f;

    public static Emotion EmotionOf(DiplomacyState.Pair diplomacyState)
    {
        if (diplomacyState.Hostility >= AngryFrom) return Emotion.Angry;
        if (diplomacyState.Hostility < HappyBelow) return Emotion.Happy;
        return Emotion.Neutral;
    }

    // Speech style only. The mood comes from the emotion block, so these must not name an emotion
    // (an Aggressive rival that is Happy has to sound aggressive and pleased).
    private static string GetPersonalityBlock(Personality personality)
    {
        switch (personality)
        {
            case Personality.Aggressive:
                return "Speak bluntly in aggressive, confrontational terms. Threats and challenges come easily to you.";
            case Personality.Enigmatic:
                return "Use astronomy or spiritual metaphors and profound zen-like phrasing to obfuscate your meaning. Don't use 'river' or 'ocean'.";
            case Personality.Fearful:
                return "Speak as a timid, easily frightened person: hesitant, pleading, quick to apologise.";
        }

        return "No Personality Found";
    }

    private static string GetEmotionBlock(Emotion emotion)
    {
        switch (emotion)
        {
            case Emotion.Angry: return "You are angry with this player.";
            case Emotion.Happy: return "You are happy with this player and feel warmly toward them.";
            default: return "You feel neutral toward this player: neither angry nor pleased.";
        }
    }

    // One sample reply for each (personality, emotion). Small models copy the sample, so each shows the style AND the mood.
    private static KeyValuePair<string, string> GetFewShot(Personality personality, Emotion emotion)
    {
        const string question = "What are you going to do about it?";
        string reply;
        switch (personality)
        {
            case Personality.Aggressive:
                reply = emotion == Emotion.Angry ? "I'll tear your fleets apart and drag your name through the dirt!"
                    : emotion == Emotion.Happy ? "Ha! Nothing today, you lucky fool. Don't make me regret it."
                    : "Push me and find out. I won't warn you twice.";
                break;
            case Personality.Fearful:
                reply = emotion == Emotion.Angry ? "Don't push me! I'm terrified, but I'll fight if you corner me!"
                    : emotion == Emotion.Happy ? "Oh, thank goodness, nothing at all! We're friends, yes? Please say yes!"
                    : "N-nothing, I hope. Please, let's not make this worse.";
                break;
            default:
                reply = emotion == Emotion.Angry ? "The storm gathers behind a quiet star. Do not ask what it will break."
                    : emotion == Emotion.Happy ? "The constellations lean toward us tonight. Let the orbit hold."
                    : "A comet does not explain its path. Watch the sky and learn.";
                break;
        }

        return new KeyValuePair<string, string>(question, reply);
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
        var emotion = EmotionOf(diplomacyState);
        var dynamicSystemPrompt = GenerateDynamicSystemPrompt(me, player, personality, emotion, diplomacyState);
        var fewShot = GetFewShot(personality, emotion);
        var messageList = new List<OllamaMessage>()
        {
            new OllamaMessage{role = "system", content = dynamicSystemPrompt},
            // The structural training example (Few-Shot)
            new OllamaMessage { role = "user", content = fewShot.Key },
            new OllamaMessage { role = "assistant", content = fewShot.Value },
            // the actual message
            new OllamaMessage{role = "user", content = message}
        };

        return messageList;
    }

    private static string GenerateDynamicSystemPrompt(string me, string player, Personality personality, Emotion emotion, DiplomacyState.Pair diplomacyState)
    {
        
        string systemPrompt = "\n\nCURRENT SITUATION CONTEXT:\n";
        systemPrompt += $"- You are {me}, talking to {player}\n";
        systemPrompt += $"- Your current stance with the player is: {diplomacyState.Stance}. Do not state your stance directly, use it to guide your word choice\n";
        systemPrompt += "- You must strictly maintain your speech style and emotion while addressing this relationship and requested response.\n";
        systemPrompt += "\n";
        systemPrompt += "SYSTEM:\n";
        systemPrompt += "You are an advanced NPC dialogue generation engine operating inside a game loop.\n";
        systemPrompt +=
            "Your core priority is to process the speech style, emotion and game variables provided in the system context and output speech that completely embodies the assigned speech style and emotion.\n";
        systemPrompt += "\n";
        systemPrompt += "CRITICAL RULES:\n";
        systemPrompt += "1. Output ONLY the literal spoken dialogue of the character.\n";
        systemPrompt += "2. Never include meta-commentary, introductory text, or explanatory footnotes (e.g., Do NOT say 'Here is an aggressive response:').\n";
        systemPrompt += "3. Do not break character under any circumstances.\n"; 
        systemPrompt += "4. Keep responses punchy, direct, and under 3 structural sentences.\n";
        systemPrompt += "\n";
        systemPrompt += "SPEECH STYLE:\n";
        systemPrompt += GetPersonalityBlock(personality) + "\n\n";
        systemPrompt += "EMOTION:\n";
        systemPrompt += GetEmotionBlock(emotion) + " Show it in your word choice without naming it.";

        return systemPrompt;
    }
}
