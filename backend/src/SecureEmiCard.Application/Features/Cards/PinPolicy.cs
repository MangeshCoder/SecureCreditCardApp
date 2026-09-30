namespace SecureEmiCard.Application.Features.Cards;

/// <summary>Rules for a 4-digit card PIN.</summary>
public static class PinPolicy
{
    public const int Length = 4;

    public static bool IsWellFormed(string? pin) =>
        pin is { Length: Length } && pin.All(char.IsAsciiDigit);

    /// <summary>Rejects easily guessed PINs: all same digit (1111) or straight sequences (1234, 4321).</summary>
    public static bool IsWeak(string pin)
    {
        if (pin.Distinct().Count() == 1) return true;

        bool ascending = true, descending = true;
        for (int i = 1; i < pin.Length; i++)
        {
            if (pin[i] - pin[i - 1] != 1) ascending = false;
            if (pin[i - 1] - pin[i] != 1) descending = false;
        }
        return ascending || descending;
    }
}
