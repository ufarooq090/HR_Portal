namespace HrPortal.Infrastructure.Security;

/// <summary>
/// Keys for field-level encryption. Supplied by user-secrets locally and Key Vault in Azure; never committed.
/// Old keys stay in <see cref="Keys"/> after rotation so existing values can still be decrypted.
/// </summary>
public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    /// <summary>Id of the key used for new values.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>Key id to base64-encoded 256-bit key.</summary>
    public Dictionary<string, string> Keys { get; set; } = [];
}
