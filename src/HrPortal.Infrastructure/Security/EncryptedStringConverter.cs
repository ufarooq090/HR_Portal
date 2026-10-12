using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HrPortal.Infrastructure.Security;

public sealed class EncryptedStringConverter(IFieldEncryptor encryptor)
    : ValueConverter<string, string>(v => encryptor.Encrypt(v), v => encryptor.Decrypt(v));
