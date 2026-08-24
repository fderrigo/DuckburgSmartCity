using Microsoft.AspNetCore.HttpOverrides;
using Duckburg.Identity.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration sources (precedence, highest last):
//   appsettings.json -> appsettings.{Env}.json -> environment variables.
// RP private keys come from a mounted file (Rp:PrivateKeysFile) or env (Rp__PrivateKeys).
// For a managed secret store add e.g. Azure Key Vault here:
//   builder.Configuration.AddAzureKeyVault(new Uri(builder.Configuration["KeyVault:Uri"]!),
//       new Azure.Identity.DefaultAzureCredential());

builder.Services.AddControllersWithViews();

// Relying Party identity: public config from image, private keys from a secret source.
builder.Services.AddSingleton(sp =>
    RpConfig.Load(builder.Configuration, sp.GetRequiredService<IWebHostEnvironment>()));

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<EntityConfigurationBuilder>();
builder.Services.AddSingleton<SsoHandoff>();
builder.Services.AddSingleton<AuthSessionStore>();
builder.Services.AddHttpClient("oidc");
builder.Services.AddHttpClient<TrustChainResolver>();
builder.Services.AddScoped<OidcClient>();

// Behind a reverse proxy/ingress in production, honor X-Forwarded-* (correct scheme/host).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

// Fail fast if the RP identity (incl. secret keys) cannot be loaded.
var rp = app.Services.GetRequiredService<RpConfig>();
app.Logger.LogInformation("SPID/CIE OIDC RP (MVC) started as {Sub}", rp.Sub);

app.Run();
