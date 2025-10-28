using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Services;

namespace ProjectIndexerMcp.Repositories;

/// <summary>
/// Repository implementation for managing repositories in the database.
/// </summary>
public sealed class RepositoryRepository : IRepositoryRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<RepositoryRepository> _logger;

    public RepositoryRepository(DatabaseService db, ILogger<RepositoryRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Repository?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, name, remote_url AS RemoteUrl, default_branch AS DefaultBranch,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM repos
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<Repository>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<Repository?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, name, remote_url AS RemoteUrl, default_branch AS DefaultBranch,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM repos
            WHERE name = @Name";

        return await _db.QueryFirstOrDefaultAsync<Repository>(sql, new { Name = name }, cancellationToken);
    }

    public async Task<IEnumerable<Repository>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, name, remote_url AS RemoteUrl, default_branch AS DefaultBranch,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM repos
            ORDER BY name";

        return await _db.QueryAsync<Repository>(sql, cancellationToken: cancellationToken);
    }

    public async Task<Repository> CreateAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO repos (id, name, remote_url, default_branch, created_at, updated_at)
            VALUES (@Id, @Name, @RemoteUrl, @DefaultBranch, @CreatedAt, @UpdatedAt)
            RETURNING id, name, remote_url AS RemoteUrl, default_branch AS DefaultBranch,
                      created_at AS CreatedAt, updated_at AS UpdatedAt";

        var result = await _db.QuerySingleAsync<Repository>(sql, new
        {
            repository.Id,
            repository.Name,
            repository.RemoteUrl,
            repository.DefaultBranch,
            repository.CreatedAt,
            repository.UpdatedAt
        }, cancellationToken);

        _logger.LogInformation("Created repository {Name} with ID {Id}", repository.Name, repository.Id);
        return result;
    }

    public async Task<Repository> UpdateAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE repos
            SET name = @Name,
                remote_url = @RemoteUrl,
                default_branch = @DefaultBranch,
                updated_at = @UpdatedAt
            WHERE id = @Id
            RETURNING id, name, remote_url AS RemoteUrl, default_branch AS DefaultBranch,
                      created_at AS CreatedAt, updated_at AS UpdatedAt";

        var result = await _db.QuerySingleAsync<Repository>(sql, new
        {
            repository.Id,
            repository.Name,
            repository.RemoteUrl,
            repository.DefaultBranch,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        _logger.LogInformation("Updated repository {Name} with ID {Id}", repository.Name, repository.Id);
        return result;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM repos WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deleted repository with ID {Id}", id);
        }

        return rowsAffected > 0;
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM repos WHERE name = @Name)";
        return await _db.ExecuteScalarAsync<bool>(sql, new { Name = name }, cancellationToken);
    }
}

