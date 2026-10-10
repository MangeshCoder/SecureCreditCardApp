using System.Net;
using System.Net.Http.Json;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.UnitTests.Api;

/// <summary>Module 8 through HTTP: who may close a cycle, who may read statements, and the PDF download.</summary>
public class BillingIntegrationTests : IClassFixture<SecureEmiApiFactory>
{
    private const string Password = "Pass@1234";
    private readonly SecureEmiApiFactory _api;

    public BillingIntegrationTests(SecureEmiApiFactory api) => _api = api;

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await _api.LoginAsync(email, password));
        return client;
    }

    [Fact]
    public async Task Bank_closes_the_cycle_customer_reads_statement_and_downloads_the_pdf()
    {
        var email = $"bill-{Guid.NewGuid():N}@test.com";
        var card = await _api.CreateCustomerWithCardAsync(email);
        var customer = await ClientAsync(email, Password);
        var admin = await ClientAsync(SecureEmiApiFactory.AdminEmail, SecureEmiApiFactory.AdminPassword);

        await customer.PostAsJsonAsync("/api/transactions/swipe", new SwipeRequest(card.CardNumber, card.Card.ExpiryDate.Month,
            card.Card.ExpiryDate.Year, card.Cvv, card.InitialPin, "Croma", MerchantCategoryCodes.Electronics, 4_000m));

        var url = $"/api/billing/cards/{card.Card.CardId}/statements";
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(url, null)).StatusCode);    // only the bank
        var created = await admin.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var statement = (await created.Content.ReadFromJsonAsync<StatementDto>())!;
        Assert.Equal(3_960m, statement.ClosingBalance);                                           // 4,000 − 1 % cashback

        var list = await customer.GetFromJsonAsync<List<StatementDto>>(url);
        Assert.Equal(statement.StatementId, Assert.Single(list!).StatementId);
        var summary = await customer.GetFromJsonAsync<CardBillingSummaryDto>($"/api/billing/cards/{card.Card.CardId}/summary");
        Assert.Equal(statement.MinimumDue, summary!.RemainingMinimumDue);

        var pdf = await customer.GetAsync($"/api/billing/statements/{statement.StatementId}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("statement-", pdf.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        var otherEmail = $"other-{Guid.NewGuid():N}@test.com";
        await _api.CreateCustomerWithCardAsync(otherEmail);
        var other = await ClientAsync(otherEmail, Password);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/billing/statements/{statement.StatementId}/pdf")).StatusCode);
    }
}
