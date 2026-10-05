using System.Reflection;
using Nexus.Governance.Contracts;
using Nexus.Governance.Core;
using Xunit;

namespace Nexus.Governance.Tests;

/// <summary>
/// W8B — "AI may provide evidence or recommendation only; AI must not grant final authority", made
/// structural rather than promised.
///
/// <para><b>Why a structural test and not a behavioural one.</b> A behavioural test can only show
/// that some particular AI input failed to change a verdict. The claim the directive actually makes
/// is stronger: that no AI input <i>can</i> reach the decision. That is a property of the contract's
/// SHAPE, so it is checked against the shape — with a non-vacuity control, because a scanner that
/// matches nothing looks exactly like a scanner over a clean surface.</para>
/// </summary>
public sealed class GovernanceAiNeutralityTests
{
    /// <summary>
    /// Whole WORDS that would mean an AI/provider opinion could be carried into a verdict.
    ///
    /// <para><b>Matched as words, not as substrings, and that distinction was a real defect here.</b>
    /// The first version of this scanner substring-matched the list, and case-insensitively "Ai"
    /// matches <c>F<b>ai</b>lureCode</c> — so it reported an existing, entirely innocent
    /// <c>RegisterProductResult.FailureCode</c> as an AI-shaped member. A scanner that flags
    /// innocent names is as useless as one that flags nothing: the first gets muted, and then the
    /// real finding is missed. <see cref="Words"/> splits PascalCase so a member only matches when
    /// a whole word matches.</para>
    /// </summary>
    private static readonly string[] ForbiddenWords =
    [
        "Ai", "Model", "Provider", "Prompt", "Inference", "Recommendation", "Confidence", "Llm",
    ];

    /// <summary>
    /// Vendor and format names distinctive enough to match anywhere, including embedded — "OpenAI"
    /// inside a longer identifier is still an OpenAI reference, whereas "ai" inside "Failure" is not.
    /// </summary>
    private static readonly string[] ForbiddenSubstrings =
    [
        "GPT", "Claude", "OpenAI", "Anthropic", "DeepSeek", "Gemini", "Llama",
    ];

    /// <summary>Splits an identifier into its words: <c>AiRecommendation</c> → Ai, Recommendation.</summary>
    private static IEnumerable<string> Words(string name)
    {
        var buffer = new System.Text.StringBuilder();

        foreach (var ch in name)
        {
            if (ch is '_' or '.' or '-')
            {
                if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
                continue;
            }

            if (char.IsUpper(ch) && buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }

            buffer.Append(ch);
        }

        if (buffer.Length > 0) yield return buffer.ToString();
    }

