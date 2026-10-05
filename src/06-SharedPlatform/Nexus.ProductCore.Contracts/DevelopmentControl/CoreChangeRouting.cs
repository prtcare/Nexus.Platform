namespace Nexus.ProductCore.Contracts.DevelopmentControl;

/// <summary>
/// Where a Core Change Request is addressed, in the ONE place both hosts read it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> A Core Change Request is created by one head and discovered by
/// another, and the two only agree if they agree about the address. They did not. Nexus Developer
/// hard-coded <c>Destination: "Platform"</c> — the head that <i>owns</i> the work — while Forge
/// discovered requests by filtering <c>Destination</c> against its own name. Developer created
/// requests that Forge could never see, and both component test suites passed, because each measured
/// its own half.
/// </para>
/// <para>
/// So the routing rule lives here, in the contracts assembly both hosts already reference, and both
/// sides call <see cref="IsAddressedTo"/> rather than comparing strings of their own. A second copy of
/// the rule is exactly what failed; there is now one.
/// </para>
/// <para>
/// <b>Routing and ownership are different facts and are not collapsed into one field.</b> The head that
/// must ACT on a request and the head that OWNS the resource it touches are frequently different — a
/// Platform-owned capability is implemented by Forge — and expressing both in one member is what made
/// the original defect look correct from either side. <c>Destination</c> is the execution handler,
/// which is what <see cref="IsAddressedTo"/> compares. Ownership travels separately, on the request's
/// own <c>OwningHead</c>, and is not consulted for discovery.
/// </para>
/// </remarks>
public static class CoreChangeRouting
{
    /// <summary>
    /// The head that implements Core Change Requests — the execution handler.
    ///
    /// <para>This is the value a request's <c>Destination</c> must carry for Forge to discover it, and
    /// the identity Forge validates a discovered request against. It is not "the head that owns the
    /// work": Forge implements Platform, Foundation and AI-foundation changes, none of which it
    /// owns.</para>
    /// </summary>
    public const string ImplementingHead = "Forge";

    /// <summary>
    /// The head that owns Platform and Foundation resources.
    ///
    /// <para>Named here because it is the other half of the pair that used to be conflated: a request
    /// that asks Forge to change a Platform-owned resource is addressed to
    /// <see cref="ImplementingHead"/> and owned by this one.</para>
    /// </summary>
    public const string PlatformHead = "Platform";

    /// <summary>
    /// The discovery and validation predicate: is this request addressed to <paramref name="head"/>?
    ///
    /// <para>One implementation, called by the creating head and the discovering head, so the two
    /// cannot drift. Comparison is trimmed and case-insensitive, matching how every other head
    /// identity in this estate is compared — but it is a comparison and not a normalization: the
    /// caller still sees, and still stores, the spelling it sent.</para>
    /// </summary>
    public static bool IsAddressedTo(string? destination, string? head)
        => !string.IsNullOrWhiteSpace(destination)
           && !string.IsNullOrWhiteSpace(head)
           && string.Equals(destination.Trim(), head.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the request is addressed to the head that implements Core Change Requests.
    /// </summary>
    public static bool IsAddressedToTheImplementingHead(string? destination)
        => IsAddressedTo(destination, ImplementingHead);
}
