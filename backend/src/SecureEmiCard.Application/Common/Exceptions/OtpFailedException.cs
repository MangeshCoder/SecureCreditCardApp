namespace SecureEmiCard.Application.Common.Exceptions;

/// <summary>
/// Mapped to HTTP 403: wrong, expired or already used code, or a code for a different action.
/// (Not 401: the user IS signed in - only this extra step failed.)
/// </summary>
public class OtpFailedException : Exception
{
    public OtpFailedException(string message) : base(message) { }
}
