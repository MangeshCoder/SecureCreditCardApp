using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Api.Infrastructure;
using SecureEmiCard.Application.Features.Auth;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Infrastructure.Notifications;

namespace SecureEmiCard.UnitTests.Api;

/// <summary>
/// Starts the REAL API (Program.cs, middleware, filters, controllers) in memory with an in-memory database.
/// Environment "Testing" means appsettings.Development.json is NOT loaded - every setting comes from here.
/// </summary>
public class SecureEmiApiFactory : WebApplicationFactory<Program>
{
    public const string PartnerId = "TEST-ACQUIRER";
    public const string DisabledPartnerId = "DISABLED-BANK";
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin@12345";

    public byte[] EncryptionKey { get; } = RandomNumberGenerator.GetBytes(32);
    public byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (not ConfigureAppConfiguration): with minimal hosting, Program.cs reads configuration
        // while registering services - UseSetting values are visible at that moment, the others arrive too late.
        var settings = new Dictionary<string, string?>
        {
            ["Database:UseInMemory"] = "true",
            ["Database:InMemoryName"] = $"api-tests-{Guid.NewGuid()}",
            ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ["Encryption:CardDataKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Encryption:SecretPepper"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["SeedAdmin:Email"] = AdminEmail, ["SeedAdmin:Password"] = AdminPassword,
            ["RateLimiting:SensitivePermitPerMinute"] = "1000",
            ["Otp:MaxCodesPerWindow"] = "1000",           // the admin signs in (with a code) many times per test class
            ["InterBank:MaxBodyBytes"] = "4096",
            ["InterBank:Partners:0:PartnerId"] = PartnerId,
            ["InterBank:Partners:0:Name"] = "Test Acquirer",
            ["InterBank:Partners:0:EncryptionKey"] = Convert.ToBase64String(EncryptionKey),
            ["InterBank:Partners:0:SigningKey"] = Convert.ToBase64String(SigningKey),
            ["InterBank:Partners:1:PartnerId"] = DisabledPartnerId,
            ["InterBank:Partners:1:Name"] = "Disabled Bank",
            ["InterBank:Partners:1:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["InterBank:Partners:1:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["InterBank:Partners:1:Enabled"] = "false"
        };
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
    }

    /// <summary>Signs in; for the admin this includes the one-time code (Module 7), read from the simulated phone.</summary>
    public async Task<string> LoginAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await SendWithOtpAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    /// <summary>Messages "sent" by the SMS / e-mail simulator.</summary>
    public DevMessageOutbox Outbox => Services.GetRequiredService<DevMessageOutbox>();

    /// <summary>The code from the most recent one-time-code SMS.</summary>
    public string LatestOtpCode() =>
        Outbox.Latest(200).First(m => m.Channel == "Sms" && m.Body.Contains(" is your Secure Credit EMI code ")).Body[..6];

    /// <summary>
    /// What the Angular OTP interceptor does: send; on 428 take the challenge id from the response and the
    /// code from the phone, and send the same request again with the X-Otp-* headers.
    /// </summary>
    public async Task<HttpResponseMessage> SendWithOtpAsync(HttpClient client, Func<HttpRequestMessage> request)
    {
        var response = await client.SendAsync(request());
        if (response.StatusCode != HttpStatusCode.PreconditionRequired) return response;

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var retry = request();
        retry.Headers.Add(HttpOtpProofAccessor.ChallengeHeader, problem.GetProperty("otp").GetProperty("challengeId").GetInt32().ToString());
        retry.Headers.Add(HttpOtpProofAccessor.CodeHeader, LatestOtpCode());
        return await client.SendAsync(retry);
    }

    /// <summary>Registers a customer and issues a card (as admin). Returns the one-time card details.</summary>
    public async Task<IssuedCardResponse> CreateCustomerWithCardAsync(string email, decimal limit = 100_000m)
    {
        var client = CreateClient();
        var registered = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Test", "Customer", email, "+919999999999", "Pass@1234"));
        registered.EnsureSuccessStatusCode();
        var customerId = (await registered.Content.ReadFromJsonAsync<AuthResponse>())!.User.CardholderId;

        client.DefaultRequestHeaders.Authorization =
            new("Bearer", await LoginAsync(AdminEmail, AdminPassword));
        var issued = await client.PostAsJsonAsync("/api/cards", new IssueCardRequest(customerId, limit));
        issued.EnsureSuccessStatusCode();
        return (await issued.Content.ReadFromJsonAsync<IssuedCardResponse>())!;
    }
}
