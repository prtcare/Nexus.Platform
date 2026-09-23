namespace Nexus.Delivery.Contracts;

/// <summary>
/// A caller-registered detector for one provider's or vendor's key format.
///
/// <para>
/// <b>Why patterns are supplied rather than built in.</b> The Owner's ruling requires
/// <b>provider-neutral</b> secret scanning. Platform's neutral assemblies are forbidden from
/// containing provider secret semantics at all — <c>SecretBoundaryTests</c> fails the build if a
/// provider env-var name or API host appears in one — so a provider's key format cannot live here.
/// The neutral engine detects <i>shape</i> (<see cref="CredentialShape"/>); a provider's own prefix
/// is registered by whoever owns that provider, at the point where the gate runs for that repository.
/// </para>
///
/// <para>
/// <b>The calibration obligation.</b> W9.0 closed seven credential-shaped candidates, five of them
/// false positives. A rule that would have fired on any of those five — an env-var identifier, a
/// redaction marker, an HTML autocomplete token, a deliberate probe value, a design-time connection
/// reference — does not belong in a default policy. A scan that cries wolf is muted within a week,
/// and a muted scan is worse than none because it is believed.
/// </para>
/// </summary>
public sealed record SecretPattern(string Name, string RegularExpression, string Description);

/// <summary>
/// What the scan engine is allowed to look at, and what it knows.
///
/// <para>
/// The policy is data, so that the same engine serves every repository and a provider-specific rule
/// is added where the provider is — never in the neutral engine.
/// </para>
/// </summary>
public sealed record SecretScanPolicy
{
    /// <summary>
    /// Key vocabulary. This is the one place a <i>word</i> is the right test, because the rule is
    /// "a value assigned to a credential-named key", and the key name is what tells us the assignment
    /// is credential-bearing. The value is still judged by shape, never by this list.
    /// </summary>
    public IReadOnlyList<string> CredentialKeyVocabulary { get; init; } =
    [
        "password", "passwd", "pwd", "secret", "token", "apikey", "api_key", "api-key",
        "accesskey", "accountkey", "auth", "credential", "privatekey", "signingkey",
        "connectionstring", "sas"
    ];

    /// <summary>Provider- or vendor-specific patterns. Empty by default, and empty is the neutral default.</summary>
    public IReadOnlyList<SecretPattern> RegisteredPatterns { get; init; } = [];

    /// <summary>Globs (relative to the scan root) to skip. Build output, dependencies, and worktree scratch — never source.</summary>
    public IReadOnlyList<string> IgnoreGlobs { get; init; } =
    [
        "**/bin/**", "**/obj/**", "**/node_modules/**", "**/.next/**", "**/.git/**",
        "**/dist/**", "**/artifacts/**", "**/*.min.js", "**/*.map",

        // .forge holds parallel-worker worktrees: full second checkouts of the same repository, 82 of
        // them across this estate when W9.0 measured it. Scanning them would re-report every finding
        // once per worktree and drown the gate in duplicates — the fastest route to a muted scan.
        // A worktree is scratch state, not source; the canonical tree is what a gate scans.
        "**/.forge/**"
    ];

    /// <summary>Files larger than this are skipped and the scan is reported <c>Incomplete</c> rather than silently passing.</summary>
    public long MaxFileBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>The neutral default: structural detection plus the credential vocabulary, no vendor patterns.</summary>
    public static SecretScanPolicy Neutral { get; } = new();
}
