using PwVault.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class SyncCommand : Command<SyncCommand.Settings>
{
    private readonly PwVaultConfig _config;

    public SyncCommand(PwVaultConfig config)
    {
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);

        if (!GitRunner.IsGitRepo(vaultPath))
        {
            AnsiConsole.MarkupLine("[red]Vault is not a git repository.[/] Nothing to sync.");
            return 1;
        }

        if (!GitRunner.HasUpstream(vaultPath))
        {
            AnsiConsole.MarkupLine(
                "[red]No upstream configured for the current branch.[/] Set one first, e.g. [bold]git push -u origin main[/] inside the vault.");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold]git pull --rebase --autostash[/]");
        var pull = GitRunner.TryPullRebaseAutostash(vaultPath);
        if (!pull.Succeeded)
        {
            AnsiConsole.MarkupLine("[red]Pull failed.[/]");
            WriteGitOutput(pull);
            AnsiConsole.MarkupLine(
                "[yellow]If the rebase stopped mid-way, resolve conflicts in the vault then run[/] [bold]git rebase --continue[/] [yellow](or[/] [bold]git rebase --abort[/][yellow]).[/]");
            return 2;
        }
        WriteGitOutput(pull);

        AnsiConsole.MarkupLine("[bold]git push[/]");
        var push = GitRunner.TryPush(vaultPath);
        if (!push.Succeeded)
        {
            AnsiConsole.MarkupLine("[red]Push failed.[/]");
            WriteGitOutput(push);
            return 3;
        }
        WriteGitOutput(push);

        AnsiConsole.MarkupLine("[green]Vault synced.[/]");
        return 0;
    }

    private static void WriteGitOutput(GitRunner.GitOutcome outcome)
    {
        var text = outcome.CombinedText;
        if (!string.IsNullOrWhiteSpace(text))
            AnsiConsole.WriteLine(text.TrimEnd());
    }
}
