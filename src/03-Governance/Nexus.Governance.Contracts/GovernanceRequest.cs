namespace Nexus.Governance.Contracts;

/// <summary>
/// The requested operation, in the caller's own vocabulary (for example a Forge protected
/// mutation). A plain wrapped string: GOVERNANCE does not enumerate operations, because the set
/// belongs to the caller's domain and enumerating it here would make every new operation a
/// GOVERNANCE contract change.
/// </summary>
public sealed record GovernanceOperation
{
    public GovernanceOperation(string value, bool mutating = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("GovernanceOperation value must not be null or whitespace.", nameof(value));
        }

        Value = value;
        Mutating = mutating;
    }

    public string Value { get; }

    /// <summary>
    /// Whether the operation changes the target. Declared by the caller, and checked against the
    /// caller's own ChangeScope: a mutation against a READ-only declaration is refused. It is a
    /// declaration to be validated, never a fact the evaluator trusts on its own — the same shape
    /// as `DevelopmentControlAppendRecord.IdentityColumn` in the shared component.
    /// </summary>
    public bool Mutating { get; }

    public override string ToString() => Value + (Mutating ? " (mutating)" : " (read-only)");
}

/// <summary>
/// What the operation acts on, and which head owns it.
///
/// <para><see cref="Head"/> is the ownership declaration the evaluation compares against
/// <see cref="GovernanceRequest.CallerHead"/>. It is supplied by the caller because GOVERNANCE
/// holds no repository or worktree state, and a rule that guessed ownership from a path would be
/// inventing the fact it is meant to check.</para>
/// </summary>
public sealed record GovernanceTarget
{
    public GovernanceTarget(string resource, string head)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("GovernanceTarget resource must not be null or whitespace.", nameof(resource));
        }

        if (string.IsNullOrWhiteSpace(head))
        {
            throw new ArgumentException("GovernanceTarget head must not be null or whitespace.", nameof(head));
        }

        Resource = resource;
        Head = head;
    }

    /// <summary>The resource, change target or path the operation addresses.</summary>
    public string Resource { get; }

    /// <summary>The head that owns <see cref="Resource"/>.</summary>
    public string Head { get; }

    public override string ToString() => Head + ":" + Resource;
}

/// <summary>One declared scope item: a target the caller has declared it may touch, and how.</summary>
public sealed record GovernanceScopeItem
{
    public GovernanceScopeItem(string target, bool write)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("GovernanceScopeItem target must not be null or whitespace.", nameof(target));
        }

        Target = target;
        Write = write;
    }

    public string Target { get; }

    /// <summary>True when the declaration permits mutation of <see cref="Target"/>.</summary>
    public bool Write { get; }

    public override string ToString() => (Write ? "write:" : "read:") + Target;
}

/// <summary>
/// The caller's declared ChangeScope. Deliberately NOT <c>Nexus.ProductCore.*</c>'s type: this
/// assembly is a contracts leaf and may reference no other Nexus assembly, and reaching into
/// ProductCore from GOVERNANCE is forbidden in the other direction by the CORE &lt;-&gt; GOVERNANCE
/// rule. The caller maps its own scope onto this shape at the boundary.
/// </summary>
public sealed record GovernanceChangeScope
{
    public GovernanceChangeScope(string changeId, IReadOnlyList<GovernanceScopeItem> items)
    {
        if (string.IsNullOrWhiteSpace(changeId))
        {
            throw new ArgumentException("GovernanceChangeScope changeId must not be null or whitespace.", nameof(changeId));
        }

        ChangeId = changeId;
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public string ChangeId { get; }

    public IReadOnlyList<GovernanceScopeItem> Items { get; }

    public override string ToString() => ChangeId + " (" + Items.Count + " items)";
}

/// <summary>
/// What the caller resolved about the target's dependencies. <see cref="Unresolved"/> carries the
/// dependencies the caller could NOT resolve; a non-empty list is what makes the evaluation
/// undecidable rather than decidable-no.
/// </summary>
public sealed record GovernanceDependencyContext
{
    public GovernanceDependencyContext(
        IReadOnlyList<string> resolved,
        IReadOnlyList<string> unresolved)
    {
        Resolved = resolved ?? throw new ArgumentNullException(nameof(resolved));
        Unresolved = unresolved ?? throw new ArgumentNullException(nameof(unresolved));
    }

