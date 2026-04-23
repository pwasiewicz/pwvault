using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class MvCommand : Command<MvCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly PwVaultConfig _config;

    public MvCommand(IFileSystem fs, PwVaultConfig config)
    {
        _fs = fs;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "<src>")]
        [Description("Source entry path.")]
        public string Source { get; init; } = "";

        [CommandArgument(1, "<dst>")]
        [Description("Destination entry path.")]
        public string Destination { get; init; } = "";

        [CommandOption("-f|--force")]
        [Description("Overwrite destination if it exists.")]
        public bool Force { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        var source = new EntryPath(settings.Source);
        var destination = new EntryPath(settings.Destination);

        using var storage = VaultStorage.Open(vaultPath, _fs);
        storage.Get(source);

        if (source == destination)
        {
            AnsiConsole.MarkupLine("[yellow]Source and destination are the same — nothing to do.[/]");
            return 0;
        }

        if (storage.TryGet(destination) is not null)
        {
            if (!settings.Force)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Destination already exists:[/] {Markup.Escape(destination.Value)}. Use [bold]-f[/] to overwrite.");
                return 1;
            }

            if (!settings.SkipConfirmations)
            {
                AnsiConsole.MarkupLine(
                    $"[bold]About to overwrite:[/] [red]{Markup.Escape(destination.Value)}[/]");
                if (!AnsiConsole.Confirm("Continue?", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                    return 1;
                }
            }
        }

        storage.Move(source, destination, overwrite: settings.Force);

        if (_config.AutoCommit)
            GitRunner.AutoCommitIfRepo(
                vaultPath,
                $"pwvault: mv {source.Value} -> {destination.Value}",
                _config.AutoPush);

        AnsiConsole.MarkupLine(
            $"[green]Moved:[/] {Markup.Escape(source.Value)} -> {Markup.Escape(destination.Value)}");
        return 0;
    }
}
