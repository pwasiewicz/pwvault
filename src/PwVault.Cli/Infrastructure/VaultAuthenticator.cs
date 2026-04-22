using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;
using Spectre.Console;

namespace PwVault.Cli.Infrastructure;

public sealed class VaultAuthenticator
{
    private readonly ISessionStore _session;
    private readonly IAgeGateway _age;
    private readonly IFileSystem _fs;

    public VaultAuthenticator(ISessionStore session, IAgeGateway age, IFileSystem fs)
    {
        _session = session;
        _age = age;
        _fs = fs;
    }

    public string Authenticate(string vaultRoot, string? providedMaster = null, bool saveToSession = true)
    {
        var metadata = VaultMetadataStore.Read(vaultRoot, _fs);

        if (providedMaster is not null)
        {
            VerifySentinel(metadata.SentinelAge, providedMaster);
            if (saveToSession) _session.Save(providedMaster);
            return providedMaster;
        }

        var sessionMaster = _session.TryGetAndExtend();
        if (sessionMaster is not null)
        {
            try
            {
                VerifySentinel(metadata.SentinelAge, sessionMaster);
                return sessionMaster;
            }
            catch (InvalidPassphraseException)
            {
                _session.Clear();
            }
        }

        while (true)
        {
            var entered = MasterPasswordPrompt.Ask();
            try
            {
                VerifySentinel(metadata.SentinelAge, entered);
                if (saveToSession) _session.Save(entered);
                return entered;
            }
            catch (InvalidPassphraseException)
            {
                AnsiConsole.MarkupLine("[red]Wrong master password.[/]");
            }
        }
    }

    private void VerifySentinel(string sentinelAge, string master)
    {
        var decrypted = _age.Decrypt(sentinelAge, master);
        if (decrypted != VaultMetadataStore.SentinelPlaintext)
            throw new InvalidPassphraseException();
    }
}
