using SecureEmiCard.Application.Features.Otp;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.UnitTests.Domain;

/// <summary>Module 7 rules: one-time code challenges, notifications, and which control changes are "risky".</summary>
public class OtpAndNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static OtpChallenge NewChallenge(int maxAttempts = 3) =>
        OtpChallenge.Issue(1, OtpPurpose.UnlockCard, "context-hash", "hash-of-123456", "+91******6072",
                           Now, TimeSpan.FromMinutes(5), maxAttempts);

    private static bool Is123456(string hash) => hash == "hash-of-123456";

    [Fact]
    public void Correct_code_is_accepted_exactly_once()
    {
        var challenge = NewChallenge();

        Assert.Equal(OtpCheckResult.Valid, challenge.Verify(Is123456, Now.AddMinutes(1)));
        Assert.Equal(OtpChallengeStatus.Used, challenge.Status);
        Assert.Equal(OtpCheckResult.NotPending, challenge.Verify(Is123456, Now.AddMinutes(1)));
    }

    [Fact]
    public void Wrong_codes_use_up_the_attempts_then_even_the_right_code_fails()
    {
        var challenge = NewChallenge(maxAttempts: 3);

        Assert.Equal(OtpCheckResult.Invalid, challenge.Verify(_ => false, Now));
        Assert.Equal(2, challenge.RemainingAttempts);
        Assert.Equal(OtpCheckResult.Invalid, challenge.Verify(_ => false, Now));
        Assert.Equal(OtpCheckResult.AttemptsExhausted, challenge.Verify(_ => false, Now));
        Assert.Equal(OtpChallengeStatus.Failed, challenge.Status);
        Assert.Equal(OtpCheckResult.NotPending, challenge.Verify(Is123456, Now));
    }

    [Fact]
    public void Expired_code_is_rejected_even_if_correct()
    {
        var challenge = NewChallenge();
        Assert.Equal(OtpCheckResult.Expired, challenge.Verify(Is123456, Now.AddMinutes(5)));
    }

    [Fact]
    public void Superseded_code_no_longer_works()
    {
        var challenge = NewChallenge();
        challenge.Supersede();
        Assert.Equal(OtpCheckResult.NotPending, challenge.Verify(Is123456, Now));
    }

    [Theory]
    [InlineData("+918669676072", "+91******6072")]
    [InlineData("+911234567", "+91***4567")]
    [InlineData("12345", "*2345")]
    public void Phone_numbers_are_masked(string phone, string masked) =>
        Assert.Equal(masked, StepUpAuthenticator.MaskPhone(phone));

    [Fact]
    public void Notification_is_cut_to_column_size_and_read_once()
    {
        var n = Notification.ForCardholder(1, NotificationCategory.Security, new string('t', 150), new string('m', 600));
        Assert.Equal(Notification.MaxTitleLength, n.Title.Length);
        Assert.Equal(Notification.MaxMessageLength, n.Message.Length);
        Assert.Equal(NotificationDeliveryStatus.Pending, n.DeliveryStatus);

        n.MarkRead(Now);
        n.MarkRead(Now.AddHours(1));
        Assert.Equal(Now, n.ReadAt);                               // first read time is kept
    }

    [Fact]
    public void Failed_delivery_is_retried_then_given_up()
    {
        var n = Notification.ForCardholder(1, NotificationCategory.Transaction, "t", "m");
        n.RecordDeliveryFailure(maxAttempts: 2);
        Assert.Equal(NotificationDeliveryStatus.Pending, n.DeliveryStatus);
        n.RecordDeliveryFailure(maxAttempts: 2);
        Assert.Equal(NotificationDeliveryStatus.Failed, n.DeliveryStatus);
    }

    // ---- which card-control changes need a code ------------------------------------------------

    private static readonly ChannelSetting On = new(true, null);
    private static readonly ChannelSetting Off = new(false, null);

    private static CardControl ControlsWithPosLimit(decimal? posLimit)
    {
        var card = new CreditCard(1, "enc", "hash", "XXXX-XXXX-XXXX-1234", "cvv", "pin", 100_000m,
                                  DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));
        card.Controls!.Update(new ChannelSetting(true, posLimit), Off, Off, On, Off, 100_000m);
        return card.Controls;
    }

    [Theory]
    [InlineData(null, false, null, false)]    // switch POS off              → safer
    [InlineData(5000.0, true, 3000.0, false)] // lower the POS limit         → safer
    [InlineData(5000.0, true, 8000.0, true)]  // raise it                    → riskier
    [InlineData(5000.0, true, null, true)]    // remove the limit            → riskier
    public void Raising_or_removing_a_limit_is_risky_lowering_is_not(double? oldLimit, bool enabled, double? newLimit, bool risky)
    {
        var controls = ControlsWithPosLimit((decimal?)oldLimit);
        Assert.Equal(risky, controls.IsRiskIncrease(new ChannelSetting(enabled, (decimal?)newLimit), Off, Off, On, Off));
    }

    [Fact]
    public void Switching_a_channel_on_is_risky_but_a_limit_on_a_switched_off_channel_is_not()
    {
        var controls = ControlsWithPosLimit(null);
        Assert.True(controls.IsRiskIncrease(On, On, Off, On, Off));                          // online on
        Assert.False(controls.IsRiskIncrease(On, new ChannelSetting(false, 9_000m), Off, On, Off));
        Assert.False(controls.IsRiskIncrease(On, Off, Off, Off, Off));                       // ATM off
    }
}
