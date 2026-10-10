using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Api.Infrastructure;
using SecureEmiCard.Application.Features.Auth;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Notifications;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Api;

/// <summary>Module 7 through HTTP: the 428 → code → retry protocol, the audit trail, the inbox and SMS delivery.</summary>
public class OtpAndNotificationsIntegrationTests : IClassFixture<SecureEmiApiFactory>
{
    private const string Password = "Pass@1234";
    private readonly SecureEmiApiFactory _api;

    public OtpAndNotificationsIntegrationTests(SecureEmiApiFactory api) => _api = api;

    private static HttpRequestMessage AdminLogin() => new(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new LoginRequest(SecureEmiApiFactory.AdminEmail, SecureEmiApiFactory.AdminPassword))
    };

    private async Task<HttpClient> CustomerAsync(string email)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await _api.LoginAsync(email, Password));
        return client;
    }

    [Fact]
    public async Task Admin_sign_in_needs_the_code_from_the_phone()
    {
        var client = _api.CreateClient();

        var first = await client.SendAsync(AdminLogin());
        Assert.Equal(HttpStatusCode.PreconditionRequired, first.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        var otp = JsonDocument.Parse(body).RootElement.GetProperty("otp");
        var code = _api.LatestOtpCode();
        Assert.DoesNotContain(code, body);                                   // the code only goes to the phone
        Assert.EndsWith("0000", otp.GetProperty("sentTo").GetString());
        Assert.Contains("*", otp.GetProperty("sentTo").GetString());

        var wrong = AdminLogin();
        wrong.Headers.Add(HttpOtpProofAccessor.ChallengeHeader, otp.GetProperty("challengeId").GetInt32().ToString());
        wrong.Headers.Add(HttpOtpProofAccessor.CodeHeader, code == "000000" ? "111111" : "000000");
        var rejected = await client.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.True((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("otpFailed").GetBoolean());

        var right = AdminLogin();
        right.Headers.Add(HttpOtpProofAccessor.ChallengeHeader, otp.GetProperty("challengeId").GetInt32().ToString());
        right.Headers.Add(HttpOtpProofAccessor.CodeHeader, code);
        var signedIn = await client.SendAsync(right);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.False(string.IsNullOrEmpty((await signedIn.Content.ReadFromJsonAsync<AuthResponse>())!.Token));

        using var scope = _api.Services.CreateScope();
        var outcomes = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecurityAuditLogs
            .Where(a => a.ActionType == AuditActions.Login).OrderByDescending(a => a.AuditId).Take(3)
            .Select(a => a.Outcome).ToListAsync();
        Assert.Equal(new[] { AuditOutcome.Success, AuditOutcome.Rejected, AuditOutcome.Challenged }, outcomes);
    }

    [Fact]
    public async Task Online_purchase_with_code_then_alert_in_the_inbox_and_by_sms()
    {
        var email = $"alerts-{Guid.NewGuid():N}@test.com";
        var card = await _api.CreateCustomerWithCardAsync(email);
        var customer = await CustomerAsync(email);

        // Switch online on (riskier → code), then pay online (always → code).
        var controls = await _api.SendWithOtpAsync(customer, () => new HttpRequestMessage(HttpMethod.Put, $"/api/cards/{card.Card.CardId}/controls")
        {
            Content = JsonContent.Create(new
            {
                pos = new { enabled = true }, online = new { enabled = true }, contactless = new { enabled = false },
                atm = new { enabled = true }, international = new { enabled = false }
            })
        });
        Assert.Equal(HttpStatusCode.OK, controls.StatusCode);

        var swipe = new SwipeRequest(card.CardNumber, card.Card.ExpiryDate.Month, card.Card.ExpiryDate.Year, card.Cvv,
            card.InitialPin, "Flipkart", MerchantCategoryCodes.Electronics, 2_499m, TransactionChannel.Online);
        var paid = await _api.SendWithOtpAsync(customer, () => new HttpRequestMessage(HttpMethod.Post, "/api/transactions/swipe")
        {
            Content = JsonContent.Create(swipe)
        });
        Assert.True((await paid.Content.ReadFromJsonAsync<SwipeResponse>())!.Approved);

        // In-app inbox
        var inbox = await customer.GetFromJsonAsync<JsonElement>("/api/notifications");
        var titles = inbox.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("title").GetString()).ToList();
        Assert.Contains("₹2,499.00 spent at Flipkart", titles);
        Assert.Contains("Card controls changed", titles);
        Assert.True((await customer.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Count >= 3);

        // SMS + e-mail delivery by the dispatcher (run now instead of waiting for its timer)
        await _api.Services.GetRequiredService<NotificationDispatcher>().DispatchPendingAsync();
        Assert.Contains(_api.Outbox.Latest(200), m => m.Channel == "Sms" && m.To == "+919999999999" && m.Body.Contains("spent at Flipkart"));
        Assert.Contains(_api.Outbox.Latest(200), m => m.Channel == "Email" && m.To == email && m.Subject!.Contains("spent at Flipkart"));

        Assert.Equal(0, (await (await customer.PostAsync("/api/notifications/read-all", null)).Content.ReadFromJsonAsync<UnreadCountDto>())!.Count);
        Assert.Equal(0, (await customer.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Count);
    }

    [Fact]
    public async Task Nobody_reads_or_marks_someone_elses_alerts()
    {
        var aliceEmail = $"alice-{Guid.NewGuid():N}@test.com";
        await _api.CreateCustomerWithCardAsync(aliceEmail);
        var alice = await CustomerAsync(aliceEmail);
        var aliceAlert = (await alice.GetFromJsonAsync<JsonElement>("/api/notifications"))
            .GetProperty("items")[0].GetProperty("notificationId").GetInt32();

        var bobEmail = $"bob-{Guid.NewGuid():N}@test.com";
        await _api.CreateCustomerWithCardAsync(bobEmail);
        var bob = await CustomerAsync(bobEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/notifications/{aliceAlert}/read", null)).StatusCode);
        var bobsInbox = await bob.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.DoesNotContain(bobsInbox.GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationId").GetInt32() == aliceAlert);
    }

    [Fact]
    public async Task Phone_simulator_endpoint_exists_only_in_development()
    {
        var response = await _api.CreateClient().GetAsync("/api/dev/messages");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);   // the test host runs as "Testing"
    }
}
