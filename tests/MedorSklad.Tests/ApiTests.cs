using System.Net;
using System.Net.Http.Json;
using MedorSklad.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace MedorSklad.Tests;

/// <summary>Integrační testy REST API nad celou aplikací se seedovanou databází v dočasném souboru.</summary>
public sealed class ApiTests : IClassFixture<ApiTests.App>
{
    public sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"medor-sklad-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Warehouse", $"Data Source={_dbPath}");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }

    private readonly HttpClient _client;

    public ApiTests(App app) => _client = app.CreateClient();

    private async Task<FilterOptions> Filters() =>
        (await _client.GetFromJsonAsync<FilterOptions>("/api/filters"))!;

    private async Task<List<StockRow>> Stock(string query = "") =>
        (await _client.GetFromJsonAsync<List<StockRow>>($"/api/stock{query}"))!;

    [Fact]
    public async Task Filters_return_seeded_clients_categories_and_products()
    {
        var filters = await Filters();

        Assert.NotEmpty(filters.Clients);
        Assert.NotEmpty(filters.Categories);
        Assert.NotEmpty(filters.Products);
    }

    [Fact]
    public async Task Repeated_query_parameters_filter_by_multiple_clients()
    {
        var clients = (await Filters()).Clients.Take(2).Select(c => c.Id).ToArray();

        var rows = await Stock($"?clientIds={clients[0]}&clientIds={clients[1]}");

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Contains(r.ClientId, clients));
        Assert.Equal(2, rows.Select(r => r.ClientId).Distinct().Count());
    }

    [Fact]
    public async Task Put_updates_quantity_visible_in_grid()
    {
        var row = (await Stock()).First();

        var response = await _client.PutAsJsonAsync($"/api/stock/{row.ClientId}/{row.ProductId}", new { quantity = row.Quantity + 1 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = Assert.Single(await Stock($"?clientIds={row.ClientId}&productIds={row.ProductId}"));
        Assert.Equal(row.Quantity + 1, updated.Quantity);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"qty": 7}""")]
    [InlineData("""{"quantity": null}""")]
    [InlineData("""{"quantity": -1}""")]
    public async Task Put_with_missing_or_invalid_quantity_is_rejected_and_keeps_stock(string body)
    {
        var row = (await Stock()).First();

        var response = await _client.PutAsync(
            $"/api/stock/{row.ClientId}/{row.ProductId}",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = Assert.Single(await Stock($"?clientIds={row.ClientId}&productIds={row.ProductId}"));
        Assert.Equal(row.Quantity, unchanged.Quantity);
    }

    [Fact]
    public async Task Put_for_product_outside_client_assortment_returns_400_with_czech_detail()
    {
        var filters = await Filters();
        var (clientId, productId) = await FindPairOutsideAssortment(filters);

        var response = await _client.PutAsJsonAsync($"/api/stock/{clientId}/{productId}", new { quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("nepatří", problem!.Detail);
    }

    [Fact]
    public async Task Put_for_unknown_client_returns_404()
    {
        var response = await _client.PutAsJsonAsync("/api/stock/99999/1", new { quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Index_page_is_served()
    {
        var html = await _client.GetStringAsync("/");

        Assert.Contains("Správa skladu", html);
    }

    private async Task<(int ClientId, int ProductId)> FindPairOutsideAssortment(FilterOptions filters)
    {
        foreach (var client in filters.Clients)
        {
            var stocked = (await Stock($"?clientIds={client.Id}")).Select(r => r.ProductId).ToHashSet();
            var missing = filters.Products.FirstOrDefault(p => !stocked.Contains(p.Id));
            if (missing is not null)
                return (client.Id, missing.Id);
        }
        throw new InvalidOperationException("Seed neobsahuje produkt mimo sortiment žádného klienta.");
    }
}
