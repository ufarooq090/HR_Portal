using System.Security.Cryptography;
using HrPortal.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace HrPortal.UnitTests.Security;

public class AesGcmFieldEncryptorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmFieldEncryptor Create(string activeKeyId, Dictionary<string, string> keys) =>
        new(Options.Create(new FieldEncryptionOptions { ActiveKeyId = activeKeyId, Keys = keys }));

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var encryptor = Create("k1", new() { ["k1"] = NewKey() });

        var ciphertext = encryptor.Encrypt("42101-1234567-1");

        Assert.StartsWith("enc:v1:k1:", ciphertext);
        Assert.DoesNotContain("42101-1234567-1", ciphertext);
        Assert.Equal("42101-1234567-1", encryptor.Decrypt(ciphertext));
    }

    [Fact]
    public void Same_plaintext_encrypts_differently_each_time()
    {
        var encryptor = Create("k1", new() { ["k1"] = NewKey() });

        Assert.NotEqual(encryptor.Encrypt("123-45-6789"), encryptor.Encrypt("123-45-6789"));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var encryptor = Create("k1", new() { ["k1"] = NewKey() });
        var ciphertext = encryptor.Encrypt("PK36SCBL0000001123456702");
        var payload = Convert.FromBase64String(ciphertext["enc:v1:k1:".Length..]);
        payload[^1] ^= 0x01;

        var tampered = "enc:v1:k1:" + Convert.ToBase64String(payload);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(tampered));
    }

    [Fact]
    public void Values_encrypted_with_a_rotated_out_key_still_decrypt()
    {
        var oldKey = NewKey();
        var before = Create("k1", new() { ["k1"] = oldKey });
        var ciphertext = before.Encrypt("secret");

        var after = Create("k2", new() { ["k1"] = oldKey, ["k2"] = NewKey() });

        Assert.Equal("secret", after.Decrypt(ciphertext));
        Assert.StartsWith("enc:v1:k2:", after.Encrypt("secret"));
    }

    [Fact]
    public void Unknown_key_id_is_rejected()
    {
        var a = Create("a", new() { ["a"] = NewKey() });
        var b = Create("b", new() { ["b"] = NewKey() });

        Assert.ThrowsAny<CryptographicException>(() => b.Decrypt(a.Encrypt("x")));
    }

    [Fact]
    public void Missing_configuration_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => Create("", []));
    }

    [Fact]
    public void Key_of_wrong_length_fails_fast()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Assert.Throws<InvalidOperationException>(() => Create("k1", new() { ["k1"] = shortKey }));
    }
}
