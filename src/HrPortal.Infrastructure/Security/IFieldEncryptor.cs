namespace HrPortal.Infrastructure.Security;

public interface IFieldEncryptor
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}
