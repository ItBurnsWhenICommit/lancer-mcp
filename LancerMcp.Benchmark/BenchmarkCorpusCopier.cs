namespace LancerMcp.Benchmark;

internal static class BenchmarkCorpusCopier
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git"
    };

    private static readonly HashSet<string> ExcludedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".user",
        ".suo"
    };

    internal static void CopyFiltered(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Benchmark source directory not found: {source}");
        }

        Directory.CreateDirectory(destination);

        var excludedInSource = FindExcludedDirectories(source);
        if (excludedInSource.Count > 0)
        {
            Console.WriteLine(
                $"Warning: Found {excludedInSource.Count} excluded directories in benchmark corpus; they will be skipped for determinism.");
        }

        CopyDirectoryFiltered(source, destination);

        var excludedInDestination = FindExcludedDirectories(destination);
        if (excludedInDestination.Count > 0)
        {
            throw new InvalidOperationException(
                $"Filtered copy still produced excluded directories: {string.Join(", ", excludedInDestination)}");
        }
    }

    private static List<string> FindExcludedDirectories(string root)
    {
        return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Where(path => ExcludedDirectoryNames.Contains(Path.GetFileName(path)))
            .ToList();
    }

    private static void CopyDirectoryFiltered(string source, string destination)
    {
        var stack = new Stack<(string Source, string Destination)>();
        stack.Push((source, destination));

        while (stack.Count > 0)
        {
            var (currentSource, currentDestination) = stack.Pop();
            var directoryName = Path.GetFileName(currentSource);
            if (!string.IsNullOrEmpty(directoryName) && ExcludedDirectoryNames.Contains(directoryName))
            {
                continue;
            }

            Directory.CreateDirectory(currentDestination);

            foreach (var file in Directory.EnumerateFiles(currentSource))
            {
                var extension = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(extension) && ExcludedFileExtensions.Contains(extension))
                {
                    continue;
                }

                var targetFile = Path.Combine(currentDestination, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite: true);
            }

            foreach (var directory in Directory.EnumerateDirectories(currentSource))
            {
                var name = Path.GetFileName(directory);
                if (ExcludedDirectoryNames.Contains(name))
                {
                    continue;
                }

                var targetDir = Path.Combine(currentDestination, name);
                stack.Push((directory, targetDir));
            }
        }
    }
}
