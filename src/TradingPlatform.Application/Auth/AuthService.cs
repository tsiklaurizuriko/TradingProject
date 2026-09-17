using TradingPlatform.Application.Abstractions;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Identity;

namespace TradingPlatform.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordResetTokenRepository _passwordResets;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenHasher _tokenHasher;
    private readonly IJwtTokenService _jwt;
    private readonly ITotpService _totp;
    private readonly ISecretProtector _secrets;
    private readonly IUnitOfWork _uow;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IPasswordResetTokenRepository passwordResets,
        IPasswordHasher passwordHasher,
        ITokenHasher tokenHasher,
        IJwtTokenService jwt,
        ITotpService totp,
        ISecretProtector secrets,
        IUnitOfWork uow,
        IAuditService audit,
        IClock clock,
        ICorrelationIdAccessor correlation,
        JwtOptions jwtOptions)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _passwordResets = passwordResets;
        _passwordHasher = passwordHasher;
        _tokenHasher = tokenHasher;
        _jwt = jwt;
        _totp = totp;
        _secrets = secrets;
        _uow = uow;
        _audit = audit;
        _clock = clock;
        _correlation = correlation;
        _jwtOptions = jwtOptions;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip, CancellationToken cancellationToken = default)
    {
        var existing = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A user with that email already exists.");
        }

        var trader = await _roles.GetByNameAsync(RoleNames.Trader, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InternalError, "Trader role is not seeded.");

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = request.Email.Trim().ToUpperInvariant(),
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            EmailConfirmed = true
        };
        user.UserRoles.Add(new UserRole { User = user, Role = trader });
        await _users.AddAsync(user, cancellationToken);
        var response = await IssueAsync(user, [RoleNames.Trader], [], ip, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry("USER_REGISTERED", "User", user.Id.ToString(), user.Id, ip, _correlation.GetOrCreate(), null), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash) || !user.IsActive)
        {
            throw new DomainException(ErrorCodes.Unauthorized, "Invalid credentials.");
        }

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TotpCode) || user.TwoFactorSecretEncrypted is null)
            {
                throw new DomainException("TWO_FACTOR_REQUIRED", "A TOTP code is required.");
            }

            var secret = _secrets.Unprotect(user.TwoFactorSecretEncrypted);
            if (!_totp.Verify(secret, request.TotpCode))
            {
                throw new DomainException(ErrorCodes.Unauthorized, "Invalid two-factor code.");
            }
        }

        var roles = user.UserRoles.Select(x => x.Role.Name).Distinct().ToArray();
        var permissions = user.UserRoles
            .SelectMany(x => x.Role.RolePermissions)
            .Select(x => x.Permission.Code)
            .Distinct()
            .ToArray();

        user.LastLoginAt = _clock.UtcNow;
        user.LastLoginIp = ip;
        var response = await IssueAsync(user, roles, permissions, ip, cancellationToken);
        await _audit.WriteAsync(new AuditEntry("USER_LOGGED_IN", "User", user.Id.ToString(), user.Id, ip, _correlation.GetOrCreate(), null), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ip, CancellationToken cancellationToken = default)
    {
        var hash = _tokenHasher.Hash(request.RefreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, cancellationToken);
        if (stored is null || !stored.IsActive)
        {
            throw new DomainException(ErrorCodes.Unauthorized, "Refresh token is invalid.");
        }

        stored.RevokedAt = _clock.UtcNow;
        var user = stored.User;
        var roles = user.UserRoles.Select(x => x.Role.Name).Distinct().ToArray();
        var permissions = user.UserRoles.SelectMany(x => x.Role.RolePermissions).Select(x => x.Permission.Code).Distinct().ToArray();
        var response = await IssueAsync(user, roles, permissions, ip, cancellationToken);
        stored.ReplacedByTokenHash = _tokenHasher.Hash(response.RefreshToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        var hash = _tokenHasher.Hash(request.RefreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, cancellationToken);
        if (stored is not null && stored.IsActive)
        {
            stored.RevokedAt = _clock.UtcNow;
            await _audit.WriteAsync(new AuditEntry("USER_LOGGED_OUT", "User", stored.UserId.ToString(), stored.UserId, stored.CreatedByIp, _correlation.GetOrCreate(), null), cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            return;
        }

        var raw = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        await _passwordResets.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = _tokenHasher.Hash(raw),
            ExpiresAt = _clock.UtcNow.AddHours(1)
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        // Email delivery is registered through INotificationService in a later phase.
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var stored = await _passwordResets.GetByHashAsync(_tokenHasher.Hash(request.Token), cancellationToken);
        if (stored is null || stored.UsedAt is not null || stored.ExpiresAt <= _clock.UtcNow)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Password reset token is invalid.");
        }

        stored.UsedAt = _clock.UtcNow;
        stored.User.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "New password must be at least 8 characters.");
        }

        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash) || !user.IsActive)
        {
            throw new DomainException(ErrorCodes.Unauthorized, "Current password is wrong.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _audit.WriteAsync(new AuditEntry("PASSWORD_CHANGED", "User", user.Id.ToString(), user.Id, null, _correlation.GetOrCreate(), null), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponse> IssueAsync(
        User user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        string? ip,
        CancellationToken cancellationToken)
    {
        var access = _jwt.CreateAccessToken(user, roles, permissions);
        var refresh = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await _refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            User = user,
            TokenHash = _tokenHasher.Hash(refresh),
            ExpiresAt = _clock.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedByIp = ip
        }, cancellationToken);

        return new AuthResponse(
            access,
            refresh,
            _clock.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
            user.Email,
            roles.ToArray(),
            user.TwoFactorEnabled);
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "TradingPlatform";
    public string Audience { get; set; } = "TradingPlatform";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
