using System.Text;
using System.Text.RegularExpressions;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Provider-neutral secret scanning — the W9 build gate.
///
/// <para>
/// <b>What it is for, and why it exists now.</b> W9.0 found a live-class credential committed in the
/// Forge repository, in a tracked file, in history, and reachable from every clone. The estate's own
/// standard already specified the control that would have caught it —
/// <c>CONFIGURATION_STANDARDS.md</c> §13.1 and milestone M-01-5.1: <i>"a secret scan runs in CI and
/// fails the build on a match"</i> — and recorded its status as <i>none</i>. This is that control.
/// </para>
///
/// <para>
/// <b>Neutral by construction.</b> The engine knows no provider. Detection is by
/// <see cref="CredentialShape"/> plus a key vocabulary; a vendor's own key format is supplied as a
/// <see cref="SecretPattern"/> by whoever owns that provider. Platform's neutral assemblies cannot
/// name a provider secret token at all — <c>SecretBoundaryTests</c> fails the build if one appears —
/// so a built-in vendor list is not merely undesirable here, it is impossible.
/// </para>
///
/// <para>
/// <b>Calibrated against real false positives, not imagined ones.</b> The W9.0 sweep produced seven
/// credential-shaped candidates and five were noise: an env-var identifier, a redaction marker, an
/// HTML autocomplete token, a deliberate probe value, and a design-time connection reference. Every
/// one of those five is a test case in <c>Nexus.Delivery.Tests</c>. A gate that fires on
/// <c>current-password</c> gets muted within a week, and a muted gate is worse than none because it
/// is believed.
/// </para>
///
/// <para>
/// <b>Findings carry no part of a value.</b> Not a prefix, not a hash. Scanner output lands in build
/// logs.
/// </para>
/// </summary>
public sealed class SecretScanner
{
    private readonly SecretScanPolicy _policy;
    private readonly Regex _assignmentPattern;
    private readonly IReadOnlyList<(SecretPattern Pattern, Regex Compiled)> _registeredPatterns;

    public SecretScanner(SecretScanPolicy? policy = null)
    {
        _policy = policy ?? SecretScanPolicy.Neutral;

        // A credential-VOCABULARY key, an assignment operator, then a quoted or bare value.
        //
        // There is deliberately no word boundary before the key. `WORKBENCH_DB_PASSWORD=<real value>`
        // is a genuine leak and a boundary that rejected `_`-preceded matches would miss it. Precision
        // is bought by judging the VALUE's shape, not by restricting where the key may begin — which
        // is exactly the correction the W9.0 calibration produced.
        var vocabulary = string.Join("|", _policy.CredentialKeyVocabulary.Select(Regex.Escape));
        _assignmentPattern = new Regex(
            $"(?<key>{vocabulary})\\s*[\"']?\\s*[:=]\\s*[\"'](?<value>[^\"'\\r\\n]{{8,}})[\"']"
            + $"|(?<key>{vocabulary})\\s*[:=]\\s*(?<barevalue>[^\\s\"'#;]{{8,}})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        _registeredPatterns = [.. _policy.RegisteredPatterns.Select(p => (p, new Regex(p.RegularExpression, RegexOptions.Compiled)))];
    }

    /// <summary>Scans a single text. Pure: no I/O, so it is usable on a file, a diff, or a manifest.</summary>
    public IReadOnlyList<SecretScanFinding> ScanText(string relativePath, string text)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(text);

        var findings = new List<SecretScanFinding>();
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            foreach (var (pattern, compiled) in _registeredPatterns)
            {
                if (compiled.IsMatch(line))
                {
                    // The registered pattern is reported by NAME only. Its match is not captured, so
                    // no part of a vendor key can reach a log through this path even accidentally.
                    findings.Add(new SecretScanFinding(relativePath, lineNumber, pattern.Name, "(registered-pattern)", 0));
                }
            }

            // A connection string is a credential when it carries one — and that judgement is made on
            // the LINE, not on a captured value. The reason is measured: MarketSurvey's README commits a
            // 16-character password inside a connection string, and a key-value capture sees only the
            // fragment after `Password=`, which is too short for any shape rule to judge on its own. The
            // connection string is the evidence, so the connection string is what gets examined.
            if (ConnectionStringCarriesCredential(line))
            {
                findings.Add(new SecretScanFinding(relativePath, lineNumber, "connection-string-with-credential", "connection-string", 0));
            }

            foreach (Match match in _assignmentPattern.Matches(line))
            {
                var key = match.Groups["key"].Value;

                // Which of the two alternatives matched is not an implementation detail — it is the syntactic
                // position of the captured text, and it is the only thing that can tell a call expression
                // from a value that looks like one. A quoted capture is a VALUE whatever it contains; only an
                // unquoted operand can be executable syntax. The regex alternation is exclusive, so
                // `value.Success` is the discriminator and there is no third case to handle.
                var quoted = match.Groups["value"].Success;
                var position = quoted
                    ? CredentialCapturePosition.Value
                    : CredentialCapturePosition.UnquotedAssignmentOperand;
                var value = quoted ? match.Groups["value"].Value : match.Groups["barevalue"].Value;

                // An unquoted operand may OPEN with a reference to code — `auth = Reader.AuthorizeWrite(`.
                // The reference is read and set aside, and the remainder is still judged as ordinary text
                // rather than the whole capture being skipped. That distinction is load-bearing in both
                // directions: `Reader.AuthorizeWrite(` loses nothing by it (nothing follows the bracket),
                // while `Reader.AuthorizeWrite(password:TOKEN)` still yields the argument, so recognising a
                // call cannot become a way to hide a credential written inside one.
                if (position == CredentialCapturePosition.UnquotedAssignmentOperand
                    && CredentialShape.TryReadCodeReference(value, out var consumed))
                {
                    value = value[consumed..];

                    // The call's ARGUMENTS are values, and the pattern above cannot reach them: its bare-value
                    // branch stops at the first quote, so a quoted argument is never inside the capture —
                    // `auth = Reader.AuthorizeWrite("…")` captures `Reader.AuthorizeWrite(` and no more. The
                    // argument is therefore read from the rest of the statement and judged as the value it is.
                    // Recognizing a call must not become a way to hide a credential written inside one, and
                    // this is the case where it would have been: measured against the pre-W9.4 scanner, this
                    // line was reported (for the wrong reason, on the code text) and under a brace-skipping fix
                    // it was lost entirely.
                    foreach (var (argument, start, end) in ArgumentLiterals(line, match.Index + match.Length))
                    {
                        // A literal the assignment pattern can reach is left to the pattern, which will
                        // report it with its own key on its own match. This is what keeps one literal from
                        // being judged twice when a named argument is written with a space before it
                        // (`AuthorizeWrite(password: "…")`) — the pattern captures that one itself.
                        if (PatternReaches(line, start, end))
                        {
                            continue;
                        }

                        if (ShouldReport(argument, CredentialCapturePosition.Value))
                        {
                            findings.Add(new SecretScanFinding(relativePath, lineNumber, "credential-shaped-value", key, argument.Length));
                        }
                    }
                }

                if (!ShouldReport(value, position))
                {
                    continue;
                }

                // The length is the length of what was judged — the remainder, when a reference was read
                // past. It is a length and never any part of the text: scanner output lands in build logs.
                findings.Add(new SecretScanFinding(relativePath, lineNumber, "credential-shaped-value", key, value.Length));
            }
        }

