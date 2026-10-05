using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Where a caller captured the text it is asking about. <b>The position is an input to the judgement, not a
/// detail of the caller</b>, because the same characters mean different things in the two positions and no
/// property of the string alone can tell them apart.
///
/// <para>
/// <c>WorkbookCompatibilityReader.AuthorizeWrite(</c> as the unquoted operand of an assignment is a call
/// expression. The identical characters between quotes are a <i>value</i> that happens to look like one, and
/// a value must still be judged as a value. That distinction cannot be recovered from the text, so it is
/// passed in explicitly — and there is no default that guesses, because a guess in either direction is
/// either a missed credential or a scanner that cannot see the repository's own source.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialCapturePosition
{
    /// <summary>
    /// The text was captured as a <b>value</b>: between quotes, or as the value half of a structured literal
    /// such as a connection string's password field. Source syntax is <b>not</b> recognised here. A quoted
    /// run is a value whatever it looks like, and exempting it because its contents resemble an expression
    /// would be an exemption reachable by anyone who can add two characters.
    /// </summary>
    Value,

    /// <summary>
    /// The text was captured as the <b>unquoted operand of an assignment</b>, which is a position where
    /// executable source syntax can appear. Only here may a member-access expression be recognised as code
    /// rather than judged as a credential.
    /// </summary>
    UnquotedAssignmentOperand
}

/// <summary>
/// Provider-neutral, pure heuristics for deciding whether a string <i>looks like a credential value</i>
/// rather than a name, a placeholder, an indirection or a redaction.
///
/// <para>
/// <b>Why this type exists at all.</b> W9.0 calibrated a secret sweep against seven real candidates
/// from this estate. Two were genuine and five were false positives — an env-var identifier
/// (<c>WORKBENCH_DB_PASSWORD</c>), a redaction marker in a comment (<c>[REDACTED]</c>), an HTML
/// autocomplete token (<c>current-password</c>), a deliberate probe value containing
/// <c>not-a-real</c>, and a design-time connection-string reference. The lesson recorded in
/// <c>W8_DEBT_TO_W9.md</c> §5 was exact: <i>a credential is not a word; it is a literal that looks
/// like one</i>, and a scan that fires on <c>current-password</c> will be muted by its operators
/// within a week — a muted scan is worse than none, because it is believed.
/// </para>
///
/// <para>
/// So the discrimination is made on <b>shape</b>, never on vocabulary. This type deliberately knows
/// nothing about any provider: no vendor names, no vendor key prefixes. A provider's own key format
/// is supplied as a registered pattern by whoever owns that provider (<c>ISecretScanPolicy</c>),
/// which is what keeps Platform's neutral assemblies neutral.
/// </para>
///
/// <para>
/// <b>Measured limitation, stated rather than hidden.</b> These heuristics catch machine-generated
/// credentials with high confidence. They do <b>not</b> reliably catch low-entropy,
/// human-chosen passwords such as a short dictionary-word-and-number passphrase. That case needs a
/// registered pattern or a vocabulary match with a higher-recall rule; it is not solved here, and
/// pretending otherwise would make the gate's green mean less than it appears to.
/// </para>
/// </summary>
public static class CredentialShape
{
    /// <summary>
    /// Longest run of letters and digits with no separator. A name separates its words
    /// (<c>NEXUS_OPENAI_API_KEY</c> — longest run 6); a generated credential does not
    /// (<c>sk-</c> followed by 32 unbroken hex characters — longest run 32).
    /// </summary>
    public const int CredentialRunThreshold = 20;

