using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class InitCommand : Command<InitCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly IAgeGateway _age;
    private readonly PwVaultConfig _config;

    public InitCommand(IFileSystem fs, IAgeGateway age, PwVaultConfig config)
    {
        _fs = fs;
        _age = age;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Directory to initialize as a vault. Defaults to configured path.")]
        public string? Path { get; init; }

        [CommandOption("--no-git")]
        [Description("Skip git init and initial commit.")]
        public bool NoGit { get; init; }

        [CommandOption("--no-save-config")]
        [Description("Skip writing vault_path to config.json.")]
        public bool NoSaveConfig { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var path = settings.Path ?? settings.VaultPath ?? _config.VaultPath
            ?? throw new VaultNotConfiguredException(
                "Provide a path argument, use --vault, set PWVAULT_PATH, or configure vault_path.");
        path = System.IO.Path.GetFullPath(path);

        if (VaultMetadataStore.Exists(path, _fs))
            throw new InvalidOperationException($"Vault already initialized at '{path}'.");

        if (Directory.Exists(path)
            && Directory.EnumerateFileSystemEntries(path).Any(e => System.IO.Path.GetFileName(e) != ".git"))
        {
            throw new InvalidOperationException(
                $"Directory '{path}' is not empty. Refusing to init. Move existing files or pick another path.");
        }

        Directory.CreateDirectory(path);

        AnsiConsole.MarkupLine($"[bold]Initializing vault at:[/] {path}");
        var master = MasterPasswordPrompt.AskWithConfirmation();

        var sentinelAge = _age.Encrypt(VaultMetadataStore.SentinelPlaintext, master);
        VaultMetadataStore.Write(path, _fs, new VaultMetadata(SchemaVersion: 1, SentinelAge: sentinelAge));

        WriteHelperFiles(path);

        if (!settings.NoGit)
        {
            if (!GitRunner.IsGitRepo(path))
                GitRunner.Init(path);
            GitRunner.AddAll(path);
            if (GitRunner.HasStagedChanges(path))
                GitRunner.Commit(path, "pwvault: initialize vault");
        }

        AnsiConsole.MarkupLine($"[green]Vault initialized.[/]");
        AnsiConsole.MarkupLine("[yellow]Remember your master password — it cannot be recovered.[/]");

        if (!settings.NoSaveConfig)
            PersistVaultPath(path, settings.SkipConfirmations);

        AnsiConsole.MarkupLine($"[dim]Use 'pwvault add <path>' to create your first entry.[/]");
        return 0;
    }

    private static void PersistVaultPath(string vaultPath, bool assumeYes)
    {
        var existing = ConfigLoader.LoadFromFile() ?? new PwVaultConfig();

        if (string.IsNullOrWhiteSpace(existing.VaultPath))
        {
            existing.VaultPath = vaultPath;
            ConfigLoader.Save(existing);
            AnsiConsole.MarkupLine($"[dim]Saved as default vault in {Markup.Escape(ConfigLoader.GetConfigPath())}[/]");
            return;
        }

        if (existing.VaultPath == vaultPath) return;

        var setAsDefault = assumeYes || AnsiConsole.Confirm(
            $"Current default vault is [yellow]{Markup.Escape(existing.VaultPath)}[/]. Set [cyan]{Markup.Escape(vaultPath)}[/] as default?",
            defaultValue: false);

        if (!setAsDefault)
        {
            AnsiConsole.MarkupLine($"[dim]Keeping default vault unchanged. Pass --vault {Markup.Escape(vaultPath)} to use the new one, or run 'pwvault config set vault_path {Markup.Escape(vaultPath)}'.[/]");
            return;
        }

        existing.VaultPath = vaultPath;
        ConfigLoader.Save(existing);
        AnsiConsole.MarkupLine("[dim]Default vault updated.[/]");
    }

    private static void WriteHelperFiles(string vaultRoot)
    {
        File.WriteAllText(System.IO.Path.Combine(vaultRoot, "README.md"), ReadmeContent);
        File.WriteAllText(System.IO.Path.Combine(vaultRoot, "FORMAT.md"), FormatContent);
        var recoverPath = System.IO.Path.Combine(vaultRoot, "recover.sh");
        File.WriteAllText(recoverPath, RecoverScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(recoverPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    private const string ReadmeContent =
        """
        # pwvault vault

        This directory is a pwvault vault. Entries are JSON files laid out as
        `{category}/{name}.json`, with the password and notes encrypted using
        [age v1](https://age-encryption.org/v1) in passphrase (scrypt) mode.

        ## Emergency recovery — without pwvault binary

        You only need `age` installed (`apt install age` / `brew install age`).

        1. Open the JSON file of the entry you need (for example, `banking/mbank.json`).
        2. Copy the contents of the `password_age` field (everything between
           `-----BEGIN AGE ENCRYPTED FILE-----` and `-----END AGE ENCRYPTED FILE-----`, inclusive).
        3. Paste it into `age -d`:

           ```bash
           age -d <<'EOF'
           -----BEGIN AGE ENCRYPTED FILE-----
           ...paste here...
           -----END AGE ENCRYPTED FILE-----
           EOF
           ```

           You will be prompted for your master password.

        There is also `recover.sh` in this directory that wraps the above for you.

        ## File format

        See `FORMAT.md` for the schema and decryption steps in detail.
        """;

    private const string FormatContent =
        """
        # pwvault file format (schema v1)

        ## Per-entry file

        Location: `{vault_root}/{category}/{name}.json`

        ```json
        {
          "schema_version": 1,
          "title": "Human-readable name",
          "username": "login (optional, plaintext)",
          "url": "https://example.com (optional, plaintext)",
          "password_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----",
          "notes_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----",
          "created": "ISO-8601 timestamp",
          "updated": "ISO-8601 timestamp"
        }
        ```

        - `title` and `password_age` are required; all other fields may be absent or null.
        - `password_age` and `notes_age` are age v1 ASCII-armored ciphertext, encrypted
          with the vault's master password via scrypt (work factor 18 by default).
        - All other fields are plaintext — keep metadata leakage in mind.

        ## Vault metadata

        Location: `{vault_root}/.vault.json`

        ```json
        {
          "schema_version": 1,
          "sentinel_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----"
        }
        ```

        The sentinel is the fixed plaintext `pwvault-sentinel-v1` encrypted with the
        master password. The CLI uses it to verify the master password before writing
        new entries (prevents encrypting with a typo that would orphan the entry).

        ## Manual decryption

        ```bash
        # Extract the password_age field value (everything including BEGIN/END markers),
        # then pipe to age:
        age -d path/to/ciphertext.age
        # prompts for master password → prints plaintext
        ```
        """;

    private const string RecoverScript =
        """
        #!/usr/bin/env bash
        # Recover a pwvault entry's password (or notes) using only age + jq.
        # Usage:
        #   ./recover.sh <relative-path-to-entry>            # decrypts password_age
        #   ./recover.sh <relative-path-to-entry> notes      # decrypts notes_age
        set -euo pipefail

        if [[ $# -lt 1 ]]; then
            echo "usage: $0 <entry-path> [password|notes]" >&2
            exit 2
        fi

        entry_path="$1"
        field="${2:-password}"
        case "$field" in
            password) json_field=password_age ;;
            notes)    json_field=notes_age ;;
            *) echo "unknown field: $field (use 'password' or 'notes')" >&2; exit 2 ;;
        esac

        file="$entry_path"
        [[ -f "$file" ]] || file="$entry_path.json"
        [[ -f "$file" ]] || { echo "not found: $entry_path" >&2; exit 1; }

        command -v jq >/dev/null 2>&1 || { echo "jq is required" >&2; exit 1; }
        command -v age >/dev/null 2>&1 || { echo "age is required" >&2; exit 1; }

        ciphertext=$(jq -r ".${json_field} // empty" "$file")
        if [[ -z "$ciphertext" || "$ciphertext" == "null" ]]; then
            echo "field '${json_field}' missing or empty in $file" >&2
            exit 1
        fi

        printf '%s\n' "$ciphertext" | age -d
        """;
}
