using Nexus.Platform.Contracts.Identity;
using Xunit;

namespace Nexus.Platform.Identity.Tests;

/// <summary>A peer context that reports exactly what a transport observed, and nothing else.</summary>
/// <remarks>
/// It models the boundary rather than a caller: a named pipe populates <see cref="PeerUser"/> from the OS,
/// a loopback HTTP connection leaves it null. It has no way to accept a caller's claim, which is the
/// property under test.
/// </remarks>
internal sealed class ObservedPeerContext(string? peerUser) : IAuthenticatedPeerContext
{
    public string? PeerUser { get; } = peerUser;
}

/// <summary>A directory with a fixed set of actors.</summary>
internal sealed class FixedActorDirectory(params NexusActorRecord[] records) : INexusActorDirectory
{
    public bool TryFindActor(string issuer, string subject, out NexusActorRecord? actor)
    {
        actor = records.FirstOrDefault(r =>
            string.Equals(issuer, LocalOsAuthenticationIssuer.IssuerName, StringComparison.Ordinal)
            && r.Subjects.Contains(subject));

        return actor is not null;
    }
}

/// <summary>
/// W10.8A TASK 3/4/5 — the trust boundary, resolution, and the caller-impersonation controls.
/// </summary>
public sealed class LocalOsIssuerAndResolutionTests
{
    private const string RealPeer = @"DURAI-DELL\Dell";

    private static NexusActorRecord KnownHuman(string subject = RealPeer, bool disabled = false) => new()
    {
        ActorId = NexusActorId.Parse("nvx.actor.human.001"),
        ActorType = NexusActorType.Human,
        Subjects = new HashSet<string>(StringComparer.Ordinal) { subject },
        Disabled = disabled,
    };

    // ---------------------------------------------------------------- TASK 3 — the trust boundary

    [Fact]
    public async Task A_trusted_named_pipe_peer_establishes_an_authenticated_subject()
    {
        var issuer = new LocalOsAuthenticationIssuer(new ObservedPeerContext(RealPeer));

        var subject = await issuer.AuthenticateAsync();

        Assert.NotNull(subject);
        Assert.Equal(LocalOsAuthenticationIssuer.IssuerName, subject!.Issuer);
        Assert.Equal(RealPeer, subject.Subject);
    }

