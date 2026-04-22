using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;
using TextCopy;

namespace PwVault.Cli.Commands;

public sealed class GenCommand : Command<GenCommand.Settings>
{
    private readonly PwVaultConfig _config;

    public GenCommand(PwVaultConfig config) => _config = config;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[length]")]
        [Description("Password length (default from config, usually 24).")]
        public int? Length { get; init; }

        [CommandOption("--no-symbols")]
        public bool NoSymbols { get; init; }

        [CommandOption("-c|--clip")]
        [Description("Copy to clipboard instead of printing.")]
        public bool Clip { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var length = settings.Length ?? _config.GeneratedPasswordLength;
        var password = PasswordGenerator.Generate(length, includeSymbols: !settings.NoSymbols);

        if (settings.Clip)
        {
            ClipboardService.SetText(password);
            AnsiConsole.MarkupLine($"[green]Generated {length}-char password copied to clipboard.[/]");
        }
        else
        {
            AnsiConsole.WriteLine(password);
        }
        return 0;
    }
}
