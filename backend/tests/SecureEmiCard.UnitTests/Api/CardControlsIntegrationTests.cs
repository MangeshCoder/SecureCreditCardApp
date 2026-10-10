using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Api;

/// <summary>Module 6 through the real HTTP pipeline: JSON contract, roles and the audit trail.</summary>
public class CardControlsIntegrationTests : IClassFixture<SecureEmiApiFactory>
{
    private const string Password = "Pass@1234"; // set by SecureEmiApiFactory.CreateCustomerWithCardAsync
    private readonly SecureEmiApiFactory _api;

    public CardControlsIntegrationTests(SecureEmiApiFactory api) => _api = api;

    private async Task<HttpClient> ClientForAsync(string email, string password)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await _api.LoginAsync(email, password));
        return client;
    }

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Swipe_json_without_channel_is_still_a_domestic_shop_purchase()
    {
        var email = $"old-client-{Guid.NewGuid():N}@test.com";
        var card = await _api.CreateCustomerWithCardAsync(email);
        var customer = await ClientForAsync(email, Password);

        // Exactly what a Module 2-5 client sends: no "channel", no "merchantCountry".
        var json = $$"""
            {"cardNumber":"{{card.CardNumber}}","expiryMonth":{{card.Card.ExpiryDate.Month}},
             "expiryYear":{{card.Card.ExpiryDate.Year}},"cvv":"{{card.Cvv}}","pin":"{{card.InitialPin}}",
             "merchantName":"Big Bazaar","merchantCategoryCode":"5411","amount":250}
            """;
        var response = await customer.PostAsync("/api/transactions/swipe", new StringContent(json, Encoding.UTF8, "application/json"));

        var result = await response.Content.ReadFromJsonAsync<SwipeResponse>();
        Assert.True(result!.Approved);
        var txn = await InDbAsync(db => db.Transactions.SingleAsync(t => t.TransactionId == result.TransactionId));
        Assert.Equal(TransactionChannel.Pos, txn.Channel);
        Assert.Equal("IN", txn.MerchantCountry);
    }

    [Fact]
    public async Task Cardholder_changes_controls_and_locks_the_card_with_an_audit_trail()
    {
        var email = $"controls-{Guid.NewGuid():N}@test.com";
        var card = await _api.CreateCustomerWithCardAsync(email);
        var customer = await ClientForAsync(email, Password);
        var url = $"/api/cards/{card.Card.CardId}";

        var update = await customer.PutAsJsonAsync($"{url}/controls", new
        {
            pos = new { enabled = true, dailyLimit = (decimal?)null },
            online = new { enabled = true, dailyLimit = 2000 },
            contactless = new { enabled = false, dailyLimit = (decimal?)null },
            atm = new { enabled = true, dailyLimit = (decimal?)null },
            international = new { enabled = false, dailyLimit = (decimal?)null }
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var controls = await update.Content.ReadFromJsonAsync<CardControlsDto>();
        Assert.True(controls!.Online.Enabled);
        Assert.Equal(2_000m, controls.Online.DailyLimit);

        var locked = await customer.PostAsync($"{url}/lock", null);
        Assert.True((await locked.Content.ReadFromJsonAsync<CardDto>())!.IsLocked);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsync($"{url}/lock", null)).StatusCode); // already locked

        var audit = await InDbAsync(db => db.SecurityAuditLogs
            .Where(a => a.Detail!.Contains($"cardId={card.Card.CardId}")).OrderBy(a => a.AuditId).ToListAsync());
        var changed = Assert.Single(audit, a => a.ActionType == AuditActions.CardControlsChanged);
        Assert.Contains("Online on (limit 2000.00)", changed.Detail);
        Assert.Contains("International off", changed.Detail);
        Assert.Equal(new[] { AuditOutcome.Success, AuditOutcome.Failed },
                     audit.Where(a => a.ActionType == AuditActions.CardLocked).Select(a => a.Outcome));
    }

    [Fact]
    public async Task Admin_can_read_but_not_change_a_customers_controls()
    {
        var card = await _api.CreateCustomerWithCardAsync($"support-{Guid.NewGuid():N}@test.com");
        var admin = await ClientForAsync(SecureEmiApiFactory.AdminEmail, SecureEmiApiFactory.AdminPassword);

        var get = await admin.GetAsync($"/api/cards/{card.Card.CardId}/controls");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("online").GetProperty("enabled").GetBoolean());   // RBI default

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/cards/{card.Card.CardId}/lock", null)).StatusCode);
    }
}
