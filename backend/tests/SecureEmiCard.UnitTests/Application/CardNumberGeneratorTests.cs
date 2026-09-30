using SecureEmiCard.Application.Features.Cards;

namespace SecureEmiCard.UnitTests.Application;

public class CardNumberGeneratorTests
{
    private readonly CardNumberGenerator _generator = new();

    [Theory]
    [InlineData("4111111111111111", true)]  // well-known Visa test number
    [InlineData("5555555555554444", true)]  // well-known Mastercard test number
    [InlineData("4111111111111112", false)]
    [InlineData("41111111abc11111", false)]
    public void Luhn_validation_matches_known_numbers(string number, bool expected)
        => Assert.Equal(expected, CardNumberGenerator.IsValidLuhn(number));

    [Fact]
    public void Generated_numbers_are_16_digits_with_bin_and_valid_luhn()
    {
        for (int i = 0; i < 200; i++)
        {
            var number = _generator.GenerateCardNumber();
            Assert.Equal(16, number.Length);
            Assert.StartsWith(CardNumberGenerator.Bin, number);
            Assert.True(CardNumberGenerator.IsValidLuhn(number), number);
        }
    }

    [Fact]
    public void Cvv_is_three_digits_and_pin_is_strong()
    {
        for (int i = 0; i < 200; i++)
        {
            Assert.Matches(@"^\d{3}$", _generator.GenerateCvv());
            var pin = _generator.GeneratePin();
            Assert.True(PinPolicy.IsWellFormed(pin));
            Assert.False(PinPolicy.IsWeak(pin));
        }
    }

    [Fact]
    public void Mask_shows_only_last_four_digits()
        => Assert.Equal("XXXX-XXXX-XXXX-1111", CardNumberGenerator.Mask("4111111111111111"));

    [Theory]
    [InlineData("1111", true)]
    [InlineData("1234", true)]
    [InlineData("9876", true)]
    [InlineData("2580", false)]
    [InlineData("1212", false)]
    public void Pin_policy_detects_weak_pins(string pin, bool weak)
        => Assert.Equal(weak, PinPolicy.IsWeak(pin));
}
