using PwVault.Cli.Infrastructure;
using PwVault.Core.Security;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class LockCommand : Command<BaseCommandSettings>
{
    private readonly ISessionStore _session;

    public LockCommand(ISessionStore session) => _session = session;

    protected override int Execute(CommandContext context, BaseCommandSettings settings, CancellationToken cancellationToken)
    {
        _session.Clear();
        AnsiConsole.MarkupLine("[green]Session cleared.[/]");
        return 0;
    }
}