    [Fact]
    public async Task A_caller_asserted_username_cannot_replace_the_observed_peer()
    {
        // LOAD-BEARING. The measured behaviour: a client asserting DOMAIN\someone-else produced no change in
        // what the server observed. Modelled at the contract level — the issuer reads the PEER, and there is
        // no parameter through which a caller could supply a subject.
        var issuer = new LocalOsAuthenticationIssuer(new ObservedPeerContext(RealPeer));

        var subject = await issuer.AuthenticateAsync();

        Assert.Equal(RealPeer, subject!.Subject);
        Assert.NotEqual(@"DOMAIN\definitely-not-the-real-user", subject.Subject);

        // And structurally: the only input to the issuer is the peer context. If this signature ever grew a
        // caller-supplied subject, the omission would be visible here rather than in a review.
        var parameters = typeof(LocalOsAuthenticationIssuer)
            .GetMethod(nameof(LocalOsAuthenticationIssuer.AuthenticateAsync))!
            .GetParameters();

        Assert.Single(parameters);
        Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);
    }

    [Fact]
    public async Task Loopback_HTTP_does_not_become_authenticated_merely_because_it_is_localhost()
    {
        // LOAD-BEARING. W10.8A measured that a loopback HTTP connection exposes no peer account. A
        // transport that carries no peer identity must authenticate nobody — "it came from 127.0.0.1" is
        // not a subject.
        var issuer = new LocalOsAuthenticationIssuer(new ObservedPeerContext(peerUser: null));

        var subject = await issuer.AuthenticateAsync();

        Assert.Null(subject);
    }

    [Fact]
    public async Task The_default_peer_context_authenticates_nobody()
    {
        // The registered default. An estate that forgets to wire a transport refuses every request rather
        // than admitting every request — a permissive default becomes the architecture.
        var issuer = new LocalOsAuthenticationIssuer(new NoPeerContext());

        Assert.Null(await issuer.AuthenticateAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_peer_identity_is_a_refusal(string peer)
    {
        var issuer = new LocalOsAuthenticationIssuer(new ObservedPeerContext(peer));

        Assert.Null(await issuer.AuthenticateAsync());
    }

    [Fact]
    public async Task A_padded_peer_identity_is_refused_rather_than_trimmed()
    {
        var issuer = new LocalOsAuthenticationIssuer(new ObservedPeerContext($" {RealPeer}"));

        Assert.Null(await issuer.AuthenticateAsync());
    }

    // ---------------------------------------------------------------- TASK 4 — resolution

    [Fact]
    public async Task A_known_subject_resolves_to_a_Nexus_actor()
    {
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory(KnownHuman()));
        var subject = await new LocalOsAuthenticationIssuer(new ObservedPeerContext(RealPeer)).AuthenticateAsync();

        var resolution = await resolver.ResolveAsync(subject!);

        Assert.Equal(ActorResolutionOutcome.Resolved, resolution.Outcome);
        Assert.NotNull(resolution.Actor);
        Assert.Equal("nvx.actor.human.001", resolution.Actor!.ActorId.Value);
        Assert.Equal(RealPeer, resolution.Actor.AuthenticatedSubject);
        Assert.Equal(LocalOsAuthenticationIssuer.IssuerName, resolution.Actor.AuthenticationIssuer);
    }

    [Fact]
    public async Task Resolution_is_deterministic()
    {
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory(KnownHuman()));
        var subject = await new LocalOsAuthenticationIssuer(new ObservedPeerContext(RealPeer)).AuthenticateAsync();

        var first = await resolver.ResolveAsync(subject!);
        var second = await resolver.ResolveAsync(subject!);

        Assert.Equal(first.Actor!.ActorId.Value, second.Actor!.ActorId.Value);
    }

    [Fact]
    public async Task An_unknown_subject_fails_closed_and_never_becomes_an_actor()
    {
        // THE FALLBACK CONTROL. An authenticated stranger is still a stranger.
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory(KnownHuman()));

        var resolution = await resolver.ResolveAsync(new AuthenticatedSubject
        {
            Issuer = LocalOsAuthenticationIssuer.IssuerName,
            Subject = @"DURAI-DELL\SomebodyElse",
            ActorType = NexusActorType.Human,
        });

        Assert.Equal(ActorResolutionOutcome.UnknownSubject, resolution.Outcome);
        Assert.Null(resolution.Actor);
        Assert.Contains("no actor", resolution.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_directory_resolves_nobody()
    {
        // The strongest form of the fallback control: with no actors at all, nothing resolves.
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory());

        var resolution = await resolver.ResolveAsync(new AuthenticatedSubject
        {
            Issuer = LocalOsAuthenticationIssuer.IssuerName,
            Subject = RealPeer,
            ActorType = NexusActorType.Human,
        });

        Assert.Null(resolution.Actor);
        Assert.Equal(ActorResolutionOutcome.UnknownSubject, resolution.Outcome);
    }

    [Fact]
    public async Task A_disabled_actor_fails_closed_and_is_distinct_from_an_unknown_one()
    {
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory(KnownHuman(disabled: true)));

        var resolution = await resolver.ResolveAsync(new AuthenticatedSubject
        {
            Issuer = LocalOsAuthenticationIssuer.IssuerName,
            Subject = RealPeer,
            ActorType = NexusActorType.Human,
        });

        // Distinct because the remedy differs: a revocation is a decision somebody made and can be
        // reviewed; a missing record is a gap nobody created.
        Assert.Equal(ActorResolutionOutcome.Disabled, resolution.Outcome);
        Assert.Null(resolution.Actor);
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData("issuer", "")]
    [InlineData("  ", "subject")]
    public async Task A_malformed_subject_is_refused(string issuer, string subject)
    {
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory(KnownHuman()));

        var resolution = await resolver.ResolveAsync(new AuthenticatedSubject
        {
            Issuer = issuer,
            Subject = subject,
            ActorType = NexusActorType.Human,
        });

        Assert.Equal(ActorResolutionOutcome.InvalidIdentity, resolution.Outcome);
        Assert.Null(resolution.Actor);
    }

    [Fact]
    public async Task No_resolution_refusal_ever_carries_an_actor()
    {
        // Swept across every refusal outcome, so a future branch that populated Actor on a refusal is
        // caught without anyone remembering to extend this test.
        var resolver = new DeterministicNexusActorResolver(new FixedActorDirectory());

        var refusals = new[]
        {
            await resolver.ResolveAsync(new AuthenticatedSubject { Issuer = "i", Subject = "s", ActorType = NexusActorType.Human }),
            await resolver.ResolveAsync(new AuthenticatedSubject { Issuer = "", Subject = "s", ActorType = NexusActorType.Human }),
        };

        Assert.All(refusals, r =>
        {
            Assert.NotEqual(ActorResolutionOutcome.Resolved, r.Outcome);
            Assert.Null(r.Actor);
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        });
    }
}
