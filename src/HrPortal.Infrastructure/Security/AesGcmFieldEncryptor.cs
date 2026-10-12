using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HrPortal.Infrastructure.Security;

/// <summary>
/// AES-256-GCM field encryption. Stored format: <c>enc:v1:{keyId}:{base64(nonce | tag | ciphertext)}</c>.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _activeKeyId;
    private readonly Dictionary<string, byte[]> _keys;

    public AesGcmFieldEncryptor(IOptions<FieldEncryptionOptions> options)
    {
        var value = options.Value;
        if (value.Keys.Count == 0 || string.IsNullOrWhiteSpace(value.ActiveKeyId))
        {
            throw new InvalidOperationException(
                $"Field encryption is not configured. Set '{FieldEncryptionOptions.SectionName}:ActiveKeyId' and " +
                $"'{FieldEncryptionOptions.SectionName}:Keys:<id>' (user-secrets locally, Key Vault in Azure).");
        }

        _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (id, base64) in value.Keys)
        {
            if (id.Contains(':'))
            {
                throw new InvalidOperationException($"Field encryption key id '{id}' must not contain ':'.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Field encryption key '{id}' is not valid base64.");
            }

            if (key.Length != 32)
            {
                throw new InvalidOperationException($"Field encryption key '{id}' must be 256 bits (32 bytes).");
            }

            _keys[id] = key;
        }

        if (!_keys.ContainsKey(value.ActiveKeyId))
        {
            throw new InvalidOperationException($"Active field encryption key '{value.ActiveKeyId}' is not in the key set.");
        }

        _activeKeyId = value.ActiveKeyId;
    }

    public string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + TagSize + plainBytes.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_keys[_activeKeyId], TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag, AssociatedData(_activeKeyId));

        return Prefix + _activeKeyId + ":" + Convert.ToBase64String(payload);
    }

    public string Decrypt(string ciphertext)
    {
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Value is not in the expected encrypted format.");
        }

        var rest = ciphertext[Prefix.Length..];
        var separator = rest.IndexOf(':');
        if (separator <= 0)
        {
            throw new CryptographicException("Value is not in the expected encrypted format.");
        }

        var keyId = rest[..separator];
        if (!_keys.TryGetValue(keyId, out var key))
        {
            throw new CryptographicException($"Unknown field encryption key '{keyId}'.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(rest[(separator + 1)..]);
        }
        catch (FormatException)
        {
            throw new CryptographicException("Encrypted value is not valid base64.");
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted value is truncated.");
        }

        var plainBytes = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            payload.AsSpan(0, NonceSize),
            payload.AsSpan(NonceSize + TagSize),
            payload.AsSpan(NonceSize, TagSize),
            plainBytes,
            AssociatedData(keyId));

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] AssociatedData(string keyId) => Encoding.UTF8.GetBytes(Prefix + keyId);
}
