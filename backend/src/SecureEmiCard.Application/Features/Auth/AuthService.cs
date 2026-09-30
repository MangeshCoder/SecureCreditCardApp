using FluentValidation;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    // Used to spend the same CPU time when the email does not exist,
    // so response timing does not reveal which emails are registered.
    private static string? _dummyHash;

    private readonly ICardholderRepository _cardholders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthService(ICardholderRepository cardholders, IUnitOfWork unitOfWork,
                       IPasswordHasher passwordHasher, IJwtTokenGenerator tokenGenerator,
                       IValidator<RegisterRequest> registerValidator, IValidator<LoginRequest> loginValidator)
    {
        _cardholders = cardholders;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        await _registerValidator.ValidateAndThrowAsync(request, ct);

        if (await _cardholders.EmailExistsAsync(request.Email, ct))
            throw new ConflictException("An account with this email already exists.");

        // Self-registration always creates a Cardholder; Admins are seeded or promoted by the bank.
        var cardholder = new Cardholder(request.FirstName, request.LastName, request.Email,
                                        request.PhoneNumber, _passwordHasher.Hash(request.Password));

        await _cardholders.AddAsync(cardholder, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return BuildResponse(cardholder);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var user = await _cardholders.GetByEmailAsync(request.Email, ct);
        if (user is null)
        {
            _passwordHasher.Verify(request.Password, _dummyHash ??= _passwordHasher.Hash("dummy-password"));
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Same generic message for every failure so attackers cannot enumerate accounts.
        if (!_passwordHasher.Verify(request.Password, user.PasswordHash) || !user.IsActive)
            throw new UnauthorizedException("Invalid email or password.");

        return BuildResponse(user);
    }

    private AuthResponse BuildResponse(Cardholder user)
    {
        var (token, expiresAt) = _tokenGenerator.GenerateToken(user);
        return new AuthResponse(token, expiresAt,
            new UserInfo(user.CardholderId, user.FullName, user.Email, user.Role.ToString()));
    }
}
