using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Otp;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Application;

/// <summary>Module 7: the step-up authenticator itself - issuing, verifying and protecting one-time codes.</summary>
public class OtpFlowTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private readonly AppDbContext _db;
    private readonly TestServices _services;
    private readonly int _aliceId;
    private readonly int _bobId;

    public OtpFlowTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var alice = new Cardholder("Alice", "A", "alice@test.com", "+911234567", "hash");
        var bob = new Cardholder("Bob", "B", "bob@test.com", "+911234568", "hash");
        _db.Cardholders.AddRange(alice, bob);
        _db.SaveChanges();
        _aliceId = alice.CardholderId;
        _bobId = bob.CardholderId;
        _services = new TestServices(_db, new FakeCurrentUser());
    }

    private StepUpRequest Unlock(int cardholderId, int cardId = 1) =>
        new(cardholderId, OtpPurpose.UnlockCard, $"card={cardId}", $"to unlock card {cardId}");

    /// <summary>A new authenticator = a new HTTP request (it is scoped per request in the API).</summary>
    private async Task<OtpChallengeDto> IssueAsync(StepUpRequest request)
    {
        _services.Otp.Current = null; // a request without a code
        return (await Assert.ThrowsAsync<OtpRequiredException>(() => _services.StepUp().RequireAsync(request))).Challenge;
    }

    private Task VerifyAsync(StepUpRequest request, int challengeId, string code)
    {
        _services.Otp.Current = new OtpProof(challengeId, code);
        return _services.StepUp().RequireAsync(request);
    }

    [Fact]
    public async Task Code_goes_by_sms_and_only_its_hash_is_stored()
    {
        var challenge = await IssueAsync(Unlock(_aliceId));

        Assert.Equal("+91***4567", challenge.SentTo);
        var (to, text) = Assert.Single(_services.Otp.Sms);
        Assert.Equal("+911234567", to);
        Assert.Contains("to unlock card 1", text);
        var code = _services.Otp.LastCode;
        Assert.Matches(@"^\d{6}$", code);

        var stored = await _db.OtpChallenges.SingleAsync();
        Assert.DoesNotContain(code, stored.CodeHash);
        Assert.Equal(OtpChallengeStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task Correct_code_works_once_retries_in_the_same_request_are_fine()
    {
        var request = Unlock(_aliceId);
        var challenge = await IssueAsync(request);
        _services.Otp.Current = new OtpProof(challenge.ChallengeId, _services.Otp.LastCode);

        var sameRequest = _services.StepUp();
        await sameRequest.RequireAsync(request);
        await sameRequest.RequireAsync(request);                     // e.g. a concurrency retry of the action

        var ex = await Assert.ThrowsAsync<OtpFailedException>(() => _services.StepUp().RequireAsync(request)); // replay
        Assert.Contains("already used", ex.Message);
    }

    [Fact]
    public async Task Code_for_one_action_or_person_cannot_approve_another()
    {
        var challenge = await IssueAsync(Unlock(_aliceId, cardId: 1));
        var code = _services.Otp.LastCode;

        await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(Unlock(_aliceId, cardId: 2), challenge.ChallengeId, code));
        await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(Unlock(_bobId, cardId: 1), challenge.ChallengeId, code));
        await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(
            new StepUpRequest(_aliceId, OtpPurpose.ChangePin, "card=1", "x"), challenge.ChallengeId, code));

        await VerifyAsync(Unlock(_aliceId, cardId: 1), challenge.ChallengeId, code);   // still valid for its own action
    }

    [Fact]
    public async Task Three_wrong_codes_burn_the_challenge()
    {
        var request = Unlock(_aliceId);
        var challenge = await IssueAsync(request);
        var code = _services.Otp.LastCode;
        var wrong = code == "000000" ? "111111" : "000000";

        Assert.Contains("2 attempt(s) left", (await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(request, challenge.ChallengeId, wrong))).Message);
        Assert.Contains("1 attempt(s) left", (await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(request, challenge.ChallengeId, wrong))).Message);
        Assert.Contains("Too many", (await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(request, challenge.ChallengeId, wrong))).Message);

        await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(request, challenge.ChallengeId, code));
        Assert.Equal(OtpChallengeStatus.Failed, (await _db.OtpChallenges.SingleAsync()).Status);
    }

    [Fact]
    public async Task Asking_again_replaces_the_previous_code()
    {
        var request = Unlock(_aliceId);
        var first = await IssueAsync(request);
        var firstCode = _services.Otp.LastCode;
        var second = await IssueAsync(request);                      // "Send a new code"

        await Assert.ThrowsAsync<OtpFailedException>(() => VerifyAsync(request, first.ChallengeId, firstCode));
        await VerifyAsync(request, second.ChallengeId, _services.Otp.LastCode);
    }

    [Fact]
    public async Task Too_many_codes_in_a_short_time_are_refused()
    {
        _services.OtpOptions.MaxCodesPerWindow = 2;
        await IssueAsync(Unlock(_aliceId, 1));
        await IssueAsync(Unlock(_aliceId, 2));

        await Assert.ThrowsAsync<TooManyRequestsException>(() => _services.StepUp().RequireAsync(Unlock(_aliceId, 3)));
        Assert.Equal(2, _services.Otp.Sms.Count);                    // no third SMS was sent
        await IssueAsync(Unlock(_bobId));                             // counted per cardholder
    }
}
