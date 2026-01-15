using LancerMcp.Benchmark;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkCorpusCopierTests
{
    [Fact]
    public void CopyFiltered_ExcludesBuildArtifacts()
    {
        var source = Path.Combine(Path.GetTempPath(), $"lancer-bench-src-{Guid.NewGuid():N}");
        var destination = Path.Combine(Path.GetTempPath(), $"lancer-bench-dest-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(source, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(source, "obj"));
        File.WriteAllText(Path.Combine(source, "bin", "Debug", "ignored.txt"), "ignore");
        File.WriteAllText(Path.Combine(source, "keep.cs"), "class C {}");

        try
        {
            BenchmarkCorpusCopier.CopyFiltered(source, destination);

            Assert.False(Directory.Exists(Path.Combine(destination, "bin")));
            Assert.False(Directory.Exists(Path.Combine(destination, "obj")));
            Assert.True(File.Exists(Path.Combine(destination, "keep.cs")));
        }
        finally
        {
            if (Directory.Exists(source))
            {
                Directory.Delete(source, recursive: true);
            }

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
        }
    }
}
