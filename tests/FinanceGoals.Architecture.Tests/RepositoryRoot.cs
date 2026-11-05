namespace FinanceGoals.Architecture.Tests;

/// <summary>Locates the repository root, the directory that holds FinanceGoals.slnx.</summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<DirectoryInfo> _root = new(Find);

    public static DirectoryInfo Directory => _root.Value;

    public static string SourceDirectory => Path.Combine(Directory.FullName, "src");

    private static DirectoryInfo Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FinanceGoals.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            $"FinanceGoals.slnx not found in {AppContext.BaseDirectory} or any parent directory.");
    }
}