    public IReadOnlyList<string> Resolved { get; }

    public IReadOnlyList<string> Unresolved { get; }

    public static GovernanceDependencyContext Empty { get; } = new([], []);

    public override string ToString() =>
        "resolved=" + Resolved.Count + ", unresolved=" + Unresolved.Count;
}

/// <summary>
/// Security classification of the target, AND the outcome of scanning the change itself.
///
/// <para><b>W8E TASK 2 added <see cref="Violation"/>, because the five required cases could not all
/// be expressed without it.</b> The original three values describe what the TARGET is; a
/// secret-scan violation describes what the CHANGE would do. Those need opposite verdicts and
/// conflating them produced the wrong one: a change that would introduce a credential is a
/// <c>BLOCK</c> — there is nothing for an Owner to decide — whereas a target that legitimately
/// carries secret material is a <c>HUMAN_DECISION_REQUIRED</c>, because "may an automated change
/// touch this" is genuinely the Owner's question. One input could not answer both, and reporting a
/// found secret as an escalation would have let it proceed on approval.</para>
/// </summary>
public enum GovernanceSecurityClassification
{
    Public,
    Internal,

    /// <summary>The target legitimately carries secret material. An Owner question, never `ALLOW`.</summary>
    Secret,

    /// <summary>
    /// The change itself would introduce, move or expose secret material. A refusal: no verdict
    /// short of <c>BLOCK</c> is correct, and no value is ever read, carried or compared in reaching it.
    /// </summary>
    Violation,
}

/// <summary>
/// Git/change metadata for the operation. <see cref="BaseSha"/> is required for any mutating
/// operation: an operation that cannot state what it was based on cannot be rolled back, and the
/// W8D forward-lineage obligation makes it mandatory for every newly created or modified governed
/// work item.
/// </summary>
public sealed record GovernanceGitMetadata
{
    public GovernanceGitMetadata(string repository, string branch, string baseSha)
    {
        Repository = repository ?? "";
        Branch = branch ?? "";
        BaseSha = baseSha ?? "";
    }

    public string Repository { get; }

    public string Branch { get; }

    public string BaseSha { get; }

    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(Repository)
        && !string.IsNullOrWhiteSpace(Branch)
        && !string.IsNullOrWhiteSpace(BaseSha);

    public override string ToString() => Repository + "@" + Branch + "#" + BaseSha;
}

/// <summary>
/// Everything a deterministic governance evaluation is allowed to consider.
///
/// <para><b>Eight inputs, and no place for a ninth of the AI kind.</b> There is deliberately no
/// field for a model, a provider, a confidence, a recommendation or a review result. That absence
/// is the structural guarantee that AI cannot grant or override authority: a caller cannot pass an
/// AI opinion in, because the contract has nowhere to put one. Evidence and recommendation flow
/// through the caller's own records, never through the verdict.</para>
///
/// <para>Evaluation is a pure function of this record. No clock, no I/O, no randomness, no ambient
/// state — so the same request always yields the same verdict, on any host.</para>
/// </summary>
public sealed record GovernanceRequest
{
    public GovernanceRequest(
        string caller,
        string callerHead,
        GovernanceOperation operation,
        GovernanceTarget target,
        GovernanceChangeScope changeScope,
        GovernanceDependencyContext dependencyContext,
        GovernanceSecurityClassification securityClassification,
        GovernanceGitMetadata gitMetadata)
    {
        Caller = caller ?? "";
        CallerHead = callerHead ?? "";
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        ChangeScope = changeScope ?? throw new ArgumentNullException(nameof(changeScope));
        DependencyContext = dependencyContext ?? throw new ArgumentNullException(nameof(dependencyContext));
        SecurityClassification = securityClassification;
        GitMetadata = gitMetadata ?? throw new ArgumentNullException(nameof(gitMetadata));
    }

    /// <summary>The work item or worker the operation is attributed to.</summary>
    public string Caller { get; }

    /// <summary>The head the caller claims to act for.</summary>
    public string CallerHead { get; }

    public GovernanceOperation Operation { get; }

    public GovernanceTarget Target { get; }

    public GovernanceChangeScope ChangeScope { get; }

    public GovernanceDependencyContext DependencyContext { get; }

    public GovernanceSecurityClassification SecurityClassification { get; }

    public GovernanceGitMetadata GitMetadata { get; }
}
