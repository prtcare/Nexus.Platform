namespace Nexus.Platform.Contracts.Models;

// The metering key - and deliberately the ONLY identity Platform ever sees.
// It must never be able to express a product's internal structure.
public sealed record InvocationIdentity(
    string TenantId,
    string ProductId,
    string TurnId,
    string UserId);
