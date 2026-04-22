using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Web.Components;
using PwVault.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<VaultOptions>(builder.Configuration.GetSection("PwVault"));

builder.Services.AddSingleton<IFileSystem, RealFileSystem>();
builder.Services.AddSingleton<IAgeGateway>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VaultOptions>>().Value;
    return new AgeV1Gateway(
        encryptWorkFactor: opts.WorkFactor,
        maxDecryptWorkFactor: opts.MaxDecryptWorkFactor);
});
builder.Services.AddSingleton<VaultAccess>();
builder.Services.AddScoped<VaultSession>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
