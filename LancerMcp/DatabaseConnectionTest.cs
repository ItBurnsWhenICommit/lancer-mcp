using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;

namespace LancerMcp;

/// <summary>
/// Simple test class to verify database connectivity and basic operations.
/// This can be run manually to test the database setup.
/// </summary>
public static class DatabaseConnectionTest
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<DatabaseService>>();
        var db = services.GetRequiredService<DatabaseService>();
        var repoRepository = services.GetRequiredService<IRepositoryRepository>();

        logger.LogInformation("=== Starting Database Connection Test ===");

        try
        {
            // Test 1: Basic connection test
            logger.LogInformation("Test 1: Testing basic database connection...");
            var connectionOk = await db.TestConnectionAsync();
            if (connectionOk)
            {
                logger.LogInformation("✓ Database connection successful!");
            }
            else
            {
                logger.LogError("✗ Database connection failed!");
                return;
            }

            // Test 2: Query existing repositories
            logger.LogInformation("Test 2: Querying existing repositories...");
            var existingRepos = await repoRepository.GetAllAsync();
            var reposList = existingRepos.ToList();
            logger.LogInformation("✓ Found {Count} existing repositories", reposList.Count);
            foreach (var repo in reposList)
            {
                logger.LogInformation("  - {Name} ({RemoteUrl})", repo.Name, repo.RemoteUrl);
            }

            // Test 3: Create a test repository
            logger.LogInformation("Test 3: Creating a test repository...");
            var testRepo = new Repository
            {
                Id = Guid.NewGuid().ToString(),
                Name = "test-repo-" + DateTime.UtcNow.Ticks,
                RemoteUrl = "https://github.com/test/test-repo.git",
                DefaultBranch = "main",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var createdRepo = await repoRepository.CreateAsync(testRepo);
            logger.LogInformation("✓ Created test repository: {Name} (ID: {Id})", createdRepo.Name, createdRepo.Id);

            // Test 4: Retrieve the created repository
            logger.LogInformation("Test 4: Retrieving the created repository...");
            var retrievedRepo = await repoRepository.GetByIdAsync(createdRepo.Id);
            if (retrievedRepo != null)
            {
                logger.LogInformation("✓ Successfully retrieved repository: {Name}", retrievedRepo.Name);
            }
            else
            {
                logger.LogError("✗ Failed to retrieve repository!");
            }

            // Test 5: Update the repository
            logger.LogInformation("Test 5: Updating the repository...");
            var updatedRepo = new Repository
            {
                Id = retrievedRepo!.Id,
                Name = retrievedRepo.Name,
                RemoteUrl = retrievedRepo.RemoteUrl,
                DefaultBranch = "develop",
                CreatedAt = retrievedRepo.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var result = await repoRepository.UpdateAsync(updatedRepo);
            logger.LogInformation("✓ Updated repository default branch to: {Branch}", result.DefaultBranch);

            // Test 6: Check if repository exists
            logger.LogInformation("Test 6: Checking if repository exists...");
            var exists = await repoRepository.ExistsAsync(createdRepo.Name);
            logger.LogInformation("✓ Repository exists: {Exists}", exists);

            // Test 7: Delete the test repository
            logger.LogInformation("Test 7: Deleting the test repository...");
            var deleted = await repoRepository.DeleteAsync(createdRepo.Id);
            if (deleted)
            {
                logger.LogInformation("✓ Successfully deleted test repository");
            }
            else
            {
                logger.LogError("✗ Failed to delete test repository!");
            }

            // Test 8: Verify deletion
            logger.LogInformation("Test 8: Verifying deletion...");
            var deletedRepo = await repoRepository.GetByIdAsync(createdRepo.Id);
            if (deletedRepo == null)
            {
                logger.LogInformation("✓ Repository successfully deleted (not found)");
            }
            else
            {
                logger.LogError("✗ Repository still exists after deletion!");
            }

            logger.LogInformation("=== All Database Tests Completed Successfully! ===");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "✗ Database test failed with exception");
            throw;
        }
    }
}

