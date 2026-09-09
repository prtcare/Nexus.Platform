using System.Text.RegularExpressions;

namespace Nexus.Governance.Contracts;

/// <summary>
/// A product's URL-style slug. Format: lowercase letters and digits, words separated by a
/// single '-', no leading or trailing '-', at most 64 characters. The constructor throws
/// <see cref="ArgumentException"/> on any violation.
/// </summary>
public sealed record ProductSlug
{
    private static readonly Regex ValidSlug = new(
        @"^[a-z0-9]+(?:-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant);

    public ProductSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Product slug must not be null or whitespace.", nameof(value));
        }

        if (value.Length > 64 || !ValidSlug.IsMatch(value))
        {
            throw new ArgumentException(
                "Product slug must be lowercase letters and digits, words separated by a single '-', " +
                "with no leading or trailing '-', and at most 64 characters.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
