using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Features.Auth;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Api;

/// <summary>The [Audit] filter records user actions - successful, rejected and failed.</summary>
public class AuditTrailIntegrationTests : IClassFixture<SecureEmiApiFactory>
{
    private readonly SecureEmiApiFactory _api;

    public AuditTrailIntegrationTests(SecureEmiApiFactory api) => _api = api;

    private async Task<List<SecurityAuditLog>> AuditRowsAsync(string actionType)
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecurityAuditLogs
            .Where(a => a.ActionType == actionType).OrderBy(a => a.AuditId).ToListAsync();
    }

    [Fact]
    public async Task Failed_login_is_recorded_as_rejected_without_the_password()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(SecureEmiApiFactory.AdminEmail, "Wrong-Password-123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var row = (await AuditRowsAsync(AuditActions.Login)).Last(a => a.Outcome == AuditOutcome.Rejected);
        Assert.Equal(401, row.HttpStatus);
        Assert.Null(row.UserId);
        Assert.DoesNotContain("Wrong-Password", row.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Card_actions_are_recorded_with_user_and_target()
    {
        var card = await _api.CreateCustomerWithCardAsync($"audit-{Guid.NewGuid():N}@test.com");
        var admin = _api.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new("Bearer", await _api.LoginAsync(SecureEmiApiFactory.AdminEmail, SecureEmiApiFactory.AdminPassword));

        (await admin.PostAsync($"/api/cards/{card.Card.CardId}/block", null)).EnsureSuccessStatusCode();
        var again = await admin.PostAsync($"/api/cards/{card.Card.CardId}/block", null);    // already blocked
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        var rows = (await AuditRowsAsync(AuditActions.CardBlocked))
            .Where(a => a.Detail!.Contains($"cardId={card.Card.CardId}")).ToList();
        Assert.Equal(new[] { AuditOutcome.Success, AuditOutcome.Failed }, rows.Select(r => r.Outcome));
        Assert.All(rows, r => Assert.True(r.SignatureValid));       // JWT verified
        Assert.All(rows, r => Assert.NotNull(r.UserId));
        Assert.Contains("already blocked", rows[1].Detail);

        Assert.Contains(await AuditRowsAsync(AuditActions.CardIssued), a => a.Outcome == AuditOutcome.Success);
    }

    [Fact]
    public async Task Only_admins_can_read_the_audit_trail()
    {
        var card = await _api.CreateCustomerWithCardAsync($"reader-{Guid.NewGuid():N}@test.com");
        var customer = _api.CreateClient();
        var login = await customer.PostAsJsonAsync("/api/auth/login",
            new LoginRequest((await FindEmailAsync(card.Card.CardholderId))!, "Pass@1234"));
        customer.DefaultRequestHeaders.Authorization =
            new("Bearer", (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/audit-logs")).StatusCode);

        var admin = _api.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new("Bearer", await _api.LoginAsync(SecureEmiApiFactory.AdminEmail, SecureEmiApiFactory.AdminPassword));
        var page = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/audit-logs?actionType=Login&outcome=Success");
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 1);
    }

    private async Task<string?> FindEmailAsync(int cardholderId)
    {
        using var scope = _api.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Cardholders.FindAsync(cardholderId))?.Email;
    }
}
