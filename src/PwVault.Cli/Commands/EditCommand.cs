using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class EditCommand : Command<EditCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly IAgeGateway _age;
    private readonly ICryptoService _crypto;
    private readonly VaultAuthenticator _auth;
    private readonly PwVaultConfig _config;

    public EditCommand(
        IFileSystem fs,
        IAgeGateway age,
        ICryptoService crypto,
        VaultAuthenticator auth,
        PwVaultConfig config)
    {
        _fs = fs;
        _age = age;
        _crypto = crypto;
        _auth = auth;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Entry path, e.g. 'banking/mbank'. Omit when using -i.")]
        public string? Path { get; init; }

        [CommandOption("-i|--interactive")]
        [Description("Pick entry from a live-filtering picker before editing.")]
        public bool Interactive { get; init; }

        [CommandOption("--title <TITLE>")]
        public string? Title { get; init; }

        [CommandOption("--username <USER>")]
        public string? Username { get; init; }

        [CommandOption("--clear-username")]
        public bool ClearUsername { get; init; }

        [CommandOption("--url <URL>")]
        public string? Url { get; init; }

        [CommandOption("--clear-url")]
        public bool ClearUrl { get; init; }

        [CommandOption("--notes <NOTES>")]
        public string? Notes { get; init; }

        [CommandOption("--clear-notes")]
        public bool ClearNotes { get; init; }

        [CommandOption("--password")]
        [Description("Prompt for a new password (with confirm).")]
        public bool Password { get; init; }

        [CommandOption("--regenerate")]
        [Description("Replace password with a CSPRNG-generated one.")]
        public bool Regenerate { get; init; }

        [CommandOption("--length <N>")]
        [Description("Length for --regenerate (default from config).")]
        public int? Length { get; init; }

        [CommandOption("--tag <TAG>")]
        [Description("Replace the entire tag list. May be repeated.")]
        public string[]? Tags { get; init; }

        [CommandOption("--add-tag <TAG>")]
        [Description("Add a tag to the existing list. May be repeated.")]
        public string[]? AddTags { get; init; }

        [CommandOption("--remove-tag <TAG>")]
        [Description("Remove a tag from the existing list. May be repeated.")]
        public string[]? RemoveTags { get; init; }

        [CommandOption("--clear-tags")]
        public bool ClearTags { get; init; }

        public override ValidationResult Validate()
        {
            var hasPath = !string.IsNullOrWhiteSpace(Path);
            if (hasPath && Interactive)
                return ValidationResult.Error("Cannot combine -i with an explicit path.");
            if (!hasPath && !Interactive)
                return ValidationResult.Error("Provide a path or use -i for the interactive picker.");

            if (Password && Regenerate)
                return ValidationResult.Error("Cannot combine --password and --regenerate.");
            if (Length.HasValue && !Regenerate)
                return ValidationResult.Error("--length only valid with --regenerate.");
            if (Notes is not null && ClearNotes)
                return ValidationResult.Error("Cannot combine --notes and --clear-notes.");
            if (Username is not null && ClearUsername)
                return ValidationResult.Error("Cannot combine --username and --clear-username.");
            if (Url is not null && ClearUrl)
                return ValidationResult.Error("Cannot combine --url and --clear-url.");

            var hasTagsReplace = Tags is not null || ClearTags;
            var hasTagsDiff = (AddTags is { Length: > 0 }) || (RemoveTags is { Length: > 0 });
            if (hasTagsReplace && hasTagsDiff)
                return ValidationResult.Error(
                    "Cannot combine --tag/--clear-tags with --add-tag/--remove-tag.");

            if (Username is "")
                return ValidationResult.Error("Use --clear-username to clear; --username cannot be empty.");
            if (Url is "")
                return ValidationResult.Error("Use --clear-url to clear; --url cannot be empty.");
            if (Title is "")
                return ValidationResult.Error("--title cannot be empty; title is required on every entry.");

            return ValidationResult.Success();
        }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);

        using var storage = VaultStorage.Open(vaultPath, _fs);
        var stored = settings.Interactive
            ? InteractiveEntryPicker.Pick(storage, "Select entry to edit")
            : storage.Get(new EntryPath(settings.Path!));

        if (stored is null) return 0;
        var entryPath = stored.Entry.Path;
        var current = stored.Entry;

        var plan = BuildPlan(settings, current);
        if (!plan.HasAnyChange)
        {
            AnsiConsole.MarkupLine("[dim]No changes.[/]");
            return 0;
        }

        string? master = null;
        if (plan.NeedsReencrypt)
            master = _auth.Authenticate(vaultPath);

        var updated = ApplyPlan(current, plan, master);
        storage.Update(updated);

        PrintDiff(current, updated, plan);

        if (_config.AutoCommit)
        {
            var changedFields = DescribeChanges(plan);
            GitRunner.AutoCommitIfRepo(
                vaultPath,
                $"pwvault: edit {entryPath.Value} ({changedFields})",
                _config.AutoPush);
        }

        return 0;
    }

    private EditPlan BuildPlan(Settings settings, VaultEntry current)
    {
        var anyFlag = HasAnyModificationFlag(settings);
        return anyFlag ? PlanFromFlags(settings, current) : PlanFromPrompts(current);
    }

    private static bool HasAnyModificationFlag(Settings s) =>
        s.Title is not null
        || s.Username is not null || s.ClearUsername
        || s.Url is not null || s.ClearUrl
        || s.Notes is not null || s.ClearNotes
        || s.Password || s.Regenerate
        || s.Tags is not null || s.AddTags is not null || s.RemoveTags is not null || s.ClearTags;

    private EditPlan PlanFromFlags(Settings s, VaultEntry current)
    {
        var plan = new EditPlan();

        if (s.Title is not null && s.Title != current.Title) plan.NewTitle = s.Title;

        if (s.ClearUsername) plan.SetUsername(null);
        else if (s.Username is not null && s.Username != current.Username) plan.SetUsername(s.Username);

        if (s.ClearUrl) plan.SetUrl(null);
        else if (s.Url is not null && s.Url != current.Url) plan.SetUrl(s.Url);

        if (s.ClearNotes) plan.SetNotes(null);
        else if (s.Notes is not null) plan.SetNotes(s.Notes);

        if (s.Regenerate)
        {
            var length = s.Length ?? _config.GeneratedPasswordLength;
            plan.NewPassword = PasswordGenerator.Generate(length);
            plan.PasswordSource = PasswordSource.Generated;
        }
        else if (s.Password)
        {
            plan.NewPassword = PromptNewPassword();
            plan.PasswordSource = PasswordSource.Typed;
        }

        plan.NewTags = ResolveTagsFromFlags(s, current.Tags);

        return plan;
    }

    private EditPlan PlanFromPrompts(VaultEntry current)
    {
        AnsiConsole.MarkupLine(
            $"[dim]Editing {Markup.Escape(current.Path.Value)}. Press Enter to keep the current value.[/]");
        AnsiConsole.MarkupLine(
            "[dim]To clear a field, cancel and re-run with --clear-username / --clear-url / --clear-notes / --clear-tags.[/]");
        var plan = new EditPlan();

        var title = AnsiConsole.Prompt(
            new TextPrompt<string>("Title:").DefaultValue(current.Title).ShowDefaultValue());
        if (title != current.Title) plan.NewTitle = title;

        var usernameDefault = current.Username ?? "";
        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("Username:")
                .DefaultValue(usernameDefault)
                .ShowDefaultValue()
                .AllowEmpty());
        var usernameNew = string.IsNullOrWhiteSpace(username) ? null : username;
        if (usernameNew != current.Username) plan.SetUsername(usernameNew);

        var urlDefault = current.Url ?? "";
        var url = AnsiConsole.Prompt(
            new TextPrompt<string>("URL:")
                .DefaultValue(urlDefault)
                .ShowDefaultValue()
                .AllowEmpty());
        var urlNew = string.IsNullOrWhiteSpace(url) ? null : url;
        if (urlNew != current.Url) plan.SetUrl(urlNew);

        var tagsDefault = string.Join(", ", current.Tags);
        var normalizedTags = TagPrompt.Prompt("Tags (comma-separated):", tagsDefault);
        if (!normalizedTags.SequenceEqual(current.Tags, StringComparer.Ordinal))
            plan.NewTags = normalizedTags;

        var passwordChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Password:")
                .AddChoices("Keep", "Type", "Generate"));
        switch (passwordChoice)
        {
            case "Type":
                plan.NewPassword = PromptNewPassword();
                plan.PasswordSource = PasswordSource.Typed;
                break;
            case "Generate":
                var length = AnsiConsole.Prompt(
                    new TextPrompt<int>("Length:").DefaultValue(_config.GeneratedPasswordLength));
                plan.NewPassword = PasswordGenerator.Generate(length);
                plan.PasswordSource = PasswordSource.Generated;
                AnsiConsole.MarkupLine("[green]Generated.[/]");
                break;
        }

        var notesChoices = current.NotesEncrypted is null
            ? new[] { "Keep (no notes)", "Type" }
            : new[] { "Keep", "Replace", "Clear" };
        var notesChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title("Notes:").AddChoices(notesChoices));
        if (notesChoice == "Type" || notesChoice == "Replace")
        {
            var newNotes = AnsiConsole.Prompt(
                new TextPrompt<string>("Notes:").AllowEmpty());
            plan.SetNotes(string.IsNullOrWhiteSpace(newNotes) ? null : newNotes);
        }
        else if (notesChoice == "Clear")
        {
            plan.SetNotes(null);
        }

        return plan;
    }

    private static IReadOnlyList<string>? ResolveTagsFromFlags(Settings s, IReadOnlyList<string> current)
    {
        if (s.ClearTags) return Array.Empty<string>();
        if (s.Tags is not null) return s.Tags;

        var add = s.AddTags ?? Array.Empty<string>();
        var remove = s.RemoveTags ?? Array.Empty<string>();
        if (add.Length == 0 && remove.Length == 0) return null;

        return TagEditor.Apply(current, add, remove);
    }

    private VaultEntry ApplyPlan(VaultEntry current, EditPlan plan, string? master)
    {
        var next = current;

        if (plan.NewTitle is not null)
            next = next with { Title = plan.NewTitle };

        if (plan.UsernameTouched)
            next = next with { Username = plan.NewUsername };

        if (plan.UrlTouched)
            next = next with { Url = plan.NewUrl };

        if (plan.NewTags is not null)
            next = next with { Tags = plan.NewTags };

        if (plan.NewPassword is not null)
        {
            if (master is null)
                throw new InvalidOperationException("Re-encryption requested without authentication.");
            next = next with { PasswordEncrypted = new EncryptedField(_age.Encrypt(plan.NewPassword, master)) };
        }

        if (plan.NotesTouched)
        {
            if (plan.NewNotes is null)
            {
                next = next with { NotesEncrypted = null };
            }
            else
            {
                if (master is null)
                    throw new InvalidOperationException("Re-encryption requested without authentication.");
                next = next with { NotesEncrypted = new EncryptedField(_age.Encrypt(plan.NewNotes, master)) };
            }
        }

        return next;
    }

    private static void PrintDiff(VaultEntry before, VaultEntry after, EditPlan plan)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Field");
        table.AddColumn("Before");
        table.AddColumn("After");

        if (plan.NewTitle is not null)
            table.AddRow("title", Markup.Escape(before.Title), Markup.Escape(after.Title));
        if (plan.UsernameTouched)
            table.AddRow("username", Markup.Escape(before.Username ?? "(none)"), Markup.Escape(after.Username ?? "(cleared)"));
        if (plan.UrlTouched)
            table.AddRow("url", Markup.Escape(before.Url ?? "(none)"), Markup.Escape(after.Url ?? "(cleared)"));
        if (plan.NewTags is not null)
            table.AddRow("tags",
                Markup.Escape($"[{string.Join(", ", before.Tags)}]"),
                Markup.Escape($"[{string.Join(", ", after.Tags)}]"));
        if (plan.NewPassword is not null)
        {
            var note = plan.PasswordSource == PasswordSource.Generated ? "re-encrypted (generated)" : "re-encrypted (typed)";
            table.AddRow("password", "***", $"[green]{note}[/]");
        }
        if (plan.NotesTouched)
        {
            var afterDesc = plan.NewNotes is null ? "[yellow]cleared[/]" : "[green]re-encrypted[/]";
            var beforeDesc = before.NotesEncrypted is null ? "(none)" : "***";
            table.AddRow("notes", beforeDesc, afterDesc);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[green]Updated {Markup.Escape(after.Path.Value)}.[/]");
    }

    private static string DescribeChanges(EditPlan plan)
    {
        var parts = new List<string>();
        if (plan.NewTitle is not null) parts.Add("title");
        if (plan.UsernameTouched) parts.Add("username");
        if (plan.UrlTouched) parts.Add("url");
        if (plan.NewTags is not null) parts.Add("tags");
        if (plan.NewPassword is not null) parts.Add("password");
        if (plan.NotesTouched) parts.Add("notes");
        return parts.Count == 0 ? "no-op" : string.Join(", ", parts);
    }

    private static string PromptNewPassword()
    {
        while (true)
        {
            var first = AnsiConsole.Prompt(new TextPrompt<string>("New password:").Secret('*'));
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

    private enum PasswordSource { None, Typed, Generated }

    private sealed class EditPlan
    {
        public string? NewTitle { get; set; }

        public bool UsernameTouched { get; private set; }
        public string? NewUsername { get; private set; }
        public void SetUsername(string? value) { UsernameTouched = true; NewUsername = value; }

        public bool UrlTouched { get; private set; }
        public string? NewUrl { get; private set; }
        public void SetUrl(string? value) { UrlTouched = true; NewUrl = value; }

        public bool NotesTouched { get; private set; }
        public string? NewNotes { get; private set; }
        public void SetNotes(string? value) { NotesTouched = true; NewNotes = value; }

        public string? NewPassword { get; set; }
        public PasswordSource PasswordSource { get; set; } = PasswordSource.None;

        public IReadOnlyList<string>? NewTags { get; set; }

        public bool NeedsReencrypt => NewPassword is not null || (NotesTouched && NewNotes is not null);

        public bool HasAnyChange =>
            NewTitle is not null
            || UsernameTouched
            || UrlTouched
            || NotesTouched
            || NewPassword is not null
            || NewTags is not null;
    }
}