    private static bool Carries(string name) =>
        Words(name).Any(w => ForbiddenWords.Any(f => string.Equals(f, w, StringComparison.OrdinalIgnoreCase)))
        || ForbiddenSubstrings.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));

    // ================================================================ the request shape

    [Fact]
    public void GovernanceRequest_HasNoFieldThatCouldCarryAnAiOpinion()
    {
        var offenders = typeof(GovernanceRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(Carries)
            .ToList();

        Assert.True(offenders.Count == 0,
            "GovernanceRequest carries a field that could hold an AI or provider opinion: "
            + string.Join(", ", offenders)
            + ". AI must not be able to reach the verdict at all, which requires that the request "
            + "has nowhere to put its output.");
    }

    [Fact]
    public void GovernanceRequest_HasExactlyTheEightDeclaredInputs()
    {
        // Pins the surface so a ninth input cannot be added without this test being seen to change.
        var names = typeof(GovernanceRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Caller", "CallerHead", "ChangeScope", "DependencyContext", "GitMetadata",
             "Operation", "SecurityClassification", "Target"],
            names);
    }

    [Fact]
    public void GovernanceDecision_HasNoFieldThatCouldBeOverwrittenByAnAiOpinion()
    {
        var offenders = typeof(GovernanceDecision)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(Carries)
            .ToList();

        Assert.True(offenders.Count == 0,
            "GovernanceDecision exposes " + string.Join(", ", offenders) + ".");
    }

    // ================================================================ assembly references

    [Fact]
    public void GovernanceAssemblies_ReferenceNoAiOrProviderAssembly()
    {
        Assembly[] assemblies =
        [
            typeof(IGovernanceEvaluator).Assembly,
            typeof(DeterministicGovernanceEvaluator).Assembly,
        ];

        foreach (var assembly in assemblies)
        {
            var offenders = assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? "")
                .Where(n => n.Contains("Intelligence", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("Provider", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("Ai", StringComparison.Ordinal))
                .ToArray();

            Assert.True(offenders.Length == 0,
                assembly.GetName().Name + " references an AI/provider assembly: "
                + string.Join(", ", offenders));
        }
    }

    [Fact]
    public void NoGovernanceTypeDeclaresAnAiOrProviderMember()
    {
        Assembly[] assemblies =
        [
            typeof(IGovernanceEvaluator).Assembly,
            typeof(DeterministicGovernanceEvaluator).Assembly,
        ];

        var offenders = new List<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (Carries(p.Name)) offenders.Add(type.Name + "." + p.Name);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Governance exports an AI/provider-shaped member: " + string.Join(", ", offenders));
    }

    // ================================================================ non-vacuity control

    /// <summary>
    /// A synthetic type that DOES carry the forbidden shape. If the scanner above cannot flag this,
    /// the scanner is vacuous and its green means nothing — the exact failure class this estate has
    /// recorded five times.
    /// </summary>
    private sealed class DeliberatelyNotAllowed
    {
        public string ModelId { get; init; } = "";

        public string AiRecommendation { get; init; } = "";

        public int ProviderConfidence { get; init; }
    }

    [Fact]
    public void TheScannerIsNonVacuous_ItFlagsADeliberatelyBadShape()
    {
        var caught = typeof(DeliberatelyNotAllowed)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(Carries)
            .ToArray();

        Assert.Equal(3, caught.Length);
        Assert.Contains("ModelId", caught);
        Assert.Contains("AiRecommendation", caught);
        Assert.Contains("ProviderConfidence", caught);
    }

    /// <summary>
    /// The other half of non-vacuity: the scanner must NOT flag innocent names. Without this, a
    /// future tightening of the word list could start flagging ordinary members, the failures would
    /// read as noise, and the guard would be weakened to shut them up — losing the real check.
    /// </summary>
    [Fact]
    public void TheScannerDoesNotFlagInnocentNames()
    {
        // "Failure" contains "ai"; "Detail" contains "ai"; "Maintainer" contains "ai".
        string[] innocent =
        [
            "FailureCode", "Detail", "Maintainer", "Domain", "Failsafe", "Available", "Chair",
            "ProductClassification", "EvaluatorVersion", "CallerHead", "GitMetadata",
        ];

        var wronglyCaught = innocent.Where(Carries).ToArray();

        Assert.True(wronglyCaught.Length == 0,
            "The scanner flags innocent names, which would make it noise: "
            + string.Join(", ", wronglyCaught));
    }

    [Fact]
    public void TheScannerStillCatchesAnEmbeddedVendorName()
    {
        // Substring matching is retained for distinctive vendor names, so the word-splitter did not
        // also throw away the ability to catch a name like this.
        Assert.True(Carries("OpenAiGatewayUrl"));
        Assert.True(Carries("deepseekApiKey"));
    }

    // ================================================================ behavioural twin

    [Fact]
    public void TheVerdictIsUnchangedByAnythingOutsideTheRequest()
    {
        // There is no AI input to vary, so the honest behavioural statement is the one that can be
        // made: repeated evaluation is stable, and no ambient state between the calls moves it.
        IGovernanceEvaluator evaluator = new DeterministicGovernanceEvaluator();

        var request = new GovernanceRequest(
            "WI-W8B-0001",
            "Forge",
            new GovernanceOperation("forge.protected.mutate", mutating: true),
            new GovernanceTarget("D:\\NEXUS\\Forge\\DevBridge", "Forge"),
            new GovernanceChangeScope("CHG-W8B-0001", [new GovernanceScopeItem("D:\\NEXUS\\Forge\\DevBridge", true)]),
            GovernanceDependencyContext.Empty,
            GovernanceSecurityClassification.Internal,
            new GovernanceGitMetadata("Forge", "w8b/forge-v3-adaptation", "984710f0"));

        var first = evaluator.Evaluate(request);
        var second = evaluator.Evaluate(request);

        Assert.True(first.IsAllow, first.DecidingReason);
        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.DecidingReason, second.DecidingReason);
    }
}
