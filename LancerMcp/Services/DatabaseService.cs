using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;
using LancerMcp.Configuration;

namespace LancerMcp.Services;

/// <summary>
/// Service for managing PostgreSQL database connections and executing queries.
/// Provides connection pooling, transaction support, and helper methods for common operations.
/// </summary>
public sealed class DatabaseService : IDisposable
{
    private readonly ILogger<DatabaseService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly NpgsqlDataSource _dataSource;
    private bool _disposed;

    public DatabaseService(
        ILogger<DatabaseService> logger,
        IOptionsMonitor<ServerOptions> options)
    {
        _logger = logger;
        _options = options;

        // Configure Dapper to handle PostgreSQL enums and pgvector types
        ConfigureDapper();

        // Create a data source for connection pooling
        var connectionString = _options.CurrentValue.GetDatabaseConnectionString();
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

        // Register pgvector types
        dataSourceBuilder.UseVector();

        _dataSource = dataSourceBuilder.Build();

        _logger.LogInformation("Database service initialized with connection to {Database}",
            _options.CurrentValue.DatabaseName);
    }

    /// <summary>
    /// Gets a new database connection from the pool.
    /// The caller is responsible for disposing the connection.
    /// </summary>
    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    public async Task<IEnumerable<T>> QueryAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
        return await connection.QueryAsync<T>(command);
    }

    /// <summary>
    /// Executes a query and returns the first result or default.
    /// </summary>
    public async Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<T>(command);
    }

    /// <summary>
    /// Executes a query and returns a single result.
    /// </summary>
    public async Task<T> QuerySingleAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<T>(command);
    }

    /// <summary>
    /// Executes a command and returns the number of affected rows.
    /// </summary>
    public async Task<int> ExecuteAsync(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    /// <summary>
    /// Executes a command and returns a scalar value.
    /// </summary>
    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<T>(command);
    }

    /// <summary>
    /// Executes multiple commands in a transaction.
    /// If any command fails, all changes are rolled back.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await action(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed, rolling back");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Executes multiple commands in a transaction without returning a value.
    /// If any command fails, all changes are rolled back.
    /// </summary>
    public async Task ExecuteInTransactionAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, Task> action,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await action(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed, rolling back");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Tests the database connection.
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await GetConnectionAsync(cancellationToken);
            var result = await connection.ExecuteScalarAsync<int>("SELECT 1");
            _logger.LogInformation("Database connection test successful");
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Configures Dapper to handle PostgreSQL-specific types.
    /// </summary>
    private static void ConfigureDapper()
    {
        // Configure Dapper to map PostgreSQL enums to C# enums
        // This is done automatically by Npgsql for most cases

        // Add custom type handlers if needed
        SqlMapper.AddTypeHandler(new VectorTypeHandler());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _dataSource.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Custom Dapper type handler for pgvector Vector type.
/// </summary>
public class VectorTypeHandler : SqlMapper.TypeHandler<Vector>
{
    public override void SetValue(IDbDataParameter parameter, Vector? value)
    {
        if (parameter is NpgsqlParameter npgsqlParameter)
        {
            npgsqlParameter.Value = value ?? (object)DBNull.Value;
            npgsqlParameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Unknown;
        }
    }

    public override Vector Parse(object value)
    {
        if (value is Vector vector)
        {
            return vector;
        }

        if (value is string str)
        {
            // Parse string representation of vector
            return new Vector(str);
        }

        throw new InvalidCastException($"Cannot convert {value.GetType()} to Vector");
    }
}

