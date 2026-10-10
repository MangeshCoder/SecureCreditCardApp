using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Api.InterBank;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests.Api;

/// <summary>
/// The inter-bank gateway attacked from the outside, through the real HTTP pipeline.
/// The test acts as the partner bank: it encrypts and signs requests itself.
/// </summary>
public class GatewayIntegrationTests : IClassFixture<SecureEmiApiFactory>
{
    private const string Path = "/api/gateway/v1/authorize";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SecureEmiApiFactory _api;
    private readonly AesCbcPayloadCryptoService _crypto = new();
    private readonly HmacSignatureService _signer = new();

    public GatewayIntegrationTests(SecureEmiApiFactory api) => _api = api;

    // ---- helpers acting as the partner bank ----------------------------------------------

    private record SignedRequest(string Body, string Timestamp, string Nonce, string Signature, string PartnerId);

    private SignedRequest Sign(object payload, string? partnerId = null, long? unixTime = null, byte[]? signingKey = null,
                               string? rawBody = null)
    {
        var body = rawBody ?? JsonSerializer.Serialize(
            new EncryptedEnvelope(_crypto.Encrypt(JsonSerializer.Serialize(payload, Json), _api.EncryptionKey)), Json);
        var timestamp = (unixTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var signature = _signer.Sign(InterBankProtocol.RequestCanonical("POST", Path, timestamp, nonce, body),
                                     signingKey ?? _api.SigningKey);
        return new SignedRequest(body, timestamp, nonce, signature, partnerId ?? SecureEmiApiFactory.PartnerId);
    }

    private Task<HttpResponseMessage> SendAsync(SignedRequest r, string? bodyOverride = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(bodyOverride ?? r.Body, Encoding.UTF8, "application/json")
        };
        message.Headers.Add(InterBankProtocol.PartnerIdHeader, r.PartnerId);
        message.Headers.Add(InterBankProtocol.TimestampHeader, r.Timestamp);
        message.Headers.Add(InterBankProtocol.NonceHeader, r.Nonce);
        message.Headers.Add(InterBankProtocol.SignatureHeader, r.Signature);
        return _api.CreateClient().SendAsync(message);
    }

    /// <summary>Verifies OUR signature on the response, then decrypts it - what a partner must do.</summary>
    private async Task<T> ReadSecureAsync<T>(HttpResponseMessage response, SignedRequest request)
    {
        var body = await response.Content.ReadAsStringAsync();
        var canonical = InterBankProtocol.ResponseCanonical((int)response.StatusCode,
            response.Headers.GetValues(InterBankProtocol.TimestampHeader).Single(),
            response.Headers.GetValues(InterBankProtocol.NonceHeader).Single(),
            request.Nonce, body);
        Assert.True(_signer.Verify(canonical, response.Headers.GetValues(InterBankProtocol.SignatureHeader).Single(), _api.SigningKey),
                    "Response signature must be valid");

        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(body, Json)!;
        return JsonSerializer.Deserialize<T>(_crypto.Decrypt(envelope.Payload, _api.EncryptionKey), Json)!;
    }

    private static SwipeRequest SwipeFor(IssuedCardResponse c, decimal amount) =>
        new(c.CardNumber, c.Card.ExpiryDate.Month, c.Card.ExpiryDate.Year, c.Cvv, c.InitialPin,
            "Amazon India", MerchantCategoryCodes.Electronics, amount);

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ---- tests ---------------------------------------------------------------------------

    [Fact]
    public async Task Signed_encrypted_request_is_authorized_and_answered_encrypted_and_signed()
    {
        var card = await _api.CreateCustomerWithCardAsync($"ok-{Guid.NewGuid():N}@test.com");
        var request = Sign(SwipeFor(card, 2_500m));

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("approved", raw);                       // nothing readable on the wire
        var result = await ReadSecureAsync<SwipeResponse>(response, request);
        Assert.True(result.Approved);
        Assert.Equal(25m, result.CashbackAmount);                     // Module 3 still applies

        var stored = await InDbAsync(db => db.Transactions.SingleAsync(t => t.TransactionId == result.TransactionId));
        Assert.Equal(request.Signature, stored.DigitalSignature);     // non-repudiation
        var audit = await InDbAsync(db => db.SecurityAuditLogs
            .Where(a => a.ActionType == AuditActions.GatewayAuthorize && a.Outcome == AuditOutcome.Success)
            .OrderByDescending(a => a.AuditId).FirstAsync());
        Assert.Equal(SecureEmiApiFactory.PartnerId, audit.PartnerId);
        Assert.True(audit.SignatureValid);
        Assert.Equal(64, audit.PayloadHash.Length);                    // SHA-256 hex of the encrypted body
    }

