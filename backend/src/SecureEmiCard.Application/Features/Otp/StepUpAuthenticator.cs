using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Messaging;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Otp;

/// <summary>
/// One protected action. <paramref name="Context"/> identifies exactly WHAT is approved (e.g. "card=5", or
/// "card=5|amount=2499.00|merchant=Amazon India") - the code only works for that. It must not contain
/// secrets (PIN, CVV): only its hash is stored, and a hash of a 4-digit PIN could be reversed.
/// <paramref name="Description"/> is shown to the user and put in the SMS, e.g. "to unlock card ending 4057".
/// </summary>
public sealed record StepUpRequest(int CardholderId, OtpPurpose Purpose, string Context, string Description);

public interface IStepUpAuthenticator
{
    /// <summary>
    /// "Step-up authentication" for a risky action:
    /// - no code in the request  → sends a new one-time code by SMS and throws <see cref="OtpRequiredException"/> (HTTP 428);
    /// - a code in the request   → verifies it, uses it up and returns, or throws <see cref="OtpFailedException"/> (403).
    /// Call it AFTER the cheap checks (so no SMS is sent for an action that would fail anyway) and BEFORE
    /// changing anything (the challenge is saved immediately, so the request must have no half-done changes).
    /// </summary>
    Task RequireAsync(StepUpRequest request, CancellationToken ct = default);
}

public class StepUpAuthenticator : IStepUpAuthenticator
{
    private readonly IOtpChallengeRepository _challenges;
    private readonly ICardholderRepository _cardholders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISecretHasher _hasher;
    private readonly IMessageSender _sender;
    private readonly IOtpProofAccessor _proof;
    private readonly OtpOptions _options;

    // Challenges already verified in THIS request (the service is scoped per request). A concurrency retry
    // runs the same operation again with the same code - it must not fail with "code already used".
    private readonly HashSet<(int ChallengeId, string ContextHash)> _verified = new();

    public StepUpAuthenticator(IOtpChallengeRepository challenges, ICardholderRepository cardholders, IUnitOfWork unitOfWork,
                               ISecretHasher hasher, IMessageSender sender, IOtpProofAccessor proof, IOptions<OtpOptions> options)
    {
        _challenges = challenges;
        _cardholders = cardholders;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _sender = sender;
        _proof = proof;
        _options = options.Value;
    }

    public async Task RequireAsync(StepUpRequest request, CancellationToken ct = default)
    {
        var contextHash = HashContext(request);
        var proof = _proof.Current;

        if (proof is null)
        {
            await IssueAsync(request, contextHash, ct); // always throws OtpRequiredException
            return;
        }
        if (_verified.Contains((proof.ChallengeId, contextHash))) return;

        var challenge = await _challenges.GetByIdAsync(proof.ChallengeId, ct);
        if (challenge is null || challenge.CardholderId != request.CardholderId || challenge.ContextHash != contextHash)
            throw new OtpFailedException("This code is not valid for this action. Request a new code.");

        var result = challenge.Verify(hash => _hasher.Verify(proof.Code, hash), DateTime.UtcNow);

        // Saved right away - like a wrong PIN, a wrong code must count even if we throw next, and a correct
        // code must be used up even if the action itself fails afterwards. Status and Attempts are
        // concurrency tokens: two requests racing with the same code cannot both succeed.
        await _unitOfWork.SaveChangesAsync(ct);

        switch (result)
        {
            case OtpCheckResult.Valid:
                _verified.Add((proof.ChallengeId, contextHash));
                return;
            case OtpCheckResult.Invalid:
                throw new OtpFailedException($"Incorrect code. {challenge.RemainingAttempts} attempt(s) left.");
            case OtpCheckResult.AttemptsExhausted:
                throw new OtpFailedException("Too many incorrect codes. Request a new code.");
            case OtpCheckResult.Expired:
                throw new OtpFailedException("This code has expired. Request a new code.");
            default:
                throw new OtpFailedException("This code was already used or replaced. Request a new code.");
        }
    }

    private async Task IssueAsync(StepUpRequest request, string contextHash, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sentRecently = await _challenges.CountIssuedSinceAsync(request.CardholderId, now.AddMinutes(-_options.WindowMinutes), ct);
        if (sentRecently >= _options.MaxCodesPerWindow)
            throw new TooManyRequestsException("Too many codes were requested. Please wait a few minutes and try again.");

        var cardholder = await _cardholders.GetByIdAsync(request.CardholderId, ct)
                         ?? throw new NotFoundException("Cardholder was not found.");

        // "Send a new code": the previous code for the same action stops working.
        foreach (var previous in await _challenges.GetPendingAsync(request.CardholderId, contextHash, ct))
            previous.Supersede();

        // 6 random digits from a cryptographic generator (never Random: its output can be predicted).
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = OtpChallenge.Issue(request.CardholderId, request.Purpose, contextHash, _hasher.Hash(code),
            MaskPhone(cardholder.PhoneNumber), now, TimeSpan.FromMinutes(_options.ExpiryMinutes), _options.MaxAttempts);
        await _challenges.AddAsync(challenge, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // The code exists in clear text only here and in the SMS - never in the database, logs or the response.
        await _sender.SendSmsAsync(cardholder.PhoneNumber,
            $"{code} is your Secure Credit EMI code {request.Description}. Valid for {_options.ExpiryMinutes} minutes. " +
            "Never share it - the bank will never ask for it.", ct);

        throw new OtpRequiredException(new OtpChallengeDto(challenge.OtpChallengeId, request.Purpose.ToString(),
            request.Description, challenge.SentTo, challenge.ExpiresAt, OtpOptions.CodeLength));
    }

    private static string HashContext(StepUpRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Purpose}|{request.Context}")));

    /// <summary>
    /// +918669676072 → +91******6072: enough for the user to recognise their number. The country code is only
    /// shown when at least 3 digits stay hidden; the last 4 digits are always shown, everything else masked.
    /// </summary>
    public static string MaskPhone(string phone)
    {
        phone = phone.Trim();
        if (phone.Length <= 4) return new string('*', phone.Length);
        var keepStart = Math.Max(0, Math.Min(3, phone.Length - 4 - 3));
        return phone[..keepStart] + new string('*', phone.Length - keepStart - 4) + phone[^4..];
    }
}
