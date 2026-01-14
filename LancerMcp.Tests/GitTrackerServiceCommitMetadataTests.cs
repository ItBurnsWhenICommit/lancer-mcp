using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using LancerMcp.Configuration;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;

namespace LancerMcp.Tests;

public sealed class GitTrackerServiceCommitMetadataTests
{
    [Fact]
    public async Task GetCommitMetadataAsync_NormalizesCommittedAtToUtc()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"git-tracker-commit-metadata-{Guid.NewGuid()}");
        var remotePath = Path.Combine(tempRoot, "remote");
        var workingDir = Path.Combine(tempRoot, "workdir");

        Directory.CreateDirectory(remotePath);
        Directory.CreateDirectory(workingDir);

        try
        {
            Repository.Init(remotePath);

            using var repo = new Repository(remotePath);
            var filePath = Path.Combine(remotePath, "README.md");
            File.WriteAllText(filePath, "fixture");
            Commands.Stage(repo, "README.md");

            var commitTime = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            var signature = new Signature("Test Author", "author@example.com", commitTime);
            var commit = repo.Commit("Initial commit", signature, signature);

            var branchName = repo.Head.FriendlyName;

            var serverOptions = new ServerOptions
            {
                WorkingDirectory = workingDir,
                Repositories =
                [
                    new ServerOptions.RepositoryDescriptor
                    {
                        Name = "local-repo",
                        RemoteUrl = remotePath,
                        DefaultBranch = branchName
                    }
                ]
            };

            var gitTracker = new GitTrackerService(
                NullLogger<GitTrackerService>.Instance,
                new TestOptionsMonitor(serverOptions),
                new MockRepositoryRepository(),
                new MockBranchRepository(),
                TestHelpers.CreateMockWorkspaceLoader());

            await gitTracker.InitializeAsync(CancellationToken.None);

            var metadata = await gitTracker.GetCommitMetadataAsync("local-repo", commit.Sha, CancellationToken.None);

            Assert.NotNull(metadata);
            Assert.Equal(TimeSpan.Zero, metadata!.CommittedAt.Offset);
            Assert.Equal(commitTime.ToUniversalTime().ToUnixTimeSeconds(), metadata.CommittedAt.ToUnixTimeSeconds());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
