using System.Text.Json;

namespace Nexus.Platform.SmokeHost;

/// <summary>
/// Durable local record store for smoke-host chat turns. Lives under the host's output
/// directory so a restarted process reads exactly what the previous process wrote.
/// </summary>
public static class ChatStore
{
    public static string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, ".data");

    public static string Save(ChatRecord record)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, $"{record.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions));
        return path;
    }

    public static ChatRecord? Load(string id)
    {
        var path = Path.Combine(DataDirectory, $"{id}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ChatRecord>(File.ReadAllText(path), JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
