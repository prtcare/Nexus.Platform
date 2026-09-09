namespace Nexus.Governance.Contracts;

/// <summary>
/// L03 GOVERNANCE lifecycle state of a product registry record. Registration always
/// creates a <see cref="Proposed"/> product; validated lifecycle transitions are M-03-1.3.
/// </summary>
public enum ProductLifecycleState
{
    Proposed,
    Active,
    Sunsetting,
    Retired,
}
