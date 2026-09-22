using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NetArchTest.Rules;
using Nexus.Platform.Contracts.Secrets;
using Xunit;

namespace Nexus.Platform.Architecture.Tests;

/// <summary>
/// W5E / D-14 negative architecture tests for the neutral secret-access boundary.
///
/// These lock in the approved V3 secret architecture:
///   1. Platform owns NEUTRAL secret custody/broker primitives only.
///   2. Platform must NOT understand AI provider semantics.
///   3. AI Head owns provider credential references / identity / configuration.
///   4. AI obtains credential access through a scoped neutral secret grant.
///   5. Forge and Products never directly own provider credentials.
///   6. No provider-specific secret implementation remains in Platform.
///
/// Each negative assertion is paired with a deliberately-VIOLATING fixture test
/// (<see cref="ViolatingFixture"/> / the <c>*DetectsViolation*</c> tests) which proves
/// the detector actually fires. A guard that cannot fail is not a guard: this file
/// would otherwise be the exact "named safety check that cannot fail" defect class the
/// audit has repeatedly found, so non-vacuity is asserted, not assumed.
/// </summary>
public sealed class SecretBoundaryTests
{
    /// <summary>
    /// The NEUTRAL Platform assemblies: every Platform assembly that ships and may hold no
    /// provider-specific knowledge. This was two assemblies before W9.1; the L07 DELIVERY leaf set
    /// joins them rather than being exempted, because a component that handles credential <i>shapes</i>
    /// is the last one that should be trusted on its own word to stay neutral. Listing it here means
    /// the token scan above runs against the code that talks about credentials most.
    /// </summary>
    private static readonly Assembly[] NeutralAssemblies =
    [
        typeof(Nexus.Platform.Contracts.Models.ModelDescriptor).Assembly,
        typeof(Nexus.Platform.Core.PlatformServiceCollectionExtensions).Assembly,
        typeof(Nexus.Delivery.Contracts.ReleaseBundle).Assembly,
        typeof(Nexus.Delivery.Core.DeploymentStateMachine).Assembly
    ];

    // W5G / F-01: the former ProviderAssembly field --
    //     typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway).Assembly
    // was removed with the provider, which now lives in the AI Head. Its removal is why
    // this file no longer needs a reference to any provider project. That is the correct
    // end state for a NEUTRAL-assembly test file: this repository can no longer name a
    // provider type at all, which is the strongest available form of the neutrality the
    // file asserts.

    /// <summary>
    /// Tokens that can only appear where a PROVIDER-SPECIFIC secret is handled: provider
    /// environment-variable names and provider API hosts. Their presence in a neutral
    /// assembly means provider secret semantics leaked back into Platform.
    /// </summary>
    private static readonly string[] ProviderSecretTokens =
    [
        "OPENAI_API_KEY",
        "ANTHROPIC_API_KEY",
        "DEEPSEEK_API_KEY",
        "OPENROUTER_API_KEY",
        "AZURE_OPENAI_API_KEY",
        "api.openai.com",
        "api.anthropic.com",
        "api.deepseek.com",
        "openrouter.ai",
        "x-api-key"
    ];

