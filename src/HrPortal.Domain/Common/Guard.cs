namespace HrPortal.Domain.Common;

/// <summary>Input checks shared by entities. Failures surface to clients as 400 with the field name.</summary>
public static class Guard
{
    public static string RequiredText(string field, string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException(field, "This field is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(field, $"Must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? OptionalText(string field, string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : RequiredText(field, trimmed, maxLength);
    }

    /// <summary>ISO 3166-1 alpha-2, upper-cased.</summary>
    public static string CountryCode(string field, string? value) =>
        UpperLetters(field, value, 2, "Must be a two-letter ISO country code.");

    /// <summary>ISO 4217, upper-cased.</summary>
    public static string CurrencyCode(string field, string? value) =>
        UpperLetters(field, value, 3, "Must be a three-letter ISO currency code.");

    /// <summary>An IANA time zone id such as Asia/Karachi.</summary>
    public static string TimeZone(string field, string? value)
    {
        var id = RequiredText(field, value, 64);
        if (!id.Contains('/') || !TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
        {
            throw new DomainValidationException(field, "Must be an IANA time zone, for example Asia/Karachi.");
        }

        return id;
    }

    public static int Range(string field, int value, int min, int max, string message)
    {
        if (value < min || value > max)
        {
            throw new DomainValidationException(field, message);
        }

        return value;
    }

    private static string UpperLetters(string field, string? value, int length, string message)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (code is null || code.Length != length || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainValidationException(field, message);
        }

        return code;
    }
}
