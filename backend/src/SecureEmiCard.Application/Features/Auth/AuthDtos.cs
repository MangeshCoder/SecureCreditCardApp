namespace SecureEmiCard.Application.Features.Auth;

public record RegisterRequest(string FirstName, string LastName, string Email, string PhoneNumber, string Password);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string Token, DateTime ExpiresAtUtc, UserInfo User);

public record UserInfo(int CardholderId, string FullName, string Email, string Role);