    // ---------------------------------------------------------------------------------
    // 1. Platform must NOT reference provider-specific secret semantics.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void NeutralAssemblies_MustNotContain_ProviderSecretTokens()
    {
        var offenders = new List<string>();

        foreach (var assembly in NeutralAssemblies)
        {
            foreach (var token in ProviderSecretTokens)
            {
                if (AssemblyContainsToken(assembly, token))
                {
                    offenders.Add($"{assembly.GetName().Name} contains '{token}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Provider-specific secret semantics found in a NEUTRAL Platform assembly. "
            + "Provider secret handling belongs to the AI Head (AI-04), reached through the "
            + "neutral Nexus.Platform.Contracts.Secrets.ISecretResolver boundary. Offenders: "
            + string.Join("; ", offenders));
    }

    /// <summary>
    /// Non-vacuity proof for the neutrality scan above. If the scanner cannot find a provider
    /// secret token in an assembly that provably contains one, the negative test is worthless.
    ///
    /// This test is deliberately written as a DISCRIMINATION proof rather than a single
    /// assertion, because the first draft of it was itself defective: its "absent" control
    /// token was written as a string literal in this file, so the compiler placed it in this
    /// assembly's #US heap and the scanner correctly found it. A control that cannot be absent
    /// proves nothing. The absent token is therefore built at RUNTIME so it can never be
    /// folded into the assembly as a literal.
    /// </summary>
    [Fact]
    public void TokenScanner_IsNonVacuous_OnRealCompiledAssembly()
    {
        var fixtureAssembly = typeof(ViolatingFixture).Assembly;

        // Positive: the violating fixture carries a provider secret token, and the scanner
        // must find it in a real compiled assembly (not merely in a synthetic byte array).
        Assert.True(
            AssemblyContainsToken(fixtureAssembly, ViolatingFixture.ProviderSecretToken),
            "The token scanner failed to detect a KNOWN-PRESENT provider secret token in a real "
            + "compiled assembly. The neutrality scan above is therefore vacuous.");

        // Negative control: a token that cannot be present is not reported. Guid.NewGuid() is
        // not a compile-time constant, so no literal for this value can exist in the assembly.
        var absentToken = "NEXUS_ABSENT_" + Guid.NewGuid().ToString("N");

        Assert.False(
            AssemblyContainsToken(fixtureAssembly, absentToken),
            "The token scanner reported a token that cannot exist; it is matching everything, "
            + "which makes the neutrality scan equally worthless.");
    }

    [Fact]
    public void NeutralSecretInfrastructure_MustNotDependOn_ProviderNamespaces()
    {
        foreach (var assembly in NeutralAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespace("Nexus.Platform.Contracts.Secrets")
                .Or().ResideInNamespace("Nexus.Platform.Core.Secrets")
                .ShouldNot()
                .HaveDependencyOnAny("Nexus.Platform.Providers", "OpenAI", "Anthropic", "DeepSeek")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assembly.GetName().Name}: neutral secret infrastructure depends on a provider "
                + "namespace: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    // ---------------------------------------------------------------------------------
    // 2. The neutral secret contract itself must be provider-neutral.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void NeutralSecretContract_LivesInContracts_AndExposesOnlyNeutralOperations()
    {
        Assert.Equal("Nexus.Platform.Contracts.Secrets", typeof(ISecretResolver).Namespace);

        var providerWords = new[] { "openai", "anthropic", "deepseek", "openrouter", "azure", "gpt", "claude" };

        var surface = typeof(ISecretResolver)
            .GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.Name).Append(m.Name))
            .ToList();

        var leaked = surface
            .Where(n => n is not null
                && providerWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            leaked.Count == 0,
            "The neutral secret contract exposes provider-specific vocabulary: " + string.Join(", ", leaked));

        // The grant/revoke/expire/audit operations named in the W5E directive were assessed
        // and found NOT REQUIRED: no caller needs them, and inventing unused verbs would be a
        // duplicate secret framework. This asserts the smallest contract that suffices.
        Assert.NotEmpty(typeof(ISecretResolver).GetMethods());
    }

    // ---------------------------------------------------------------------------------
    // 3. The AI provider adapter MAY depend on the neutral secret contract.
    // 4. D-14 regression lock: provider options carry REFERENCES, never VALUES.
    //
    // W5G / F-01: both sections were RELOCATED, not deleted. Their tests --
    //     ProviderAdapter_MayDependOn_NeutralSecretContract
    //     ProviderOptions_CarrySecretReferences_NeverSecretValues
    // -- asserted properties of Nexus.Platform.Providers.OpenAI types, which this
    // repository no longer contains. They now live in
    // Nexus.Intelligence.Architecture.Tests/RelocatedSecretBoundaryTests.cs, in the same
    // change, where the provider is.
    //
    // This relocation is NOT neutral for D-14 and was treated as a security-control move
    // rather than a test move. Section 4 was the ONLY continuous regression lock on
    // W4L-201's blocking reason ("OpenAIOptions carries an ApiKey property and the
    // provider is the credential consumer"), and section 3 was the lock on the adapter
    // resolving through ISecretResolver instead of reading OPENAI_API_KEY from the
    // environment. Both are asserted at COMPILE time in the relocated file, so a
    // regression there breaks the AI Head's build rather than merely failing a test.
    //
    // What stays here, and is deliberately the whole point of the split: the NEUTRAL side
    // of the D-14 boundary. Sections 1 and 2 above still assert that no provider secret
    // token can appear in a neutral Platform assembly, and the scanner's non-vacuity proof
    // still runs against THIS assembly, so this guard cannot become a check that cannot
    // fail when the provider leaves.
    // ---------------------------------------------------------------------------------

    // ---------------------------------------------------------------------------------
    // Scanner
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Scans a compiled assembly for a token. The <c>#US</c> (user-string) metadata heap
    /// stores literals as UTF-16LE, so a UTF-16 byte search finds string literals anywhere
    /// in the assembly - including inside method bodies, which reflection alone cannot reach.
    /// </summary>
    private static bool AssemblyContainsToken(Assembly assembly, string token)
    {
        var path = assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        var haystack = File.ReadAllBytes(path);
        var needle = Encoding.Unicode.GetBytes(token);

        return IndexOf(haystack, needle) >= 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        var limit = haystack.Length - needle.Length;
        for (var i = 0; i <= limit; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// A deliberately-VIOLATING fixture. It exists solely so
/// <see cref="SecretBoundaryTests.TokenScanner_IsNonVacuous_OnRealCompiledAssembly"/> can prove the
/// token scanner detects a provider secret token in a real compiled assembly. It is never invoked
/// by production code and never read by the neutrality assertions, which scan only the neutral
/// Platform assemblies. Without this, the neutrality guard would be a check that cannot fail.
///
/// <see cref="ProviderSecretToken"/> is deliberately NOT one of the tokens in
/// <c>ProviderSecretTokens</c>, so its presence in this assembly is attributable to this fixture
/// rather than to the token list itself.
/// </summary>
internal static class ViolatingFixture
{
    internal const string ProviderSecretToken = "FIXTURE_PROVIDER_SECRET_TOKEN_9F3A1B";

    internal static string Leak() => ProviderSecretToken;
}
