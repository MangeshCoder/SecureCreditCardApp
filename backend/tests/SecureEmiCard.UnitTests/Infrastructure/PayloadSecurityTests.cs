using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using SecureEmiCard.Api.InterBank;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Infrastructure;

public class PayloadSecurityTests
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Aes_cbc_round_trips_with_a_fresh_iv_every_time()
    {
        var sut = new AesCbcPayloadCryptoService();
        var c1 = sut.Encrypt("{\"cardNumber\":\"4111111111111111\"}", _key);
        var c2 = sut.Encrypt("{\"cardNumber\":\"4111111111111111\"}", _key);

        Assert.NotEqual(c1, c2);
        Assert.Equal("{\"cardNumber\":\"4111111111111111\"}", sut.Decrypt(c1, _key));
    }

    [Fact]
    public void Aes_cbc_requires_a_real_256_bit_key()
    {
        var sut = new AesCbcPayloadCryptoService();
        Assert.Throws<CryptographicException>(() => sut.Encrypt("x", new byte[16]));
        Assert.Throws<CryptographicException>(() => sut.Decrypt("AAAA", _key));      // too short
    }

    [Fact]
    public void Hmac_detects_any_change_and_the_wrong_key()
    {
        var sut = new HmacSignatureService();
        var signature = sut.Sign("POST\n/api/gateway/v1/authorize\n1\nabc\n{}", _key);

        Assert.True(sut.Verify("POST\n/api/gateway/v1/authorize\n1\nabc\n{}", signature, _key));
        Assert.False(sut.Verify("POST\n/api/gateway/v1/authorize\n1\nabc\n{ }", signature, _key)); // one space
        Assert.False(sut.Verify("POST\n/api/gateway/v1/authorize\n1\nabc\n{}", signature, RandomNumberGenerator.GetBytes(32)));
        Assert.False(sut.Verify("x", "not-base64!!", _key));
        Assert.Throws<CryptographicException>(() => sut.Sign("x", new byte[8]));             // weak key
    }

    [Fact]
    public void Response_signature_cannot_be_reused_as_a_request_signature()
    {
        var request = InterBankProtocol.RequestCanonical("POST", "/p", "1", "n", "{}");
        var response = InterBankProtocol.ResponseCanonical(200, "1", "n", "n", "{}");
        Assert.NotEqual(request, response);
        Assert.StartsWith("RESPONSE\n", response);
    }

    [Fact]
    public void Nonce_can_be_used_only_once_per_partner()
    {
        var store = new MemoryNonceStore(new MemoryCache(new MemoryCacheOptions()));
        Assert.True(store.TryUse("BANK-A", "nonce-0000000001", TimeSpan.FromMinutes(5)));
        Assert.False(store.TryUse("BANK-A", "nonce-0000000001", TimeSpan.FromMinutes(5)));
        Assert.True(store.TryUse("BANK-B", "nonce-0000000001", TimeSpan.FromMinutes(5)));   // other partner
    }

    [Theory]
    [InlineData("0123456789abcdef", true)]
    [InlineData("short", false)]
    [InlineData("contains spaces in it!!", false)]
    public void Nonce_format_is_checked(string nonce, bool valid) =>
        Assert.Equal(valid, InterBankProtocol.IsValidNonce(nonce));

    [Fact]
    public void Audit_values_are_cut_to_the_column_size_instead_of_failing()
    {
        var row = SecurityAuditLog.Create("Login", new string('e', 400), AuditOutcome.Success, true, "hash",
                                          detail: new string('d', 400), clientIp: new string('1', 60));
        Assert.Equal(250, row.Endpoint.Length);
        Assert.Equal(250, row.Detail!.Length);
        Assert.Equal(45, row.ClientIp!.Length);
    }
}