    [Fact]
    public async Task Tampered_body_is_rejected()
    {
        var card = await _api.CreateCustomerWithCardAsync($"tamper-{Guid.NewGuid():N}@test.com");
        var request = Sign(SwipeFor(card, 10m));
        var i = request.Body.IndexOf("\"payload\":\"", StringComparison.Ordinal) + 15;
        var tampered = request.Body[..i] + (request.Body[i] == 'A' ? 'B' : 'A') + request.Body[(i + 1)..];

        var response = await SendAsync(request, tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Request authentication failed", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains(InterBankProtocol.SignatureHeader));
    }

    [Fact]
    public async Task Replayed_request_is_rejected()
    {
        var card = await _api.CreateCustomerWithCardAsync($"replay-{Guid.NewGuid():N}@test.com");
        var request = Sign(SwipeFor(card, 100m));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(request)).StatusCode);   // same nonce

        var replays = await InDbAsync(db => db.SecurityAuditLogs.CountAsync(a => a.Detail!.StartsWith("Replayed")));
        Assert.True(replays >= 1);
        var swipes = await InDbAsync(db => db.Transactions.CountAsync(t => t.DigitalSignature == request.Signature));
        Assert.Equal(1, swipes);                                                             // charged once
    }

    [Theory]
    [InlineData(-600)]   // 10 minutes old
    [InlineData(600)]    // 10 minutes in the future
    public async Task Request_outside_the_time_window_is_rejected(int offsetSeconds)
    {
        var request = Sign(new { }, unixTime: DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Wrong_key_unknown_partner_and_disabled_partner_are_rejected_with_the_same_message()
    {
        var wrongKey = await SendAsync(Sign(new { }, signingKey: RandomNumberGenerator.GetBytes(32)));
        var unknown = await SendAsync(Sign(new { }, partnerId: "EVIL-BANK"));
        var disabled = await SendAsync(Sign(new { }, partnerId: SecureEmiApiFactory.DisabledPartnerId));

        foreach (var r in new[] { wrongKey, unknown, disabled })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("{\"error\":\"Request authentication failed.\"}", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Missing_headers_and_a_valid_user_jwt_are_not_enough()
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", await _api.LoginAsync(SecureEmiApiFactory.AdminEmail, SecureEmiApiFactory.AdminPassword));

        var response = await client.PostAsJsonAsync(Path, new { cardNumber = "4111111111111111" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);   // even an Admin JWT cannot use the gateway
    }

    [Fact]
    public async Task Authentic_but_undecryptable_payload_is_a_400()
    {
        var body = JsonSerializer.Serialize(new EncryptedEnvelope(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))), Json);
        var response = await SendAsync(Sign(new { }, rawBody: body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_body_is_refused()
    {
        var body = JsonSerializer.Serialize(new EncryptedEnvelope(new string('A', 8_000)), Json); // limit is 4 KB in tests
        var response = await SendAsync(Sign(new { }, rawBody: body));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Card_controls_apply_to_partner_bank_requests_too()
    {
        // Module 6: a partner bank sends channel + country; the cardholder never switched on online use.
        var card = await _api.CreateCustomerWithCardAsync($"ctl-{Guid.NewGuid():N}@test.com");
        var request = Sign(new
        {
            card.CardNumber, ExpiryMonth = card.Card.ExpiryDate.Month, ExpiryYear = card.Card.ExpiryDate.Year,
            card.Cvv, Pin = card.InitialPin, MerchantName = "Amazon US", MerchantCategoryCode = "5732",
            Amount = 1_000m, Channel = "Online", MerchantCountry = "US"
        });

        var result = await ReadSecureAsync<SwipeResponse>(await SendAsync(request), request);

        Assert.False(result.Approved);
        Assert.Equal(DeclineReasons.ChannelDisabled(TransactionChannel.Online), result.DeclineReason);
        var stored = await InDbAsync(db => db.Transactions.SingleAsync(t => t.TransactionId == result.TransactionId));
        Assert.True(stored.IsInternational);
        Assert.Equal(request.Signature, stored.DigitalSignature);
    }

    [Fact]
    public async Task Validation_errors_are_returned_inside_the_secure_channel()
    {
        var card = await _api.CreateCustomerWithCardAsync($"val-{Guid.NewGuid():N}@test.com");
        var request = Sign(SwipeFor(card, 0m));

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadSecureAsync<JsonElement>(response, request);   // signed + encrypted
        Assert.True(problem.GetProperty("errors").TryGetProperty("amount", out _));
    }
}
