using System.Security.Cryptography;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Infrastructure;

public class SecurityServiceTests
{
    [Fact]
    public void Aes_gcm_round_trips_and_uses_a_fresh_nonce_each_time()
    {
        var sut = new AesGcmCardEncryptionService(TestKeys.Encryption());

        var c1 = sut.Encrypt("4111111111111111");
        var c2 = sut.Encrypt("4111111111111111");

        Assert.NotEqual(c1, c2); // random nonce => different cipher texts
        Assert.DoesNotContain("4111", c1);
        Assert.Equal("4111111111111111", sut.Decrypt(c1));
        Assert.Equal("4111111111111111", sut.Decrypt(c2));
    }

    [Fact]
    public void Aes_gcm_detects_tampering()
    {
        var sut = new AesGcmCardEncryptionService(TestKeys.Encryption());
        var cipher = sut.Encrypt("4111111111111111");

        var bytes = Convert.FromBase64String(cipher[3..]);
        bytes[^1] ^= 0x01; // flip one bit of the cipher text
        var tampered = "v1:" + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => sut.Decrypt(tampered));
    }

    [Fact]
    public void Password_hash_is_salted_and_verifies()
    {
        var sut = new Pbkdf2PasswordHasher(iterations: 1_000); // low iteration count keeps the test fast

        var h1 = sut.Hash("Pass@1234");
        var h2 = sut.Hash("Pass@1234");

        Assert.NotEqual(h1, h2);
        Assert.True(sut.Verify("Pass@1234", h1));
        Assert.False(sut.Verify("pass@1234", h1));
        Assert.False(sut.Verify("Pass@1234", "garbage"));
    }

    [Fact]
    public void Pin_hash_depends_on_the_pepper()
    {
        var sut = new PepperedSecretHasher(TestKeys.Encryption(), iterations: 1_000);
        var hash = sut.Hash("2580");

        Assert.True(sut.Verify("2580", hash));
        Assert.False(sut.Verify("2581", hash));

        var otherPepper = Microsoft.Extensions.Options.Options.Create(new EncryptionOptions
        {
            SecretPepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        var attacker = new PepperedSecretHasher(otherPepper, iterations: 1_000);
        Assert.False(attacker.Verify("2580", hash)); // DB dump alone is not enough
    }
}
