using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Operations;

namespace TradingPlatform.Infrastructure.Persistence;

public sealed class UserRepository : IUserRepository
{
    private readonly TradingDbContext _db;
    public UserRepository(TradingDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).ThenInclude(r => r.RolePermissions).ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).ThenInclude(r => r.RolePermissions).ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await _db.Users.AddAsync(user, cancellationToken);
}

public sealed class RoleRepository : IRoleRepository
{
    private readonly TradingDbContext _db;
    public RoleRepository(TradingDbContext db) => _db = db;

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        _db.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly TradingDbContext _db;
    public RefreshTokenRepository(TradingDbContext db) => _db = db;

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
        await _db.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _db.RefreshTokens.Include(t => t.User).ThenInclude(u => u.UserRoles).ThenInclude(r => r.Role).ThenInclude(r => r.RolePermissions).ThenInclude(p => p.Permission)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
}

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly TradingDbContext _db;
    public PasswordResetTokenRepository(TradingDbContext db) => _db = db;

    public async Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default) =>
        await _db.PasswordResetTokens.AddAsync(token, cancellationToken);

    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _db.PasswordResetTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
}

public sealed class AuditService : IAuditService
{
    private readonly TradingDbContext _db;
    public AuditService(TradingDbContext db) => _db = db;

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _db.AuditLogs.AddAsync(new AuditLog
        {
            UserId = entry.UserId,
            Action = entry.Action,
            Entity = entry.Entity,
            EntityId = entry.EntityId,
            IpAddress = entry.IpAddress,
            CorrelationId = entry.CorrelationId,
            MetadataJson = entry.MetadataJson
        }, cancellationToken);
    }
}
