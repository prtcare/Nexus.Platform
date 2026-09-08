namespace Nexus.Assurance.Contracts;

/// <summary>
/// Typed identifier of an Assurance-held <c>Evidence</c> record (L08 ASSURANCE). A plain
/// opaque reference; equality is by <see cref="Value"/>.
/// </summary>
/// <remarks>
/// This is the **minimum** seam across the Assurance authority boundary: an identifier that a
/// DevelopmentRun / typed-DCR handback / Outcome may *reference* when an independent (non-Developer)
/// verification result has been recorded under ASSURANCE authority. It deliberately carries **no**
/// verdict, no status and no pass/fail member: ASSURANCE owns the verdict that derives from its
/// Evidence (see docs/ASSURANCE_ARCHITECTURE.md — a Pass with no Evidence is rejected at write
/// time). A consumer may hold an <see cref="AssuranceEvidenceId"/>; it must never fabricate one and
/// never interpret one as an Assurance PASS declaration.
/// </remarks>
public sealed record AssuranceEvidenceId
{
    public AssuranceEvidenceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("AssuranceEvidenceId value must not be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
