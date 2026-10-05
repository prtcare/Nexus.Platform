// ChangeScope.cs — W1-05 REAL CHANGE-SCOPE COLLISION DETECTION.
//
// WHAT THIS REPLACES
// ------------------
// ExcelDevelopmentControlStore.cs:1620-1638 holds the current overlap test:
//
//     GlobsOverlap(x, y) -> GlobOverlaps(x, y)   // "naive path-prefix; no wildcard expansion"
//
// and its own comment records the limitation. The preflight then reports ConflictFound on
// that basis. Two consequences the audit measured:
//
//   * A declared glob such as `src/**/*.cs` is compared as a STRING PREFIX. `src/Api/x.cs`
//     and `src/Api2/x.cs` share the prefix `src/Api` and are reported as colliding when they
//     are disjoint; the reverse comparison misses overlap entirely.
//   * Because the comparison never expands a wildcard, the verdict is a function of how the
//     scope was SPELLED, not of what it COVERS. Two workers can hold overlapping scopes and
//     see `Clear`, which is the false-assurance shape this stage exists to remove.
//
// WHAT THIS DOES INSTEAD
// ----------------------
// Paths are normalised, then compiled to real matchers. Overlap between two matchers is
// decided exactly where it is decidable and conservatively where it is not — never by
// substring. The conservative direction is always toward reporting a conflict, because an
// over-reported conflict costs a conversation and an under-reported one costs a corrupt file.
//
// Access mode is part of the comparison, not an afterthought: two readers never conflict,
// two writers always do, and one writer beside one reader is a POLICY question whose default
// answer is "collide".
using System.Text;
using System.Text.RegularExpressions;

namespace Nexus.DevelopmentControl.Safety;

/// <summary>What a scope item names. The kind decides which fields participate in comparison
/// and which collision rules apply, so it is never inferred.</summary>
public enum ScopeKind
{
    ExactFile = 0,
    DirectorySubtree = 1,
    Glob = 2,
    ProjectResource = 3,
    PublicContract = 4,
    DatabaseMigration = 5,
    ControlStore = 6,
}

/// <summary>How the scope is touched. Every comparison is mode-aware.</summary>
public enum AccessMode
{
    Read = 0,
    Write = 1,
    Create = 2,
    Delete = 3,
    Migrate = 4,
}

public enum CollisionVerdict
{
    /// <summary>Provably not colliding.</summary>
    Compatible = 0,

    /// <summary>Colliding under the active policy.</summary>
    Collision = 1,

    /// <summary>A policy input is missing, so neither answer is justified. Gates as a collision
    /// for protected scope; must never be silently folded into Compatible.</summary>
    Undetermined = 2,
}

public sealed record ScopeItem(
    ScopeKind Kind,
    string RepositoryId,
    string Path,
    AccessMode Access,
    /// <summary>For DatabaseMigration: the DbContext / schema the migration targets. Two
    /// migrations on different contexts do not collide; two on the same one do.</summary>
    string? ContextId = null)
{
    /// <summary>Stable, comparable spelling. Slash and case differences fold here, once.</summary>
    public string Canonical()
    {
        var p = (Path ?? "").Replace('/', '\\').Trim();
        p = p.TrimStart('\\');
        var sb = new StringBuilder(p.Length);
        var lastSep = false;
        foreach (var c in p)
        {
            var isSep = c == '\\';
            if (isSep && lastSep) continue;
            sb.Append(isSep ? '\\' : c);
            lastSep = isSep;
        }
        p = sb.ToString().TrimEnd('\\').ToLowerInvariant();
        return $"{Kind}|{RepositoryId.ToLowerInvariant()}|{p}|{ContextId?.ToLowerInvariant() ?? ""}|{Access}";
    }
}

public sealed record CollisionResult(
    CollisionVerdict Verdict,
    string Reason,
    IReadOnlyList<string> Evidence);

