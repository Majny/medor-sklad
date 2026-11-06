using MedorSklad.Controllers;
using MedorSklad.Data;
using MedorSklad.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Relativní cestu k SQLite souboru vztáhneme ke ContentRoot, aby DB vznikla vždy v src/MedorSklad.
var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Warehouse"));
if (!Path.IsPathRooted(connection.DataSource) && connection.DataSource != ":memory:")
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);

builder.Services.AddDbContext<WarehouseDbContext>(options => options
    .UseSqlite(connection.ConnectionString)
    .UseSeeding((ctx, _) => DbSeeder.Seed((WarehouseDbContext)ctx))
    .UseAsyncSeeding((ctx, _, ct) => DbSeeder.SeedAsync((WarehouseDbContext)ctx, ct)));
builder.Services.AddScoped<StockService>();
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ValidationProblemResponse.Create);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Při startu aplikujeme migrace; MigrateAsync potom spustí i UseAsyncSeeding (naplní prázdnou DB).
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Database.MigrateAsync();

// Bezpečnostní hlavičky pro všechny odpovědi. Frontend nemá inline skripty ani styly a nic nenačítá
// z cizích serverů, takže stačí 'self'. frame-ancestors 'none' brání vložení gridu do cizí stránky.
// Nastavuje se přes OnStarting, aby hlavičky přežily i Response.Clear() v exception handleru.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy =
            "default-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        return Task.CompletedTask;
    });
    return next(context);
});

// Chyby klienta, které vyhodí Kestrel (např. příliš velké tělo, 413), vracíme s jejich kódem, ne jako 500,
// a nelogujeme je jako chybu serveru.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
    SuppressDiagnosticsCallback = c => c.Exception is BadHttpRequestException,
});
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi(); // ve všech prostředích, je to demo: /openapi/v1.json
app.MapControllers();

await app.RunAsync();

// Zpřístupnění pro integrační testy (WebApplicationFactory).
public partial class Program;
