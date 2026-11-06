using System.Net;
using System.Text.RegularExpressions;

namespace MedorSklad.Tests;

/// <summary>Statický frontend: stránka a všechny ES moduly se servírují a nic se nenačítá z cizích serverů.</summary>
public sealed partial class SiteTests : IClassFixture<TestApp>
{
    private readonly HttpClient _client;

    public SiteTests(TestApp app) => _client = app.CreateClient();

    [Fact]
    public async Task Index_and_js_modules_are_served()
    {
        var html = await _client.GetStringAsync("/");
        Assert.Contains("Správa skladu", html);
        Assert.Contains("""<script type="module" src="js/main.js">""", html);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/css/app.css")).StatusCode);

        // Projdeme importy od main.js, aby rozbitá cesta k modulu neprošla bez povšimnutí.
        var pending = new Queue<string>(["main.js"]);
        var seen = new HashSet<string>();
        while (pending.TryDequeue(out var module))
        {
            if (!seen.Add(module)) continue;
            var response = await _client.GetAsync($"/js/{module}");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Modul js/{module} se nenačetl ({response.StatusCode}).");
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
            foreach (Match import in ImportPattern().Matches(await response.Content.ReadAsStringAsync()))
                pending.Enqueue(import.Groups["file"].Value);
        }
        Assert.Contains("editing.js", seen);
    }

    [Fact]
    public async Task Page_has_no_external_script_or_stylesheet_urls()
    {
        var html = await _client.GetStringAsync("/");

        var urls = SrcOrHrefPattern().Matches(html).Select(m => m.Groups["url"].Value).ToList();

        Assert.NotEmpty(urls);
        Assert.DoesNotContain(urls, url => url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("//"));
    }

    [GeneratedRegex(@"from\s+""\./(?<file>[\w-]+\.js)""")]
    private static partial Regex ImportPattern();

    [GeneratedRegex("""(?:src|href)\s*=\s*["'](?<url>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SrcOrHrefPattern();
}
