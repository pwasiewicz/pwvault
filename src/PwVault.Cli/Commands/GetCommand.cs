using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class GetCommand : Command<GetCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly ICryptoService _crypto;
    private readonly VaultAuthenticator _auth;
    private readonly PwVaultConfig _config;

    public GetCommand(IFileSystem fs, ICryptoService crypto, VaultAuthenticator auth, PwVaultConfig config)
    {
        _fs = fs;
        _crypto = crypto;
        _auth = auth;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "[path]")]
        public string? Path { get; init; }

        [CommandOption("-i|--interactive")]
        [Description("Pick an entry from a live-filtering picker (type to filter by path/title/username/tags).")]
        public bool Interactive { get; init; }

        [CommandOption("-c|--clip")]
        [Description("Copy password to clipboard (auto-clears). Default behavior.")]
        public bool Clip { get; init; }

        [CommandOption("--show")]
        [Description("Print password to stdout instead of copying.")]
        public bool Show { get; init; }

        [CommandOption("--notes")]
        [Description("Also print decrypted notes.")]
        public bool IncludeNotes { get; init; }

        public override ValidationResult Validate()
        {
            var hasPath = !string.IsNullOrWhiteSpace(Path);
            if (hasPath && Interactive)
                return ValidationResult.Error("Cannot combine -i with an explicit path.");
            if (!hasPath && !Interactive)
                return ValidationResult.Error("Provide a path or use -i for the interactive picker.");
            return ValidationResult.Success();
        }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);

        using var storage = VaultStorage.Open(vaultPath, _fs);
        var stored = settings.Interactive
            ? InteractiveEntryPicker.Pick(storage, "Select entry")
            : storage.Get(new EntryPath(settings.Path!));

        if (stored is null) return 0;

        _auth.Authenticate(vaultPath);

        var passwordResult = _crypto.DecryptPassword(stored.Entry);
        if (passwordResult.Status != DecryptionStatus.Success || passwordResult.PlainText is null)
            throw new AgeDecryptionException("Failed to decrypt password.");

        PrintSummary(stored.Entry);

        if (settings.Show)
        {
            AnsiConsole.MarkupLine($"[bold]Password:[/] {Markup.Escape(passwordResult.PlainText)}");
        }
        else
        {
            ClipboardCopier.CopyWithAutoClear(passwordResult.PlainText, _config.ClipboardClearSeconds);
        }

        if (settings.IncludeNotes && stored.Entry.NotesEncrypted is not null)
        {
            var notesResult = _crypto.DecryptNotes(stored.Entry);
            if (notesResult.Status == DecryptionStatus.Success && notesResult.PlainText is not null)
            {
                AnsiConsole.Write(new Panel(Markup.Escape(notesResult.PlainText))
                    .Header("[yellow]Notes[/]"));
            }
        }

        return 0;
    }

    private static void PrintSummary(VaultEntry entry)
    {
        var table = new Table().NoBorder().HideHeaders();
        table.AddColumn("k");
        table.AddColumn("v");
        table.AddRow("[dim]Path[/]", Markup.Escape(entry.Path.Value));
        table.AddRow("[dim]Title[/]", Markup.Escape(entry.Title));
        if (!string.IsNullOrEmpty(entry.Username))
            table.AddRow("[dim]Username[/]", Markup.Escape(entry.Username));
        if (!string.IsNullOrEmpty(entry.Url))
            table.AddRow("[dim]URL[/]", Markup.Escape(entry.Url));
        table.AddRow("[dim]Updated[/]", entry.Updated.ToString("u"));
        AnsiConsole.Write(table);
    }
}