        return findings;
    }

    /// <summary>
    /// Scans a directory tree. Files are read as UTF-8 with a replacement fallback: a binary file
    /// produces replacement characters rather than throwing, because a scanner that dies on the first
    /// binary it meets reports nothing about everything after it.
    /// </summary>
    public SecretScanResult ScanDirectory(string rootDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return new SecretScanResult(
                SecretScanVerdict.Incomplete,
                [],
                0,
                0,
                [$"Scan root does not exist: {rootDirectory}"]);
        }

        var findings = new List<SecretScanFinding>();
        var incomplete = new List<string>();
        var scanned = 0;
        var skipped = 0;
        var root = Path.GetFullPath(rootDirectory);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (IsIgnored(relative))
            {
                skipped++;
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                incomplete.Add($"{relative}: length unreadable");
                continue;
            }

            if (length > _policy.MaxFileBytes)
            {
                skipped++;
                incomplete.Add($"{relative}: exceeds {_policy.MaxFileBytes} bytes and was not scanned");
                continue;
            }

            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                findings.AddRange(ScanText(relative, text));
                scanned++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                incomplete.Add($"{relative}: unreadable ({ex.GetType().Name})");
            }
        }

        // A scan that could not read part of its input is never reported Clean. The estate's rule:
        // a suite that cannot run is NOT_RUN, never a pass.
        var verdict = findings.Count > 0
            ? SecretScanVerdict.Findings
            : incomplete.Count > 0
                ? SecretScanVerdict.Incomplete
                : SecretScanVerdict.Clean;

        return new SecretScanResult(verdict, findings, scanned, skipped, incomplete);
    }

    /// <summary>
    /// The quoted literals that follow a recognised call on the same statement, with the spans they occupy in
    /// <paramref name="line"/>.
    ///
    /// <para>
    /// <b>Why they are read separately rather than captured.</b> The assignment pattern's bare-value branch
    /// stops at the first quote or whitespace, which is why it cannot see an argument list at all. The
    /// statement boundary is respected so that a second statement on the same line is left to the pattern's
    /// own next match; the scan also stops at an unterminated quote, because there is no literal to judge and
    /// no boundary left to trust — a scanner that guessed at either would be inventing text.
    /// </para>
    ///
    /// <para>
    /// <b>This finds text; it does not judge it.</b> Whether a literal is a credential stays with
    /// <see cref="CredentialShape.LooksLikeCredentialValue"/>, which is what keeps one judgement in one place.
    /// </para>
    ///
    /// <para>
    /// <b>Line-scoped, and that is a stated limitation rather than an accident.</b> An argument list continued
    /// onto the next line carries no key on that line and is not judged, exactly as every other keyless
    /// literal in this scanner is not judged. Reading across lines would mean holding state between lines in a
    /// gate whose findings are attributed by line, which is a larger change than this correction is.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Value, int Start, int End)> ArgumentLiterals(string line, int from)
    {
        var index = from;

        while (index < line.Length)
        {
            var c = line[index];

            if (c == ';')
            {
                yield break;
            }

            if (c is '"' or '\'')
            {
                var close = line.IndexOf(c, index + 1);

                if (close < 0)
                {
                    yield break;
                }

                yield return (line[(index + 1)..close], index, close + 1);
                index = close + 1;
                continue;
            }

            index++;
        }
    }

    /// <summary>
    /// Whether a span is inside a match of the assignment pattern — i.e. whether the pattern will read that
    /// text itself, with a key of its own. Asked with the same compiled pattern the scan runs on rather than
    /// with a second rule about what a key assignment looks like.
    /// </summary>
    private bool PatternReaches(string line, int start, int end)
    {
        foreach (Match candidate in _assignmentPattern.Matches(line))
        {
            if (candidate.Index <= start && end <= candidate.Index + candidate.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a value should be reported as a credential-shaped literal. The exclusions are the W9.0
    /// false-positive classes, each measured against a real candidate rather than imagined; they are
    /// enforced inside <see cref="CredentialShape.LooksLikeCredentialValue"/> so that the same judgement
    /// is available to the reference type and the lineage guard.
    ///
    /// <para>
    /// The position is carried through rather than decided here. This method knows <i>what</i> the text is;
    /// only the caller knows where it came from, and the judgement itself belongs in one place.
    /// </para>
    /// </summary>
    private static bool ShouldReport(string value, CredentialCapturePosition position)
        => CredentialShape.LooksLikeCredentialValue(value, position);

    /// <summary>
    /// Whether a connection string carries a credential.
    ///
    /// <para>
    /// <b>Connection-string STRUCTURE is required, not merely a password marker.</b> The first version of
    /// this rule fired on any line containing <c>Password=</c> with a non-placeholder tail — and this
    /// scanner promptly reported <i>its own source file</i>, because a doc comment explaining the rule
    /// contains the words "the fragment after <c>Password=</c>, which is too short…", and prose after a
    /// marker looks exactly like a value. Requiring a server/host/user clause is what distinguishes a
    /// connection string from a sentence about one.
    /// </para>
    ///
    /// <para>
    /// An integrated-auth or machine-local string carries no credential: <c>CONFIGURATION_STANDARDS.md</c>
    /// §6.1 permits committing exactly that case, and W9.0 verified both of the estate's committed
    /// strings are of that kind.
    /// </para>
    /// </summary>
    private static bool ConnectionStringCarriesCredential(string value)
    {
        if (!LooksLikeAConnectionString(value))
        {
            return false;
        }

        if (value.Contains("Integrated Security=true", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Trusted_Connection=true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var marker in new[] { "Password=", "Pwd=", "AccountKey=", "SharedAccessSignature=", "sig=" })
        {
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var remainder = value[(index + marker.Length)..];
            var end = remainder.IndexOfAny([';', '"', '\'', ' ', ',']);
            var secret = end < 0 ? remainder : remainder[..end];

            // IsNonValueLiteral, not a hand-rolled pair of checks. The previous version consulted
            // placeholders and indirections but not redaction markers, which made `Password=...` in a
            // documentation table look like a credential and refused a certification for it.
            if (!string.IsNullOrWhiteSpace(secret) && !CredentialShape.IsNonValueLiteral(secret))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeAConnectionString(string value)
    {
        foreach (var clause in new[] { "Server=", "Data Source=", "Host=", "User Id=", "Username=", "Database=" })
        {
            if (value.Contains(clause, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsIgnored(string relativePath)
        => _policy.IgnoreGlobs.Any(glob => GlobMatch(glob, relativePath));

    /// <summary>
    /// Minimal glob: <c>**</c> spans path separators, <c>*</c> does not, and a LEADING <c>**/</c> is
    /// optional so that <c>**/bin/**</c> matches a root-level <c>bin/x</c> as well as <c>a/bin/x</c>.
    /// That second half is not pedantry: without it the default ignore list silently stopped ignoring
    /// <c>bin/</c> at the top of a tree, which is where build output actually lands — a skip that
    /// stopped skipping, discovered by a test rather than by a leaked artefact.
    ///
    /// <para>
    /// Deliberately implemented here rather than taken from a package: a build gate's matching
    /// semantics should not change because a transitive dependency updated.
    /// </para>
    /// </summary>
    private static bool GlobMatch(string glob, string path)
    {
        if (Regex.IsMatch(path, ToRegex(glob), RegexOptions.IgnoreCase))
        {
            return true;
        }

        return glob.StartsWith("**/", StringComparison.Ordinal)
            && Regex.IsMatch(path, ToRegex(glob[3..]), RegexOptions.IgnoreCase);
    }

    private static string ToRegex(string glob)
        => "^" + Regex.Escape(glob)
            .Replace("\\*\\*", "\u0000")
            .Replace("\\*", "[^/]*")
            .Replace("\u0000", ".*") + "$";
}
