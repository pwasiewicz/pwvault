using PwVault.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class UnlockCommand : Command<BaseCommandSettings>
{
    private readonly VaultAuthenticator _authenticator;
    private readonly PwVaultConfig _config;

    public UnlockCommand(VaultAuthenticator authenticator, PwVaultConfig config)
    {
        _authenticator = authenticator;
        _config = config;
    }

    protected override int Execute(CommandContext context, BaseCommandSettings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        _authenticator.Authenticate(vaultPath);
        AnsiConsole.MarkupLine($"[green]Vault unlocked.[/] [dim]Session valid for 1 hour.[/]");
        return 0;
    }
}
