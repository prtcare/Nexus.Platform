using System.Text.Json;
using Nexus.Platform.Contracts.Secrets;

namespace Nexus.Platform.SmokeHost;

/// <summary>
/// Resolves the OpenAI API key exactly where set-openai-key.ps1 writes it:
/// the Nexus.Intelligence.Api user-secrets store, key "Platform:Providers:OpenAI:ApiKey",
/// with OPENAI_API_KEY honoured as the documented environment override. This is the
/// interim secret mechanism until M-01-5.1 replaces it with a real platform resolver;
/// the smoke host uses the ISecretResolver seam so the resolution path matches a host's.
/// </summary>
public sealed class StoreSecretResolver : ISecretResolver
{
    private readonly string[] _storePaths;

    public StoreSecretResolver(params string[]? storePaths)
    {
        _storePaths = storePaths is { Length: > 0 } ? storePaths : [DefaultStorePath()];
    }

    public Task<string?> ResolveAsync(string key, CancellationToken ct = default)
    {
        // 1. Environment override - set-openai-key.ps1 step 6 warns it overrides config.
        var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Task.FromResult<string?>(env);
        }

        // 2. The user-secrets store set-openai-key.ps1 writes to (flat key form).
        foreach (var path in _storePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var value = Find(doc.RootElement, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return Task.FromResult<string?>(value);
                }
            }
            catch (JsonException)
            {
                // Malformed or not JSON - try the next candidate store.
            }
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>Reads an "A:B:C" key whether the store holds it flat or nested.</summary>
    private static string? Find(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var flat) && flat.ValueKind == JsonValueKind.String)
        {
            return flat.GetString();
        }

        var current = root;
        foreach (var segment in key.Split(':'))
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string DefaultStorePath()
    {
        // set-openai-key.ps1 computes: %APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json
        // for the Nexus.Intelligence.Api project. Read the id from that csproj when present,
        // otherwise fall back to the currently-known id.
        string[] candidateProjectDirs =
        [
            @"C:\Personal\Nexus.Intelligence\src\Nexus.Intelligence.Api",
            @"C:\Personal\Nexus.Int\src\Nexus.Intelligence.Api"
        ];

        foreach (var dir in candidateProjectDirs)
        {
            var csproj = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null)
            {
                continue;
            }

            var m = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(csproj), "<UserSecretsId>(.+?)</UserSecretsId>");
            if (m.Success)
            {
                return SecretsPath(m.Groups[1].Value.Trim());
            }
        }

        return SecretsPath("0b5d9fda-b833-4142-bc5e-0e6e6fbdae5b");
    }

    private static string SecretsPath(string secretsId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "UserSecrets", secretsId, "secrets.json");
}
