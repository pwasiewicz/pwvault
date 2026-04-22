using Microsoft.Extensions.DependencyInjection;
using PwVault.Cli.Commands;
using PwVault.Cli.Infrastructure;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using Spectre.Console;
using Spectre.Console.Cli;

var config = ConfigLoader.Load();

var services = new ServiceCollection();
services.AddSingleton(config);
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IFileSystem, RealFileSystem>();
services.AddSingleton<ISessionStore>(sp =>
    SessionStoreFactory.Create(sp.GetRequiredService<TimeProvider>()));
services.AddSingleton<IAgeGateway>(sp =>
{
    var cfg = sp.GetRequiredService<PwVaultConfig>();
    return new AgeV1Gateway(
        encryptWorkFactor: cfg.WorkFactor,
        maxDecryptWorkFactor: cfg.MaxDecryptWorkFactor);
});
services.AddSingleton<ICryptoService, CryptoService>();
services.AddSingleton<VaultAuthenticator>();

var app = new CommandApp(new TypeRegistrar(services));
app.Configure(c =>
{
    c.SetApplicationName("pwvault");
    c.AddCommand<InitCommand>("init").WithDescription("Initialize a new vault.");
    c.AddCommand<UnlockCommand>("unlock").WithDescription("Prompt for master password and cache in session.");
    c.AddCommand<LockCommand>("lock").WithDescription("Clear the cached master password.");
    c.AddCommand<AddCommand>("add").WithDescription("Add a new entry.");
    c.AddCommand<EditCommand>("edit").WithDescription("Edit an existing entry.");
    c.AddCommand<GetCommand>("get").WithDescription("Fetch an entry; default copies password to clipboard.");
    c.AddCommand<LsCommand>("ls").WithDescription("List entries as a tree.");
    c.AddCommand<RmCommand>("rm").WithDescription("Remove an entry.");
    c.AddCommand<SearchCommand>("search").WithDescription("Fuzzy search across plaintext metadata.");
    c.AddCommand<TagsCommand>("tags").WithDescription("List all tags in use with entry counts.");
    c.AddCommand<GenCommand>("gen").WithDescription("Generate a random password.");
});

try
{
    return await app.RunAsync(args);
}
catch (VaultNotConfiguredException ex)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
    return 2;
}
catch (InvalidPassphraseException)
{
    AnsiConsole.MarkupLine("[red]Wrong master password.[/]");
    return 3;
}
catch (AgeDecryptionException ex)
{
    AnsiConsole.MarkupLine($"[red]Decryption failed:[/] {Markup.Escape(ex.Message)}");
    return 3;
}