    /// <summary>The longest unbroken letter/digit run in <paramref name="value"/>.</summary>
    public static int LongestUnbrokenAlphanumericRun(ReadOnlySpan<char> value)
    {
        var longest = 0;
        var current = 0;

        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                current++;
                if (current > longest)
                {
                    longest = current;
                }
            }
            else
            {
                current = 0;
            }
        }

        return longest;
    }

    /// <summary>
    /// A value an author left in place of a real one. Matched on shape and on the documented
    /// placeholder vocabulary — the one place vocabulary is the right test, because the author is
    /// telling the reader the value is not real.
    /// </summary>
    public static bool IsPlaceholderShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string[] markers =
        [
            "change-me", "changeme", "change_me", "replace", "your-", "your_", "yourkey",
            "example", "placeholder", "not-a-real", "notreal", "dummy", "fake", "sample",
            "todo", "fixme", "xxx", "****", "redacted", "masked", "set-me", "insert",
            "development-only", "dev-only"
        ];

        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return value.StartsWith('<') || value.StartsWith("${") || value.StartsWith("{{");
    }

    /// <summary>
    /// A value that is an <i>indirection</i> — it names where the value lives rather than being it.
    /// <c>process.env.X</c>, <c>env.X</c>, <c>$env:X</c>, <c>%X%</c>, <c>{{X}}</c>.
    /// </summary>
    public static bool IsReferenceShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("$env:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("$env_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("process.env.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("env.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("$", StringComparison.Ordinal)
            || value.StartsWith("%", StringComparison.Ordinal)
            || value.StartsWith("{{", StringComparison.Ordinal)
            || value.StartsWith("<%", StringComparison.Ordinal))
        {
            return true;
        }

        // A whole-value template such as "${NAME}" or "%NAME%" is an indirection. A value that
        // merely CONTAINS one is not (a connection string that interpolates a password at run time
        // is still a connection string whose literal form must not be committed).
        return value.Length > 2 && value[^1] == '%' && value[0] == '%';
    }

    /// <summary>A value deliberately replaced by a redaction marker in documentation or a test comment.</summary>
    public static bool IsRedactionMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        return trimmed.StartsWith('[') && trimmed.EndsWith(']')
            || trimmed.StartsWith('(') && trimmed.EndsWith(')')
            || trimmed.StartsWith('<') && trimmed.EndsWith('>')
            || trimmed.All(c => c is '*' or '-' or '.' or 'x' or 'X');
    }

    /// <summary>
    /// A bare identifier: one word-shaped token with no separators beyond <c>_</c>. This is the
    /// shape of a variable NAME, which is the single most common false positive — measured, not
    /// assumed: <c>WORKBENCH_DB_PASSWORD</c> was candidate 4 of 7 in the W9.0 sweep.
    ///
    /// <para>
    /// <b>The narrowing here was forced by a real defect, not by a preference.</b> The first version of
    /// this rule returned true for <i>any</i> all-alphanumeric token — which silently swallowed every
    /// unseparated mixed-case credential, including the 16-character password committed in
    /// MarketSurvey's README. A name gives itself away structurally: it separates its words with
    /// <c>_</c>, or carries no digits, or keeps a single case. A generated credential does none of
    /// those. <c>Xk9Qm2Vt7Lp4Rb8Nzt3Hy6Wb1</c> is a single run of mixed case and digits, and that is
    /// exactly what distinguishes it from <c>NEXUS_OPENAI_API_KEY</c>.
    /// </para>
    ///
    /// <para>
    /// The residual false positive is a long camelCase identifier that contains digits and no
    /// separator (<c>base64EncodedValue2</c>). It is reported only when it is the value of a
    /// credential-named key, where a second look costs little.
    /// </para>
    /// </summary>
    public static bool IsBareIdentifierShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!char.IsAsciiLetter(value[0]) && value[0] != '_')
        {
            return false;
        }

        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        var hasLower = value.Any(char.IsAsciiLetterLower);
        var hasUpper = value.Any(char.IsAsciiLetterUpper);
        var hasDigit = value.Any(char.IsAsciiDigit);

        return value.Contains('_')
            || !hasDigit
            || !(hasLower && hasUpper);
    }

    /// <summary>
    /// Reads a <b>reference to code</b> — a dotted member-access chain such as
    /// <c>WorkbookCompatibilityReader.AuthorizeWrite</c> — from the start of <paramref name="text"/>, and
    /// reports how many characters it occupies.
    ///
    /// <para>
    /// <b>Why this is a shape rule and not a keyword list.</b> The scanner's assignment pattern fires on a
    /// credential-named key followed by an assignment operator, and the bare-value branch of that pattern
    /// captures a run of non-whitespace. On an ordinary statement such as
    /// <c>var auth = WorkbookCompatibilityReader.AuthorizeWrite(</c> it therefore captures a C# member-access
    /// expression, and that expression contains a 28-character unbroken alphanumeric run — past
    /// <see cref="CredentialRunThreshold"/> — so Branch A of <see cref="LooksLikeCredentialValue"/> reported
    /// it. The scanner was reading source code as if it were a value.
    /// </para>
    ///
    /// <para>
    /// <b>The discriminator is structure, not length.</b> Raising
    /// <see cref="CredentialRunThreshold"/> would answer this finding by letting shorter real keys through,
    /// which is the wrong trade. What actually separates the two is that the captured text <i>opens with</i> a
    /// chain of <i>names joined by dots</i>, whereas a credential literal is a single token. Every segment
    /// must satisfy <see cref="IsBareIdentifierShape"/>, so a chain that carries a dense mixed-case token —
    /// <c>eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0</c> is the shape — is <b>not</b>
    /// a code reference and is still reported. The dotted form alone is not an exemption: a JSON web token is
    /// dotted and must keep being caught.
    /// </para>
    ///
    /// <para>
    /// <b>It reports a LENGTH rather than a verdict, and that is the point.</b> A call expression and its
    /// argument list are different things: the reference is <c>A.B(</c> and an argument may follow it. A
    /// predicate that answered only yes-or-no about the whole captured run would have to either exempt the
    /// arguments with the reference — losing a credential written as an argument — or refuse to recognise the
    /// call at all. Reading the reference and leaving the remainder to be judged keeps both. The two yes-or-no
    /// forms are <see cref="IsCodeReferenceShape"/> (chain, with or without call syntax) and
    /// <see cref="IsQualifiedNameShape"/> (chain only), both defined from this one parse so that none of the
    /// three can disagree about where a reference ends.
    /// </para>
    ///
    /// <para>
    /// <b>Measured limitation, stated rather than hidden.</b> A dotted credential whose every segment
    /// happens to be name-shaped — pure letters, or letters with a single case, carrying no digit — is
    /// indistinguishable from a dotted member access and will not be reported. No machine-generated
    /// credential format known to this estate has that shape; a credential is generated, and generation is
    /// what makes a token dense. The limitation is inherited from the name rule rather than introduced here:
    /// it is the same reason a bare <c>WorkbenchDatabasePassword</c> is not reported.
    /// </para>
    /// </summary>
    /// <param name="text">The captured text. Nothing before the reference is skipped: the reference must open it.</param>
    /// <param name="consumedLength">
    /// How many characters the reference occupies, from index 0. Includes the opening bracket of an argument
    /// list and an immediately closing <c>()</c>, because those belong to the call syntax and not to the
    /// reference. Zero when there is no reference.
    /// </param>
    public static bool TryReadCodeReference(string? text, out int consumedLength)
        => TryReadMemberChain(text, out _, out consumedLength);

    /// <summary>
    /// The <b>one</b> parse behind both shape predicates and the scanner's own reading: a dotted chain of
    /// name-shaped segments, optionally followed by the opening of an argument list (with an immediately
    /// closing <c>()</c> consumed too, since that is still call syntax rather than an argument).
    ///
    /// <para>
    /// It reports the chain's length separately from the reference's, because the two questions callers ask —
    /// "is this wholly a qualified name?" and "is this wholly a reference to code?" — differ only in whether a
    /// bracket was consumed. Deriving both from one parse is what stops them drifting apart, which is the
    /// defect class this estate has now recorded four times.
    /// </para>
    /// </summary>
    private static bool TryReadMemberChain(string? text, out int chainLength, out int consumedLength)
    {
        chainLength = 0;
        consumedLength = 0;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = 0;
        var segments = 0;

        while (true)
        {
            if (index >= text.Length || (!char.IsAsciiLetter(text[index]) && text[index] != '_'))
            {
                return false;
            }

            var start = index;

            while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] == '_'))
            {
                index++;
            }

            // The load-bearing half. A segment that is not itself name-shaped is a generated token, not an
            // identifier, and therefore the text is not a reference to code. This is the same judgement the
            // bare-identifier rule already makes, applied per segment rather than re-derived: one rule, one
            // implementation.
            if (!IsBareIdentifierShape(text[start..index]))
            {
                return false;
            }

            segments++;

            if (index < text.Length && text[index] == '.')
            {
                // A trailing dot is not a chain, and an empty segment is not a name.
                if (index + 1 >= text.Length)
                {
                    return false;
                }

                index++;
                continue;
            }

            break;
        }

        // One dot at minimum: a bare identifier is already `IsBareIdentifierShape`'s case, and a text with no
        // separator has no chain structure to justify recognising it as a reference.
        if (segments < 2)
        {
            return false;
        }

        if (index == text.Length)
        {
            chainLength = index;
            consumedLength = index;
            return true;
        }

        // An argument list belongs to the call site, not to the reference: `A.B(`, `A.B()` and `A.B(read,`
        // all name the member `A.B`. Exactly the bracket and an immediately closing bracket are consumed;
        // everything after them is the caller's to judge, which is how an argument cannot become a blind
        // spot. A space is not reached here — a reference is a single token — and any other trailing
        // character (a `-`, a `:`, an `=`) means the opening run was never a bare member access.
        if (text[index] == '(')
        {
            chainLength = index;
            consumedLength = index + 1 < text.Length && text[index + 1] == ')' ? index + 2 : index + 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the <b>whole</b> of <paramref name="value"/> is a qualified <b>name</b>: a dotted chain of
    /// name-shaped segments with no call syntax — <c>Nexus.ProductCore.Core.DevelopmentControl</c>.
    ///
    /// <para>
    /// This is <see cref="IsBareIdentifierShape"/> generalised across dots, and it is deliberately
    /// <b>position-independent</b>: a name is a name whether it appears bare or between quotes, and the
    /// existing bare-identifier rule is position-independent for the same reason. A caller quoting a name has
    /// not made it a value, but it was never a credential candidate either.
    /// </para>
    ///
    /// <para>
    /// <b>Call syntax is not a name, and that difference is load-bearing.</b> <c>A.B(</c> is not a
    /// qualified name; it is executable syntax, it can only be code, and it is recognised only in the
    /// unquoted operand position (<see cref="IsCodeReferenceShape"/>). Which means the identical text quoted
    /// <i>is</i> judged as a value and reported — the discriminator that neither former implementation had.
    /// </para>
    /// </summary>
    public static bool IsQualifiedNameShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        return TryReadMemberChain(text, out var chain, out var consumed)
            && consumed == chain
            && chain == text.Length;
    }

    /// <summary>
    /// Whether the <b>whole</b> of <paramref name="value"/> is a reference to code: a dotted member-access
    /// chain, optionally with an empty or open argument list, and nothing else.
    ///
    /// <para>
    /// Defined from the same parse as <see cref="IsQualifiedNameShape"/> rather than re-parsing, so there is
    /// one implementation of "what a reference is". The whole-text requirement is what keeps an argument list
    /// from being exempted along with the call: <c>A.B(</c> is a reference, and <c>A.B(read,</c> is a reference
    /// followed by text that still has to be judged.
    /// </para>
    /// </summary>
    public static bool IsCodeReferenceShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        return TryReadCodeReference(text, out var consumed) && consumed == text.Length;
    }

    /// <summary>
    /// A literal that is excluded from detection <b>by construction</b>: a placeholder, an indirection, a
    /// redaction marker, a bare name, or a reference to code rather than to a value.
    ///
    /// <para>
    /// <b>This is the single place that judgement is made, and that is the point.</b> It was previously made
    /// twice — once on the value path and once on the connection-string path — and the two diverged: the
    /// connection-string path consulted placeholders and indirections but omitted the redaction check, so
    /// <c>Password=...</c> in a documentation table was extracted as a three-character "credential" and
    /// reported. The W9.2 first proof run found exactly that, in three documentation files, and refused
    /// certification for it — which is how a false positive is supposed to surface, and also why a gate that
    /// fires on an ellipsis would have been muted within a week had nobody looked.
    /// </para>
    ///
    /// <para>
    /// Two implementations of one rule is one implementation too many. Both callers now call this — and the
    /// code-reference rule was added HERE rather than at either call site for the same reason: a new judgement
    /// is not permitted to exist in two places.
    /// </para>
    ///
    /// <para>
    /// <b>The position is a parameter and not a second rule.</b> Every clause above is a shape test on the
    /// text; the code-reference clause is the one clause whose <i>applicability</i> depends on where the text
    /// was captured, because the identical characters are a call expression in one position and a value in the
    /// other. Keeping that condition here — one clause, one place — is what stops it becoming a second
    /// implementation of "is this a value?" that drifts from this one.
    /// </para>
    ///
    /// <para>
    /// <b>The code-reference clause is the only position-dependent one, and that is deliberate.</b> The name
    /// clause covers a qualified name in either position, because a name is what it is wherever it is written
    /// — the same reason the bare-identifier clause beside it has never been position-dependent. What the
    /// position decides is narrower and sharper: whether text may be read as <i>executable syntax</i>. An
    /// unquoted operand can be; a quoted value cannot, because quoting it removed it from the language. So
    /// <c>Reader.AuthorizeWrite(</c> bare is code and <c>"Reader.AuthorizeWrite("</c> quoted is a value that
    /// is reported, while <c>Nexus.Core.Contracts</c> is a name either way.
    /// </para>
    ///
    /// <para>
    /// The default is <see cref="CredentialCapturePosition.Value"/> deliberately: it is the position that
    /// recognises <b>fewest</b> exemptions, so a caller that does not say where its text came from gets the
    /// conservative answer rather than a silent exemption it never asked for.
    /// </para>
    /// </summary>
    public static bool IsNonValueLiteral(
        string? value,
        CredentialCapturePosition position = CredentialCapturePosition.Value)
        => IsPlaceholderShape(value)
           || IsReferenceShape(value)
           || IsRedactionMarker(value)
           || IsBareIdentifierShape(value)
           || IsQualifiedNameShape(value)
           || (position == CredentialCapturePosition.UnquotedAssignmentOperand && IsCodeReferenceShape(value));

    /// <summary>
    /// Whether a string looks like a credential <b>value</b> rather than a name, a placeholder, an
    /// indirection or a redaction.
    /// </summary>
    /// <param name="value">The captured text.</param>
    /// <param name="position">
    /// Where it was captured. Defaults to <see cref="CredentialCapturePosition.Value"/> — see
    /// <see cref="IsNonValueLiteral"/> for why the conservative position is the default.
    /// </param>
    public static bool LooksLikeCredentialValue(
        string? value,
        CredentialCapturePosition position = CredentialCapturePosition.Value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 16)
        {
            return false;
        }

        if (IsNonValueLiteral(value, position))
        {
            return false;
        }

        var hasLower = false;
        var hasUpper = false;
        var hasDigit = false;

        foreach (var c in value)
        {
            if (char.IsAsciiLetterLower(c))
            {
                hasLower = true;
            }
            else if (char.IsAsciiLetterUpper(c))
            {
                hasUpper = true;
            }
            else if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
            }
        }

        // Branch A — a long unbroken machine-generated run. Catches the DeepSeek-shaped case
        // (a prefix, a hyphen, then 32 unbroken hex characters) without knowing what "sk-" means.
        if (LongestUnbrokenAlphanumericRun(value) >= CredentialRunThreshold)
        {
            return true;
        }

        // Ordinary prose is excluded from Branches B and C. Without this, "W9.1 promotion proof."
        // — a perfectly ordinary reason string — satisfied "has upper, has lower, has a digit and is
        // 20 characters long" and was reported as a credential. Mixed classes must be DENSE, not
        // merely present, and a space means the value is a sentence rather than a key.
        if (value.Any(char.IsWhiteSpace) || value.Contains('/') || value.Contains('\\'))
        {
            return false;
        }

        // Branch B — dense mixed case plus a digit: the shape of a generated password or token.
        if (value.Length >= 20 && hasLower && hasUpper && hasDigit)
        {
            return true;
        }

        // Branch C — long, lower-case-or-digit: the shape of a base64url or hex blob.
        return value.Length >= 32 && hasDigit && !hasUpper;
    }

    /// <summary>
    /// Whether a string is shaped like <b>the name of</b> a secret, which is the only thing a
    /// contract, a manifest or a lineage record may carry.
    ///
    /// <para>
    /// This is a positive test with a negative backstop, and the backstop is the load-bearing half:
    /// a credential-shaped value such as <c>sk-…</c> begins with a letter and contains only
    /// name-safe characters, so a positive-only rule would accept it. A name also separates its
    /// words; a credential does not.
    /// </para>
    /// </summary>
    public static bool IsNameShaped(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || value.Length > 200)
        {
            return false;
        }

        if (!char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or '/' or ':'))
            {
                return false;
            }
        }

        return LongestUnbrokenAlphanumericRun(value) < CredentialRunThreshold;
    }
}
