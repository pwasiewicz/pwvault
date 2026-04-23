using System.Diagnostics;

namespace PwVault.Cli.Infrastructure;

public static class GitRunner
{
    public static bool IsGitRepo(string directory) =>
        Run(directory, ["rev-parse", "--is-inside-work-tree"]).ExitCode == 0;

    public static void Init(string directory) =>
        RunOrThrow(directory, ["init", "-b", "main"]);

    public static void AddAll(string directory) =>
        RunOrThrow(directory, ["add", "-A"]);

    public static void Commit(string directory, string message) =>
        RunOrThrow(directory, ["commit", "-m", message]);

    public static bool HasStagedChanges(string directory)
    {
        var result = Run(directory, ["diff", "--cached", "--quiet"]);
        return result.ExitCode != 0;
    }

    public static void Push(string directory) =>
        RunOrThrow(directory, ["push"]);

    public static bool HasUpstream(string directory) =>
        Run(directory, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"]).ExitCode == 0;

    public readonly record struct GitOutcome(int ExitCode, string Output, string Error)
    {
        public bool Succeeded => ExitCode == 0;
        public string CombinedText => string.Join(
            "\n",
            new[] { Output, Error }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public static GitOutcome TryPullRebaseAutostash(string directory)
    {
        var r = Run(directory, ["pull", "--rebase", "--autostash"]);
        return new GitOutcome(r.ExitCode, r.Output, r.Error);
    }

    public static GitOutcome TryPush(string directory)
    {
        var r = Run(directory, ["push"]);
        return new GitOutcome(r.ExitCode, r.Output, r.Error);
    }

    public static void AutoCommitIfRepo(string directory, string message, bool autoPush)
    {
        if (!IsGitRepo(directory)) return;
        AddAll(directory);
        if (!HasStagedChanges(directory)) return;
        Commit(directory, message);
        if (autoPush) Push(directory);
    }

    private readonly record struct ProcessResult(int ExitCode, string Output, string Error);

    private static ProcessResult Run(string directory, string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            return new ProcessResult(p.ExitCode, stdout, stderr);
        }
        catch
        {
            return new ProcessResult(-1, "", "git not available");
        }
    }

    private static void RunOrThrow(string directory, string[] args)
    {
        var result = Run(directory, args);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed (exit {result.ExitCode}): {result.Error}{result.Output}");
    }
}
