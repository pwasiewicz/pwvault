using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class AddCommand : Command<AddCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly IAgeGateway _age;
    private readonly VaultAuthenticator _auth;
    private readonly PwVaultConfig _config;

    public AddCommand(IFileSystem fs, IAgeGateway age, VaultAuthenticator auth, PwVaultConfig config)
    {
        _fs = fs;
        _age = age;
        _auth = auth;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Entry path, e.g. 'banking/mbank'.")]
        public string Path { get; init; } = "";

        [CommandOption("--title <TITLE>")]
        public string? Title { get; init; }

        [CommandOption("--username <USER>")]
        public string? Username { get; init; }

        [CommandOption("--url <URL>")]
        public string? Url { get; init; }

        [CommandOption("--notes <NOTES>")]
        public string? Notes { get; init; }

        [CommandOption("--generate")]
        [Description("Generate a random password (skip password prompt).")]
        public bool Generate { get; init; }

        [CommandOption("--length <N>")]
        [Description("Length of generated password (default from config).")]
        public int? Length { get; init; }

        [CommandOption("--tag <TAG>")]
        [Description("Tag (normalized to lowercase). May be repeated.")]
        public string[]? Tags { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        var entryPath = new EntryPath(settings.Path);

        using var storage = VaultStorage.Open(vaultPath, _fs);
        if (storage.TryGet(entryPath) is not null)
            throw new InvalidOperationException($"Entry '{entryPath.Value}' already exists. Use 'edit' instead.");

        var master = _auth.Authenticate(vaultPath);

        var title = settings.Title ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Title:").DefaultValue(entryPath.Name).ShowDefaultValue());
        var username = settings.Username ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Username:").AllowEmpty());
        var url = settings.Url ?? AnsiConsole.Prompt(
            new TextPrompt<string>("URL:").AllowEmpty());

        var password = ResolvePassword(settings);
        var notes = settings.Notes ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Notes (optional, encrypted):").AllowEmpty());

        var passwordAge = new EncryptedField(_age.Encrypt(password, master));
        EncryptedField? notesAge = string.IsNullOrWhiteSpace(notes)
            ? null
            : new EncryptedField(_age.Encrypt(notes, master));

        var entry = new VaultEntry(
            Path: entryPath,
            Title: string.IsNullOrWhiteSpace(title) ? entryPath.Name : title,
            Username: string.IsNullOrWhiteSpace(username) ? null : username,
            Url: string.IsNullOrWhiteSpace(url) ? null : url,
            PasswordEncrypted: passwordAge,
            NotesEncrypted: notesAge)
        {
            Tags = settings.Tags ?? Array.Empty<string>(),
        };

        storage.Add(entry);

        if (_config.AutoCommit)
            GitRunner.AutoCommitIfRepo(vaultPath, $"pwvault: add {entryPath.Value}", _config.AutoPush);

        AnsiConsole.MarkupLine($"[green]Added entry:[/] {Markup.Escape(entryPath.Value)}");
        return 0;
    }

    private string ResolvePassword(Settings settings)
    {
        if (settings.Generate)
        {
            var length = settings.Length ?? _config.GeneratedPasswordLength;
            var pw = PasswordGenerator.Generate(length);
            AnsiConsole.MarkupLine($"[green]Generated password[/] [dim]({length} chars)[/]");
            return pw;
        }

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Password:")
                .AddChoices("Generate", "Type"));

        if (mode == "Generate")
        {
            var length = AnsiConsole.Prompt(
                new TextPrompt<int>("Length:").DefaultValue(_config.GeneratedPasswordLength));
            var pw = PasswordGenerator.Generate(length);
            AnsiConsole.MarkupLine($"[green]Generated.[/]");
            return pw;
        }

        while (true)
        {
            var first = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret('*'));
            if (string.IsNullOrEmpty(first))
            {
                AnsiConsole.MarkupLine("[red]Password cannot be empty.[/]");
                continue;
            }
            var second = AnsiConsole.Prompt(new TextPrompt<string>("Confirm password:").Secret('*'));
            if (first == second) return first;
            AnsiConsole.MarkupLine("[red]Passwords did not match. Try again.[/]");
        }
    }
}
