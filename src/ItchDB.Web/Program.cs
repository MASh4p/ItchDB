using ApexCharts;
using ItchDB.Infrastructure;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Http;
using ItchDB.Web.Components;
using ItchDB.Web.Services;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
// The app is meant to be started with `dotnet run` in any environment, so serve package assets from the build output.
builder.WebHost.UseStaticWebAssets();

var dataRoot = builder.Configuration["DataDirectory"] is { Length: > 0 } configured
    ? Path.GetFullPath(configured, builder.Environment.ContentRootPath)
    : Path.Combine(builder.Environment.ContentRootPath, "data");

builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .MinimumLevel.Override("Polly", LogEventLevel.Fatal)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(dataRoot, "logs", "itchdb-.log"), rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddApexCharts(o =>
{
    o.GlobalOptions = new ApexChartBaseOptions
    {
        Theme = new Theme { Mode = Mode.Dark },
        Chart = new Chart { Background = "transparent", FontFamily = "Segoe UI, Inter, Roboto, sans-serif" },
    };
});

builder.Services.AddItchInfrastructure(dataRoot);
builder.Services.AddScoped<UiState>();
builder.Services.AddScoped<UiText>();
builder.Services.AddScoped<ExportService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);
if (!ApplyLaunchProxy(app, args)) return;
_ = app.Services.GetRequiredService<AnalyticsService>().RefreshAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static bool ApplyLaunchProxy(WebApplication app, string[] args)
{
    if (!LaunchProxy.TryReadArgument(args, out var address, out var error))
    {
        Log.Error("{Error}", error);
        Environment.ExitCode = 1;
        return false;
    }
    app.Services.GetRequiredService<LaunchProxy>().Address = address;
    if (address.Length > 0) Log.Information("Локальный прокси: {Proxy}", address);
    else Log.Information("Запуск без прокси");
    return true;
}
