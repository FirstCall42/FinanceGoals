using System.Diagnostics;

namespace FinanceGoals.Migrator.Tests;

/// <summary>Runs a built FinanceGoals.Migrator.dll as its own process, the way a deployment does.</summary>
internal static class MigratorProcess
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    // Settings the child would otherwise inherit from the test run's environment.
    private static readonly string[] _settings =
    [
        "ConnectionStrings__SchemaOwner",
        "Migrator__AppUser",
        "Migrator__AppUserKind",
        "Migrator__AppUserPassword",
        "Migrator__AppUserClientId",
    ];

    /// <summary>This branch's migrator, built next to the tests by the project reference.</summary>
    public static string Current => typeof(DatabaseMigrator).Assembly.Location;

    public static async Task<Result> RunAsync(string migratorDll, IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("dotnet", [migratorDll])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var setting in _settings)
        {
            start.Environment.Remove(setting);
        }

        foreach (var (key, value) in environment)
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {migratorDll}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(_timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{migratorDll} did not finish within {_timeout}:\n{await output}\n{await error}");
        }

        return new Result(process.ExitCode, await output, await error);
    }

    public sealed record Result(int ExitCode, string Output, string Error)
    {
        public override string ToString() => $"exit {ExitCode}\n{Output}\n{Error}";
    }
}
