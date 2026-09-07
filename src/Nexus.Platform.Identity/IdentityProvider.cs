namespace Nexus.Platform.Identity;

// TODO(V2): Nexus.Platform.Identity is L01 CORE. Implement
// Nexus.Platform.Contracts.Identity.IIdentityService and ITenantResolver here
// against the platform's tenant/user store -- both are CORE responsibilities.
//
// Product registration (Nexus.Platform.Contracts.Governance.IProductRegistry) is
// L03 GOVERNANCE-owned, not CORE-owned, and DEPENDENCY_RULES.md's matrix forbids
// CORE from depending on GOVERNANCE in either direction (row 01 CORE has no "may
// reference" cell for column 03 GOVERNANCE). Do NOT implement IProductRegistry in
// this project or take a direct reference to Governance's implementation of it. If
// identity resolution ever genuinely needs a product-registration answer, reach it
// through an approved neutral boundary or composition-root/DI design (the same
// dependency-inversion shape used for IQuotaPolicy -- see
// architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md and
// _BATCH_08_REPORT.md §7, _BATCH_09_REPORT.md), never a CORE -> GOVERNANCE
// project/type reference.
public interface IIdentityProvider
{
}
