using Nexus.Platform.Contracts.Secrets;

namespace Nexus.Platform.Core.Secrets;

/// <summary>
/// L01 CORE implementation of the neutral <see cref="ISecretResolver"/> contract.
///
/// W5E / D-14: this type is the platform-side half of the secret-access boundary. It
/// resolves an OPAQUE secret reference to a value and knows nothing else. It contains
/// no provider name, no model name, no vendor SDK, no vendor environment-variable
/// semantics, and it opens no credential store. That is deliberate: V3 rule 2 says
/// Platform must not understand AI provider semantics, and V3 rule 1 says Platform may
/// own NEUTRAL secret custody primitives. This is that primitive, and only that.
///
/// REFERENCE NAMESPACE. A secret reference is an opaque locator; the resolver
/// implementation defines the namespace it lives in. THIS implementation's namespace is
/// the process/host environment, so the reference a caller passes IS an environment
/// variable name. The AI Head's provider configuration owns the DECISION of which name
/// to use; Platform only performs the lookup. Other implementations are free to define
/// other namespaces -- e.g. the smoke host binds a store-backed resolver whose namespace
/// is a dotted configuration path -- which is exactly why the contract takes a bare
/// <c>string key</c> and not a typed provider credential.
///
/// WHY ENVIRONMENT ONLY, AND NOT THE USER-SECRETS STORE. The local development store
/// (%APPDATA%\Microsoft\UserSecrets\...\secrets.json) is credential-adjacent by
/// definition, and D-14 blocks any W5 lane that could copy, move, expose, consume or
/// preserve a credential in an unsafe form. Keeping the store-backed implementation out
/// of neutral CORE means the blocked adapter stays isolated in the sample host, while the
/// neutral boundary every other consumer depends on is free of that exposure. Adding
/// store access here later is a change to a resolver IMPLEMENTATION, not to the boundary.
///
/// This implements roadmap work item WI-01-5.1.1 ("ISecretResolver implementation",
/// milestone M-01-5.1), which named Nexus.Platform.Core as the advised landing project --
/// see docs/CONFIGURATION_STANDARDS.md, docs/INTEGRATION_ARCHITECTURE.md and
/// nexus-roadmap.yaml. The contract itself is unchanged and was NOT duplicated.
/// </summary>
public sealed class EnvironmentSecretResolver : ISecretResolver
{
    /// <summary>
    /// Resolves a secret reference against the current process environment.
    /// Returns <c>null</c> when the reference is empty or unset -- the contract's
    /// "no such secret" signal. Never throws for a missing secret.
    /// </summary>
    public Task<string?> ResolveAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult<string?>(null);
        }

        var value = Environment.GetEnvironmentVariable(key);

        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }
}
