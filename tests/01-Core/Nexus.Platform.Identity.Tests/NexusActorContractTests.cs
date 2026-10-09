using Nexus.Platform.Contracts.Identity;
using Xunit;

namespace Nexus.Platform.Identity.Tests;

/// <summary>
/// W10.8A TASK 2 — the actor contract's architectural invariants.
/// </summary>
/// <remarks>
/// <para>
/// <b>These assert invariants, not implementation trivia.</b> Each one would still be worth asserting if
/// the types were rewritten: an actor is product-independent, its identifier is Nexus's, both actor types
/// are representable, and permissions are not part of who somebody is.
/// </para>
/// <para>
/// The last of those is asserted by <em>reflection over the type</em> rather than by inspecting a value.
/// A member-by-member assertion would pass on a record that had gained a <c>Permissions</c> property
/// tomorrow, and the property being absent is exactly the invariant.
/// </para>
/// </remarks>
public sealed class NexusActorContractTests
{
    [Fact]
    public void A_human_actor_is_representable()
    {
        var actor = new NexusActor
        {
            ActorId = NexusActorId.Parse("nvx.actor.human.001"),
            ActorType = NexusActorType.Human,
            AuthenticationIssuer = LocalOsAuthenticationIssuer.IssuerName,
            AuthenticatedSubject = @"DURAI-DELL\Dell",
        };

        Assert.Equal(NexusActorType.Human, actor.ActorType);
        Assert.Equal("nvx.actor.human.001", actor.ActorId.Value);
    }

    [Fact]
    public void A_service_actor_is_representable()
    {
        // The Owner's decision requires both types to be expressible. A service is not a fake human: an
        // audit record must be able to say that no person was involved.
        var actor = new NexusActor
        {
            ActorId = NexusActorId.Parse("nvx.actor.service.forge"),
            ActorType = NexusActorType.Service,
            AuthenticationIssuer = "service-token",
            AuthenticatedSubject = "forge.devbridge",
        };

        Assert.Equal(NexusActorType.Service, actor.ActorType);
        Assert.NotEqual(NexusActorType.Human, actor.ActorType);
    }

    [Fact]
    public void The_actor_type_vocabulary_is_closed_to_exactly_two_members()
    {
        // A third member would be a claim that another kind of actor exists and has been thought about.
        // This asserts the vocabulary is a decision rather than a default.
        var members = Enum.GetValues<NexusActorType>();

        Assert.Equal(2, members.Length);
        Assert.Equal(
            new[] { NexusActorType.Human, NexusActorType.Service }.OrderBy(x => x),
            members.OrderBy(x => x));
    }

    [Fact]
    public void The_actor_carries_no_roles_and_no_permissions()
    {
        // THE ARCHITECTURAL INVARIANT. Identity answers who; authorization answers may they. A permission
        // set on the actor would make every decision a lookup on a value carried with the identity, and
        // the two questions would stop being separable — which is the estate's current state, where
        // Roles and Permissions ride along with a caller-supplied user id.
        var propertyNames = typeof(NexusActor)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, n => n.Contains("Role", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("Claim", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, n => n.Contains("Scope", StringComparison.OrdinalIgnoreCase)
                                              && n != nameof(NexusActor.TenantId));
    }

    [Fact]
    public void There_is_no_way_to_make_an_actor_from_a_string()
    {
        // The single convenience that would become the request-body identity within one milestone. There
        // is no factory, no Parse, no implicit conversion — asserted structurally rather than promised.
        var type = typeof(NexusActor);

        // No factory: nothing static on the type returns an actor, so there is no Parse, no Create and no
        // From that a request handler could call with a body field.
        var makers = type
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => type.IsAssignableFrom(m.ReturnType))
            .ToArray();

        Assert.Empty(makers);

        // No conversion from a string, implicit or explicit. This is the shape the temptation takes once a
        // factory is refused — `NexusActor actor = request.ActorId;` reads as a convenience and is the
        // request-body identity.
        var conversions = type
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .ToArray();

        Assert.Empty(conversions);

        // And no constructor takes a subject: `new NexusActor(...)` must require every member to be named,
        // which is what makes the provenance of an AuthenticatedSubject visible at the call site.
        //
        // NOTE: a parameterless constructor IS present — a C# record must have one for object-initializer
        // syntax, and asserting its absence was the first draft of this test and was WRONG. The constructor
        // does not weaken the invariant: it cannot supply an ActorId, and `required` members make omitting
        // one a compile error rather than a default.
        var subjectful = type.GetConstructors()
            .Where(c => c.GetParameters().Length > 0)
            .ToArray();

        Assert.Empty(subjectful);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_actor_identifier_refuses_absence(string? value)
    {
        // Absence of an identity is not an identity. Resolving it to one would make every downstream
        // decision about nobody in particular.
        Assert.Throws<ArgumentException>(() => NexusActorId.Parse(value));
    }

    [Theory]
    [InlineData(" alice")]
    [InlineData("alice ")]
    public void An_actor_identifier_refuses_padding_rather_than_trimming(string value)
    {
        // Trimming would accept " alice" and "alice " as one actor while an issuer emitting either would be
        // a different actor to every other component. A boundary that silently normalises is one whose two
        // sides can disagree about who someone is.
        Assert.Throws<ArgumentException>(() => NexusActorId.Parse(value));
    }

    [Fact]
    public void An_unset_actor_identifier_is_not_a_specified_one()
    {
        // default(NexusActorId) is a value the runtime can produce without anybody deciding to. It must not
        // read as an actor.
        Assert.False(default(NexusActorId).IsSpecified);
        Assert.True(NexusActorId.Parse("nvx.actor.human.001").IsSpecified);
    }
}
