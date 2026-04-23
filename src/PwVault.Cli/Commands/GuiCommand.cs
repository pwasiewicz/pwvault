using PwVault.Cli.Infrastructure;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Storage;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class GuiCommand : Command<GuiCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly ICryptoService _crypto;
    private readonly VaultAuthenticator _auth;
    private readonly PwVaultConfig _config;

    public GuiCommand(IFileSystem fs, ICryptoService crypto, VaultAuthenticator auth, PwVaultConfig config)
    {
        _fs = fs;
        _crypto = crypto;
        _auth = auth;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);

        using var storage = VaultStorage.Open(vaultPath, _fs);

        _auth.Authenticate(vaultPath);

        var app = new VaultGuiApp(storage, _crypto, _config);
        app.Run();
        return 0;
    }
}
