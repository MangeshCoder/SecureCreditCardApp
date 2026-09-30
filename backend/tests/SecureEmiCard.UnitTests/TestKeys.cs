using Microsoft.Extensions.Options;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests;

internal static class TestKeys
{
    public static IOptions<EncryptionOptions> Encryption() => Options.Create(new EncryptionOptions
    {
        CardDataKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
        SecretPepper = Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray())
    });
}
