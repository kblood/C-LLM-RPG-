using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// Represents the "AI brain" of an NPC, handling conversations and decision-making via LLM.
/// Each NPC has a brain that maintains its own personality, memory, and conversation history.
/// </summary>
public class NpcBrain
{
    private readonly OllamaClient _ollamaClient;
    private readonly Character _npc;
    private readonly string _systemPrompt;

    public NpcBrain(OllamaClient ollamaClient, Character npc, string? systemPrompt = null)
    {
        _ollamaClient = ollamaClient;
        _npc = npc;
        _systemPrompt = systemPrompt ?? GenerateDefaultSystemPrompt(npc);
    }

    /// <summary>
    /// Get a response from this NPC given player input.
    /// Maintains conversation history for context.
    /// </summary>
    public async Task<string> RespondToPlayerAsync(string playerMessage, string? model = null)
    {
        // Build message history
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = _systemPrompt }
        };

        // Add conversation history (last N messages to avoid token bloat)
        var recentHistory = _npc.ConversationHistory.TakeLast(10).ToList();
        foreach (var entry in recentHistory)
        {
            messages.Add(new ChatMessage { Role = entry.Role, Content = entry.Content });
        }

        // Add current player message
        messages.Add(new ChatMessage { Role = "user", Content = playerMessage });

        try
        {
            // Get response from Ollama
            var response = await _ollamaClient.ChatAsync(messages, model);

            // Record this exchange in conversation history
            _npc.ConversationHistory.Add(new ConversationEntry
            {
                Role = "user",
                Content = playerMessage,
                Timestamp = DateTime.UtcNow
            });

            _npc.ConversationHistory.Add(new ConversationEntry
            {
                Role = "assistant",
                Content = response,
                Timestamp = DateTime.UtcNow
            });

            return response;
        }
        catch (OllamaException)
        {
            return $"*{_npc.Name} seems confused and cannot speak.*";
        }
    }

    /// <summary>
    /// Stream a response for long or complex replies.
    /// Useful for narrative-heavy NPCs or story narration.
    /// </summary>
    public async IAsyncEnumerable<string> RespondToPlayerStreamAsync(
        string playerMessage,
        string? model = null)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = _systemPrompt }
        };

        var recentHistory = _npc.ConversationHistory.TakeLast(10).ToList();
        foreach (var entry in recentHistory)
        {
            messages.Add(new ChatMessage { Role = entry.Role, Content = entry.Content });
        }

        messages.Add(new ChatMessage { Role = "user", Content = playerMessage });

        // Record the player message
        _npc.ConversationHistory.Add(new ConversationEntry
        {
            Role = "user",
            Content = playerMessage,
            Timestamp = DateTime.UtcNow
        });

        // Stream the response and collect it
        var fullResponse = string.Empty;

        await foreach (var chunk in _ollamaClient.ChatStreamAsync(messages, model))
        {
            fullResponse += chunk;
            yield return chunk;
        }

        // Record the full assistant response
        _npc.ConversationHistory.Add(new ConversationEntry
        {
            Role = "assistant",
            Content = fullResponse,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get NPC's action intent based on game context.
    /// Parses intent from LLM response (friendly, hostile, trade, give_hint, etc).
    /// </summary>
    public async Task<NpcIntent> GetIntentAsync(string playerAction, string gameContext)
    {
        var prompt = $@"
Given this game context:
{gameContext}

The player just did: {playerAction}

Respond with ONLY a JSON object like:
{{
  ""intent"": ""friendly|hostile|trade|give_hint|ignore"",
  ""emotion"": ""happy|angry|confused|scared|neutral"",
  ""willTrade"": true/false
}}

Do not include any other text.";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = _systemPrompt },
            new() { Role = "user", Content = prompt }
        };

        try
        {
            var response = await _ollamaClient.ChatAsync(messages);
            return ParseIntentJson(response);
        }
        catch
        {
            return new NpcIntent { Intent = "neutral", Emotion = "confused" };
        }
    }

    /// <summary>
    /// Clear conversation history (useful when starting a new session).
    /// </summary>
    public void ClearHistory()
    {
        _npc.ConversationHistory.Clear();
    }

    /// <summary>
    /// Get the last N messages from conversation history.
    /// </summary>
    public List<ConversationEntry> GetRecentMessages(int count = 5)
    {
        return _npc.ConversationHistory.TakeLast(count).ToList();
    }

    private static string GenerateDefaultSystemPrompt(Character npc)
    {
        return $@"You are {npc.Name}, an NPC in a fantasy RPG.
Your health is {npc.Health}/{npc.MaxHealth}.
You are level {npc.Level}.

Personality: Helpful and mysterious.
Background: You are a seasoned adventurer who has seen much.

Keep your responses brief (1-3 sentences) unless told otherwise.
Stay in character at all times.
Respond naturally and realistically to player interactions.";
    }

    private static NpcIntent ParseIntentJson(string jsonString)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(jsonString);
            var root = json.RootElement;

            var intent = root.GetProperty("intent").GetString() ?? "neutral";
            var emotion = root.GetProperty("emotion").GetString() ?? "neutral";
            var willTrade = root.TryGetProperty("willTrade", out var tradeProp) && tradeProp.GetBoolean();

            return new NpcIntent
            {
                Intent = intent,
                Emotion = emotion,
                WillTrade = willTrade
            };
        }
        catch
        {
            return new NpcIntent { Intent = "neutral", Emotion = "confused" };
        }
    }
}

public class NpcIntent
{
    public string Intent { get; set; } = "neutral"; // friendly, hostile, trade, give_hint, ignore
    public string Emotion { get; set; } = "neutral"; // happy, angry, confused, scared, etc.
    public bool WillTrade { get; set; }
}
