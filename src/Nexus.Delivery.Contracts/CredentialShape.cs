namespace Nexus.Delivery.Contracts;

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
    /// A literal that is excluded from detection <b>by construction</b>: a placeholder, an indirection, a
    /// redaction marker, or a bare name.
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
    /// Two implementations of one rule is one implementation too many. Both callers now call this.
    /// </para>
    /// </summary>
    public static bool IsNonValueLiteral(string? value)
        => IsPlaceholderShape(value)
           || IsReferenceShape(value)
           || IsRedactionMarker(value)
           || IsBareIdentifierShape(value);

    /// <summary>
    /// Whether a string looks like a credential <b>value</b> rather than a name, a placeholder, an
    /// indirection or a redaction.
    /// </summary>
    public static bool LooksLikeCredentialValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 16)
        {
            return false;
        }

        if (IsNonValueLiteral(value))
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
