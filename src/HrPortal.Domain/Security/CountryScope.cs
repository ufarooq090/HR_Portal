namespace HrPortal.Domain.Security;

/// <summary>The set of countries an HR user may see: every country, or a list (possibly empty).</summary>
public sealed record CountryScope
{
    private CountryScope(bool isAll, IReadOnlyList<string> codes)
    {
        IsAll = isAll;
        Codes = codes;
    }

    public static CountryScope All { get; } = new(true, []);

    public static CountryScope None { get; } = new(false, []);

    public bool IsAll { get; }

    /// <summary>Upper-case ISO codes, sorted. Empty when <see cref="IsAll"/>.</summary>
    public IReadOnlyList<string> Codes { get; }

    public bool IsEmpty => !IsAll && Codes.Count == 0;

    public static CountryScope Of(IEnumerable<string> codes) =>
        new(false, codes.Select(c => c.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToList());

    public bool Includes(string countryCode) =>
        IsAll || Codes.Contains(countryCode.ToUpperInvariant(), StringComparer.Ordinal);

    public bool Covers(CountryScope other) => IsAll || (!other.IsAll && other.Codes.All(Includes));

    public bool Equals(CountryScope? other) =>
        other is not null && IsAll == other.IsAll && Codes.SequenceEqual(other.Codes);

    public override int GetHashCode() => IsAll ? 1 : Codes.Aggregate(17, (h, c) => (h * 31) + c.GetHashCode(StringComparison.Ordinal));

    public override string ToString() => IsAll ? "All countries" : Codes.Count == 0 ? "No countries" : string.Join(", ", Codes);
}
