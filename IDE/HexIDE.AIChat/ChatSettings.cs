using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.AIChat;

public sealed class ChatSettings
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1";

    /// <summary>The API key as stored in the settings file. Prefer an environment variable
    /// (see <see cref="ResolveApiKey"/>) so the key need not live in plaintext on disk at all.</summary>
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = "claude-opus-5";

    [JsonPropertyName("maxTurnsBeforeCompaction")]
    public int MaxTurnsBeforeCompaction { get; set; } = 20;

    private static readonly string[] ApiKeyEnvVars = ["HEXIDE_AI_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY"];

    /// <summary>The effective API key. An environment variable — checked in order: HEXIDE_AI_API_KEY,
    /// ANTHROPIC_API_KEY, OPENAI_API_KEY — takes precedence over <see cref="ApiKey"/>, so the recommended
    /// setup keeps no key in the plaintext settings file at all.</summary>
    public string ResolveApiKey()
    {
        foreach (var name in ApiKeyEnvVars)
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return ApiKey;
    }

    /// <summary>True when the key is coming from the plaintext settings file rather than an environment
    /// variable — used to surface a one-time "prefer the env var" hint.</summary>
    public bool ApiKeyIsFromPlaintextFile()
    {
        foreach (var name in ApiKeyEnvVars)
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                return false;
        return !string.IsNullOrEmpty(ApiKey);
    }

    public static ChatSettings Load(string settingsDir)
    {
        var path = Path.Combine(settingsDir, "chat-settings.json");
        if (!File.Exists(path)) return new ChatSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ChatSettings>(json) ?? new ChatSettings();
        }
        catch { return new ChatSettings(); }
    }
}