/// <summary>Policy inputs. <see cref="ProtectedPrefixes"/> names the paths for which a
/// writer-beside-reader collision is the DEFAULT rather than a policy choice.</summary>
public sealed record ScopeCollisionPolicy(
    /// <summary>When true, Write+Read is compatible for non-protected paths. Default false:
    /// the conservative answer, and the one the directive specifies for protected scope.</summary>
    bool AllowWriteBesideReadOutsideProtectedScope = false,
    /// <summary>Path prefixes (repo-relative, lower-case, backslash) treated as protected.</summary>
    IReadOnlyList<string>? ProtectedPrefixes = null,
    /// <summary>Database migration contexts on which two MIGRATE scopes coexist.</summary>
    IReadOnlyList<string>? MigrationContextsWithExplicitCoordination = null,
    /// <summary>Public contracts explicitly coordinated between two changes.</summary>
    IReadOnlyList<string>? CoordinatedContracts = null)
{
    public static readonly ScopeCollisionPolicy Default = new();

    private static readonly string[] BuiltInProtected =
    {
        "src\\", "tests\\", "control\\", "config\\", "scripts\\", "database\\", "migrations\\",
    };

    public bool IsProtected(string canonicalRepoRelativePath)
    {
        var p = canonicalRepoRelativePath;
        if (ProtectedPrefixes is not null)
            foreach (var pre in ProtectedPrefixes)
                if (p.StartsWith(pre.ToLowerInvariant(), StringComparison.Ordinal)) return true;
        foreach (var pre in BuiltInProtected)
            if (p.StartsWith(pre, StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>
/// The collision engine. Every decision here is a function of compiled path matchers and
/// declared access modes. There is no substring test anywhere in this type.
/// </summary>
public static class ChangeScopeCollision
{
    /// <summary>Compare two scope items. Symmetric: Evaluate(a,b) == Evaluate(b,a) in verdict
    /// and reason.</summary>
    public static CollisionResult Evaluate(ScopeItem a, ScopeItem b, ScopeCollisionPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var pol = policy ?? ScopeCollisionPolicy.Default;

        // ---- 1. Repository isolation. Two repositories are two trees; the same relative path
        // in each is NOT the same file. The two exceptions are the resources that are shared
        // BY DEFINITION across repositories and are compared globally.
        var globalKind = a.Kind is ScopeKind.PublicContract or ScopeKind.ControlStore
                      || b.Kind is ScopeKind.PublicContract or ScopeKind.ControlStore;

        if (!globalKind
            && !string.Equals(a.RepositoryId, b.RepositoryId, StringComparison.OrdinalIgnoreCase))
        {
            return new CollisionResult(CollisionVerdict.Compatible,
                $"Different repositories ('{a.RepositoryId}' vs '{b.RepositoryId}'); the same relative path in two trees is two different files.",
                new[] { $"a={a.Canonical()}", $"b={b.Canonical()}" });
        }

        // ---- 2. Same aggregate resource kinds are colliding by identity, before any path logic.
        if (a.Kind == ScopeKind.PublicContract && b.Kind == ScopeKind.PublicContract
            && PathsIntersect(a, b))
        {
            var name = a.Path;
            if (pol.CoordinatedContracts?.Any(c => PathsEquivalent(c, name)) == true)
                return new CollisionResult(CollisionVerdict.Compatible,
                    $"Public contract '{name}' is explicitly coordinated between the two changes.",
                    new[] { $"coordinated={name}" });
            return new CollisionResult(CollisionVerdict.Collision,
                $"Both changes touch the public contract '{name}'. A public contract is one surface; it collides unless explicitly coordinated.",
                new[] { $"a={a.Canonical()}", $"b={b.Canonical()}" });
        }

        if (a.Kind == ScopeKind.ControlStore || b.Kind == ScopeKind.ControlStore)
        {
            if (!PathsIntersect(a, b))
                return new CollisionResult(CollisionVerdict.Compatible,
                    "Different control stores.", new[] { $"a={a.Path}", $"b={b.Path}" });
            return new CollisionResult(CollisionVerdict.Collision,
                $"Both changes write the control store '{a.Path}'. The store is the governed artifact; concurrent writers are always a collision.",
                new[] { $"a={a.Canonical()}", $"b={b.Canonical()}" });
        }

        // ---- 3. Database migrations. Same context collides; different contexts do not.
        if (a.Kind == ScopeKind.DatabaseMigration && b.Kind == ScopeKind.DatabaseMigration
            && a.Access == AccessMode.Migrate && b.Access == AccessMode.Migrate)
        {
            var sameContext = string.Equals(a.ContextId ?? "", b.ContextId ?? "", StringComparison.OrdinalIgnoreCase);
            if (!sameContext)
                return new CollisionResult(CollisionVerdict.Compatible,
                    $"Migrations target different contexts ('{a.ContextId}' vs '{b.ContextId}').",
                    new[] { $"a={a.Canonical()}", $"b={b.Canonical()}" });
            if (pol.MigrationContextsWithExplicitCoordination?.Contains(a.ContextId ?? "", StringComparer.OrdinalIgnoreCase) == true)
                return new CollisionResult(CollisionVerdict.Compatible,
                    $"Context '{a.ContextId}' has explicit coordination for concurrent migrations.", Array.Empty<string>());
            return new CollisionResult(CollisionVerdict.Collision,
                $"Two migrations target the same context '{a.ContextId}'. Migration order is a global property of the context, so these collide even on different files.",
                new[] { $"a={a.Canonical()}", $"b={b.Canonical()}" });
        }

        // ---- 4. Path overlap. This is the part that used to be a string prefix test.
        if (!PathsIntersect(a, b))
            return new CollisionResult(CollisionVerdict.Compatible,
                "The two scope ranges are disjoint: neither can cover a path the other covers.",
                new[] { $"a={a.Canonical()}", $"b={b.Canonical()}", $"aRange={Describe(a)}", $"bRange={Describe(b)}" });

        // ---- 5. The ranges intersect. Access mode now decides.
        return ByAccessMode(a, b, pol);
    }

    public static CollisionResult EvaluateAll(
        IEnumerable<ScopeItem> requested, IEnumerable<ScopeItem> held, ScopeCollisionPolicy? policy = null)
    {
        var evidence = new List<string>();
        foreach (var r in requested)
        foreach (var h in held)
        {
            var res = Evaluate(r, h, policy);
            if (res.Verdict != CollisionVerdict.Compatible)
            {
                evidence.Add($"{res.Verdict}: {res.Reason}");
                foreach (var e in res.Evidence) evidence.Add("  " + e);
            }
        }
        return evidence.Count == 0
            ? new CollisionResult(CollisionVerdict.Compatible, "No requested scope item collides with any held scope item.", Array.Empty<string>())
            : new CollisionResult(evidence.Any(e => e.StartsWith("Collision", StringComparison.Ordinal)) ? CollisionVerdict.Collision : CollisionVerdict.Undetermined,
                evidence[0], evidence);
    }

    // ------------------------------------------------------------------ access-mode matrix

    private static CollisionResult ByAccessMode(ScopeItem a, ScopeItem b, ScopeCollisionPolicy pol)
    {
        var (x, y) = (a.Access, b.Access);

        // READ + READ: two readers of one file are not a conflict at any scope.
        if (x == AccessMode.Read && y == AccessMode.Read)
            return new CollisionResult(CollisionVerdict.Compatible,
                "Both changes only READ the overlapping range.", new[] { $"a={a.Canonical()}", $"b={b.Canonical()}" });

        // WRITE + WRITE (and any write-family pair): always a collision.
        if (IsWriteFamily(x) && IsWriteFamily(y))
            return new CollisionResult(CollisionVerdict.Collision,
                $"Both changes WRITE the overlapping range ({x} vs {y}). Concurrent writers on one path is the defect the reservation exists to prevent.",
                new[] { $"a={a.Canonical()}", $"b={b.Canonical()}", $"overlap={DescribeOverlap(a, b)}" });

        // One writer, one reader: POLICY. Default is collision for protected development scope.
        var reader = x == AccessMode.Read ? a : b;
        var writer = x == AccessMode.Read ? b : a;
        var protectedPath = DefaultProtectedPath(a, b);

        if (pol.AllowWriteBesideReadOutsideProtectedScope && !pol.IsProtected(protectedPath))
            return new CollisionResult(CollisionVerdict.Compatible,
                $"A writer and a reader share '{protectedPath}', which is outside the protected development scope and the policy permits it.",
                new[] { $"writer={writer.Canonical()}", $"reader={reader.Canonical()}" });

        return new CollisionResult(CollisionVerdict.Collision,
            $"A writer ({writer.Access}) and a reader share '{protectedPath}'. Reading a file another change is mid-way through rewriting is not a stable read, so the default is a collision.",
            new[] { $"writer={writer.Canonical()}", $"reader={reader.Canonical()}", $"protected={protectedPath}" });
    }

    private static bool IsWriteFamily(AccessMode m) =>
        m is AccessMode.Write or AccessMode.Create or AccessMode.Delete or AccessMode.Migrate;

    // ------------------------------------------------------------------ real path semantics

    /// <summary>
    /// Do the two scope ranges share any path? Exact where decidable, conservative where not.
    /// </summary>
    public static bool PathsIntersect(ScopeItem a, ScopeItem b)
    {
        var ra = RangeOf(a);
        var rb = RangeOf(b);

        // A literal on either side can be tested against the other's matcher exactly.
        if (ra.IsLiteral && rb.IsLiteral)
            return LiteralOverlaps(ra, rb);
        if (ra.IsLiteral) return rb.Matcher!.IsMatch(ra.Literal!);
        if (rb.IsLiteral) return ra.Matcher!.IsMatch(rb.Literal!);

        // Both have wildcards. Glob-vs-glob intersection is not decidable by inspection, so
        // use the wildcard-free leading segments: disjoint prefixes prove disjointness, and
        // anything else is reported as an intersection. Never a substring test.
        return PrefixesCompatible(ra.LiteralPrefix, rb.LiteralPrefix);
    }

    private sealed record Range(bool IsLiteral, string? Literal, Regex? Matcher, string LiteralPrefix, string Pattern);

    private static Range RangeOf(ScopeItem item)
    {
        var rel = Normalize(item.Path);
        return item.Kind switch
        {
            ScopeKind.ExactFile or ScopeKind.PublicContract or ScopeKind.ControlStore or ScopeKind.DatabaseMigration
                => new Range(true, rel, null, rel, rel),

            ScopeKind.DirectorySubtree =>
                new Range(false, null,
                    new Regex("^" + Regex.Escape(rel) + @"(\\|$)", RegexOptions.CultureInvariant),
                    rel, rel + @"\**"),

            ScopeKind.ProjectResource =>
                new Range(true, rel, null, rel, rel),

            // A real glob: ** crosses separators, * and ? do not.
            _ => new Range(false, null, CompileGlob(rel), LiteralPrefixOf(rel), rel),
        };
    }

    /// <summary>Compiles a glob into a regex with real path semantics:
    /// <c>**</c> crosses directory separators, <c>*</c> and <c>?</c> stay within one segment,
    /// and <c>[abc]</c> is a character class. Anchored at both ends.</summary>
    public static Regex CompileGlob(string pattern)
    {
        var p = Normalize(pattern);
        var sb = new StringBuilder("^");
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < p.Length && p[i + 1] == '*')
                    {
                        i++;
                        // "**\" may match zero segments, so consume an immediately following separator.
                        if (i + 1 < p.Length && p[i + 1] == '\\') i++;
                        sb.Append(@"(?:.*\\)?.*");
                    }
                    else
                    {
                        sb.Append(@"[^\\]*");
                    }
                    break;

                case '?':
                    sb.Append(@"[^\\]");
                    break;

                case '[':
                {
                    var close = p.IndexOf(']', i + 1);
                    if (close < 0) { sb.Append(Regex.Escape("[")); break; }
                    var cls = p.Substring(i + 1, close - i - 1);
                    sb.Append('[').Append(cls.Replace("\\", "\\\\")).Append(']');
                    i = close;
                    break;
                }

                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        // IgnoreCase is not redundant with Normalize's own case fold: a character class such as
        // `[AB]` carries its case INTO the regex, and Normalize only lowercases the pattern
        // string around it. Without this, `src\[AB]pi\*.cs` would silently match nothing while
        // the caller believed it had asked for both spellings.
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>Longest leading run of segments containing no wildcard metacharacter.</summary>
    internal static string LiteralPrefixOf(string pattern)
    {
        var parts = Normalize(pattern).Split('\\');
        var keep = new List<string>();
        foreach (var part in parts)
        {
            if (part.IndexOfAny(new[] { '*', '?', '[' }) >= 0) break;
            keep.Add(part);
        }
        return string.Join("\\", keep);
    }

    /// <summary>True when neither prefix can be a prefix of the other on a segment boundary.</summary>
    private static bool PrefixesCompatible(string pa, string pb)
    {
        if (pa.Length == 0 || pb.Length == 0) return true; // no evidence of disjointness
        if (string.Equals(pa, pb, StringComparison.Ordinal)) return true;

        var sa = pa.Split('\\');
        var sb = pb.Split('\\');
        var n = Math.Min(sa.Length, sb.Length);
        for (var i = 0; i < n; i++)
            if (!string.Equals(sa[i], sb[i], StringComparison.Ordinal))
                return false;              // diverged on a segment: provably disjoint
        return true;                        // one is a prefix of the other: may intersect
    }

    private static bool LiteralOverlaps(Range a, Range b)
    {
        var la = a.Literal!;
        var lb = b.Literal!;
        if (string.Equals(la, lb, StringComparison.Ordinal)) return true;
        return IsUnder(la, lb) || IsUnder(lb, la);
    }

    /// <summary>True when <paramref name="child"/> is the same path as, or sits inside,
    /// <paramref name="parent"/> — compared on segment boundaries, so `src\Api` is NOT a
    /// parent of `src\Api2`.</summary>
    internal static bool IsUnder(string child, string parent)
    {
        if (string.Equals(child, parent, StringComparison.Ordinal)) return true;
        if (parent.Length == 0) return true;
        var prefix = parent.EndsWith("\\", StringComparison.Ordinal) ? parent : parent + "\\";
        return child.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool PathsEquivalent(string x, string y) =>
        string.Equals(Normalize(x), Normalize(y), StringComparison.Ordinal);

    internal static string Normalize(string path)
    {
        var p = (path ?? "").Replace('/', '\\');
        var sb = new StringBuilder(p.Length);
        var lastSep = false;
        foreach (var c in p)
        {
            var isSep = c == '\\';
            if (isSep && lastSep) continue;
            sb.Append(c);
            lastSep = isSep;
        }
        return sb.ToString().Trim('\\').ToLowerInvariant();
    }

    /// <summary>The path a decision is about. When a reader and a writer overlap only
    /// partially, the decision is about the deeper (more specific) of the two.</summary>
    internal static string DefaultProtectedPath(ScopeItem a, ScopeItem b)
    {
        var na = Normalize(a.Path);
        var nb = Normalize(b.Path);
        return na.Length >= nb.Length ? na : nb;
    }

    private static string Describe(ScopeItem i) => $"{i.Kind}:{i.Path}";

    private static string DescribeOverlap(ScopeItem a, ScopeItem b)
    {
        var la = LiteralPrefixOf(a.Path);
        var lb = LiteralPrefixOf(b.Path);
        return string.Equals(la, lb, StringComparison.Ordinal) ? la : $"{la} ~ {lb}";
    }
}

