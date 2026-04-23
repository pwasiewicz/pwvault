using PwVault.Cli.Infrastructure;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class RotateMasterCommand : Command<RotateMasterCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly IAgeGateway _age;
    private readonly VaultAuthenticator _auth;
    private readonly ISessionStore _session;
    private readonly PwVaultConfig _config;
    private readonly TimeProvider _time;

    public RotateMasterCommand(
        IFileSystem fs,
        IAgeGateway age,
        VaultAuthenticator auth,
        ISessionStore session,
        PwVaultConfig config,
        TimeProvider time)
    {
        _fs = fs;
        _age = age;
        _auth = auth;
        _session = session;
        _config = config;
        _time = time;
    }

    public sealed class Settings : BaseCommandSettings
    {
        // No flags yet — master passwords must always be interactive (never as argv).
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);

        AnsiConsole.MarkupLine("[bold]Rotating master password[/]");
        AnsiConsole.MarkupLine("[dim]Every encrypted field (passwords + notes) will be re-encrypted.[/]");
        AnsiConsole.WriteLine();

        var oldMaster = _auth.Authenticate(vaultPath, saveToSession: false);

        var newMaster = MasterPasswordPrompt.AskWithConfirmation();
        if (newMaster == oldMaster)
        {
            AnsiConsole.MarkupLine("[yellow]New master password is identical to the old one — nothing to do.[/]");
            return 1;
        }

        using var storage = VaultStorage.Open(vaultPath, _fs);
        var entries = storage.List();

        AnsiConsole.MarkupLine($"[bold]Entries to re-encrypt:[/] {entries.Count}");
        if (!settings.SkipConfirmations)
        {
            if (!AnsiConsole.Confirm("A backup of the vault will be created alongside it. Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 1;
            }
        }

        var backupPath = VaultBackup.Create(vaultPath, _time.GetUtcNow());
        AnsiConsole.MarkupLine($"[green]Backup:[/] {Markup.Escape(backupPath)}");

        MasterRotationResult result;
        try
        {
            result = AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn())
                .Start(ctx =>
                {
                    var progress = ctx.AddTask("Re-encrypting", maxValue: Math.Max(entries.Count, 1));
                    return MasterRotator.Rotate(
                        storage, _age, _fs, vaultPath, oldMaster, newMaster,
                        onProgress: (done, total) =>
                        {
                            progress.MaxValue = Math.Max(total, 1);
                            progress.Value = done;
                        });
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Rotation failed:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine($"[yellow]Vault may be in a partial state. Restore from:[/] {Markup.Escape(backupPath)}");
            throw;
        }

        _session.Save(newMaster);

        if (_config.AutoCommit)
            GitRunner.AutoCommitIfRepo(
                vaultPath,
                $"pwvault: rotate master password ({result.EntriesRewritten} entries)",
                _config.AutoPush);

        AnsiConsole.MarkupLine(
            $"[green]Done.[/] {result.EntriesRewritten} entries re-encrypted with new master password.");
        AnsiConsole.MarkupLine(
            $"[dim]Once you have verified the vault works, you can delete[/] {Markup.Escape(backupPath)}");
        return 0;
    }
}
