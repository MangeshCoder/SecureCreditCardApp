using System.Security.Cryptography;

namespace SecureEmiCard.Application.Features.Cards;

public interface ICardNumberGenerator
{
    string GenerateCardNumber();
    string GenerateCvv();
    string GeneratePin();
}

/// <summary>
/// Generates 16-digit card numbers that pass the Luhn (mod 10) check, plus random CVV and PIN,
/// using a cryptographically secure random number generator (never System.Random for secrets).
/// </summary>
public class CardNumberGenerator : ICardNumberGenerator
{
    // Demo Bank Identification Number (first 6 digits). A real issuer uses the BIN assigned by the card network.
    public const string Bin = "458123";

    public string GenerateCardNumber()
    {
        var digits = Bin + RandomDigits(16 - Bin.Length - 1);
        return digits + CalculateLuhnCheckDigit(digits);
    }

    public string GenerateCvv() => RandomDigits(3);

    public string GeneratePin()
    {
        string pin;
        do { pin = RandomDigits(PinPolicy.Length); } while (PinPolicy.IsWeak(pin));
        return pin;
    }

    public static int CalculateLuhnCheckDigit(string digitsWithoutCheck)
    {
        int sum = 0;
        // Walk from the right; the digit next to the (future) check digit is doubled.
        for (int i = digitsWithoutCheck.Length - 1, pos = 0; i >= 0; i--, pos++)
        {
            int d = digitsWithoutCheck[i] - '0';
            if (pos % 2 == 0)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }

    public static bool IsValidLuhn(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber) || cardNumber.Length < 2 || !cardNumber.All(char.IsAsciiDigit))
            return false;
        return CalculateLuhnCheckDigit(cardNumber[..^1]) == cardNumber[^1] - '0';
    }

    public static string Mask(string cardNumber) => $"XXXX-XXXX-XXXX-{cardNumber[^4..]}";

    private static string RandomDigits(int count) =>
        string.Create(count, 0, (span, _) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        });
}
