using MedorSklad.Controllers;
using MedorSklad.Data;
using MedorSklad.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi(); // ve všech prostředích, je to demo: /openapi/v1.json
app.MapControllers();

await app.RunAsync();

// Zpřístupnění pro integrační testy (WebApplicationFactory).
public partial class Program;
