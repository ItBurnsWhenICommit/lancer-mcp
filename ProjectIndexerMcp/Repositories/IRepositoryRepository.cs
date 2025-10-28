using ProjectIndexerMcp.Models;

namespace ProjectIndexerMcp.Repositories;

/// <summary>
/// Repository interface for managing repositories in the database.
/// </summary>
public interface IRepositoryRepository
{
    /// <summary>
    /// Gets a repository by ID.
    /// </summary>
    Task<Repository?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a repository by name.
    /// </summary>
    Task<Repository?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all repositories.
    /// </summary>
    Task<IEnumerable<Repository>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new repository.
    /// </summary>
    Task<Repository> CreateAsync(Repository repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing repository.
    /// </summary>
    Task<Repository> UpdateAsync(Repository repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a repository by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a repository exists by name.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for managing branches in the database.
/// </summary>
public interface IBranchRepository
{
    /// <summary>
    /// Gets a branch by ID.
    /// </summary>
    Task<Branch?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a branch by repository ID and branch name.
    /// </summary>
    Task<Branch?> GetByRepoAndNameAsync(string repoId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all branches for a repository.
    /// </summary>
    Task<IEnumerable<Branch>> GetByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all branches with a specific index state.
    /// </summary>
    Task<IEnumerable<Branch>> GetByIndexStateAsync(IndexState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new branch.
    /// </summary>
    Task<Branch> CreateAsync(Branch branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing branch.
    /// </summary>
    Task<Branch> UpdateAsync(Branch branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the index state of a branch.
    /// </summary>
    Task UpdateIndexStateAsync(string id, IndexState state, string? indexedCommitSha = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a branch by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all branches for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for managing commits in the database.
/// </summary>
public interface ICommitRepository
{
    /// <summary>
    /// Gets a commit by ID.
    /// </summary>
    Task<Commit?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a commit by repository ID and SHA.
    /// </summary>
    Task<Commit?> GetByShaAsync(string repoId, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all commits for a repository.
    /// </summary>
    Task<IEnumerable<Commit>> GetByRepoIdAsync(string repoId, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all commits for a branch.
    /// </summary>
    Task<IEnumerable<Commit>> GetByBranchAsync(string repoId, string branchName, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new commit.
    /// </summary>
    Task<Commit> CreateAsync(Commit commit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple commits in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<Commit> commits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a commit by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all commits for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for managing files in the database.
/// </summary>
public interface IFileRepository
{
    /// <summary>
    /// Gets a file by ID.
    /// </summary>
    Task<FileMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a file by repository, branch, commit, and file path.
    /// </summary>
    Task<FileMetadata?> GetByPathAsync(string repoId, string branchName, string commitSha, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all files for a commit.
    /// </summary>
    Task<IEnumerable<FileMetadata>> GetByCommitAsync(string repoId, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all files for a branch.
    /// </summary>
    Task<IEnumerable<FileMetadata>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all files by language.
    /// </summary>
    Task<IEnumerable<FileMetadata>> GetByLanguageAsync(string repoId, Language language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new file.
    /// </summary>
    Task<FileMetadata> CreateAsync(FileMetadata file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple files in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<FileMetadata> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all files for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all files for a branch.
    /// </summary>
    Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for managing symbols in the database.
/// </summary>
public interface ISymbolRepository
{
    /// <summary>
    /// Gets a symbol by ID.
    /// </summary>
    Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all symbols for a file.
    /// </summary>
    Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all symbols for a branch.
    /// </summary>
    Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches symbols by name (exact or fuzzy).
    /// </summary>
    Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets symbols by kind.
    /// </summary>
    Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new symbol.
    /// </summary>
    Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple symbols in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a symbol by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all symbols for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all symbols for a branch.
    /// </summary>
    Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for managing symbol edges in the database.
/// </summary>
public interface IEdgeRepository
{
    /// <summary>
    /// Gets an edge by ID.
    /// </summary>
    Task<SymbolEdge?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all edges from a source symbol.
    /// </summary>
    Task<IEnumerable<SymbolEdge>> GetBySourceAsync(string sourceSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all edges to a target symbol.
    /// </summary>
    Task<IEnumerable<SymbolEdge>> GetByTargetAsync(string targetSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all edges for a branch.
    /// </summary>
    Task<IEnumerable<SymbolEdge>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new edge.
    /// </summary>
    Task<SymbolEdge> CreateAsync(SymbolEdge edge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple edges in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<SymbolEdge> edges, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an edge by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all edges for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all edges for a branch.
    /// </summary>
    Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);
}

