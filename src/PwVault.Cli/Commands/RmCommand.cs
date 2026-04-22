using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class RmCommand : Command<RmCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly PwVaultConfig _config;

    public RmCommand(IFileSystem fs, PwVaultConfig config)
    {
        _fs = fs;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Entry path to remove.")]
        public string Path { get; init; } = "";
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        var entryPath = new EntryPath(settings.Path);

        using var storage = VaultStorage.Open(vaultPath, _fs);
        var existing = storage.Get(entryPath);

        if (!settings.SkipConfirmations)
        {
            AnsiConsole.MarkupLine($"[bold]About to remove:[/] [red]{Markup.Escape(entryPath.Value)}[/]");
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(existing.Entry.Title)}[/]");
            if (!AnsiConsole.Confirm("Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 1;
            }
        }

        storage.Remove(entryPath);

        if (_config.AutoCommit)
            GitRunner.AutoCommitIfRepo(vaultPath, $"pwvault: remove {entryPath.Value}", _config.AutoPush);

        AnsiConsole.MarkupLine($"[green]Removed:[/] {Markup.Escape(entryPath.Value)}");
        return 0;
    }
}
