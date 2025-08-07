using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

public class IndexModel : PageModel
{
    [BindProperty] public string PlanId { get; set; }
    [BindProperty] public string SuiteId { get; set; }

    public List<SelectListItem> PlanOptions { get; set; } = new();
    public List<SelectListItem> SuiteOptions { get; set; } = new();

    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Total { get; set; }
    public double PassPercentage { get; set; }

    private const string org = "itron";
    private const string project = "RnD";
    private const string pat = "x";
    private const string apiVersion = "7.1-preview.1";

    public async Task OnGetAsync()
    {
        await LoadTestPlansAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadTestPlansAsync();

        if (!string.IsNullOrEmpty(PlanId))
        {
            await LoadSuitesAsync(PlanId);
        }

        if (!string.IsNullOrEmpty(PlanId) && !string.IsNullOrEmpty(SuiteId))
        {
            await ProcessSuiteAsync(PlanId, SuiteId);
        }

        return Page();
    }

    private async Task LoadTestPlansAsync()
    {
        string url = $"https://dev.azure.com/{org}/{project}/_apis/test/plans";

        using var client = CreateClient();
        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                PlanOptions.Add(new SelectListItem
                {
                    Value = item.GetProperty("id").GetInt32().ToString(),
                    Text = item.GetProperty("name").GetString()
                });
            }
        }
    }


    private async Task LoadSuitesAsync(string planId)
    {
        string url = $"https://dev.azure.com/{org}/{project}/_apis/test/plans/{planId}/suites";
        using var client = CreateClient();
        var response = await client.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                SuiteOptions.Add(new SelectListItem
                {
                    Value = item.GetProperty("id").GetInt32().ToString(),
                    Text = item.GetProperty("name").GetString()
                });
            }
        }
    }

    private async Task GetTestResultsAsync(string planId, string suiteId)
    {
        string url = $"https://dev.azure.com/{org}/{project}/_apis/test/plans/{planId}/suites/{suiteId}/points";
        using var client = CreateClient();
        var response = await client.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            var points = doc.RootElement.GetProperty("value");

            foreach (var point in points.EnumerateArray())
            {
                if (point.TryGetProperty("outcome", out var outcomeProp))
                {
                    string outcome = outcomeProp.GetString();
                    if (outcome == "Passed" || outcome == "Failed")
                    {
                        Total++;
                        if (outcome == "Passed") Passed++;
                        else Failed++;
                    }
                }
            }

            if (Total > 0)
                PassPercentage = Math.Round((Passed * 100.0) / Total, 2);
        }
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}")));
        return client;
    }

    private  async Task ProcessSuiteAsync(int planId, int suiteId)
    {
        // Use these counters at class level or pass them via ref
        int total = 0, passed = 0, failed = 0;

        using var client = CreateClient();

        async Task ProcessSuiteRecursive(int suiteId)
        {
            // 1. Get test points for this suite
            string pointsUrl = $"https://dev.azure.com/{org}/{project}/_apis/test/plans/{planId}/suites/{suiteId}/points?api-version=7.0";
            var pointsResponse = await client.GetAsync(pointsUrl);
            if (pointsResponse.IsSuccessStatusCode)
            {
                string json = await pointsResponse.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                var points = doc.RootElement.GetProperty("value");

                foreach (var point in points.EnumerateArray())
                {
                    if (point.TryGetProperty("outcome", out var outcomeProp))
                    {
                        string outcome = outcomeProp.GetString();
                        if (outcome == "Passed" || outcome == "Failed")
                        {
                            total++;
                            if (outcome == "Passed") passed++;
                            else failed++;
                        }
                    }
                }
            }

            // 2. Get child suites for this suite
            string suitesUrl = $"https://dev.azure.com/{org}/{project}/_apis/test/plans/{planId}/suites/{suiteId}/suites";
            var suitesResponse = await client.GetAsync(suitesUrl);
            if (suitesResponse.IsSuccessStatusCode)
            {
                string json = await suitesResponse.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                var suites = doc.RootElement.GetProperty("value");

                foreach (var suite in suites.EnumerateArray())
                {
                    if (suite.TryGetProperty("id", out var childSuiteIdProp) && int.TryParse(childSuiteIdProp.GetString(), out int childSuiteId))
                    {
                        // Recursively process each child suite
                        await ProcessSuiteRecursive(childSuiteId);
                    }
                }
            }
        }

        await ProcessSuiteRecursive(suiteId);

        if (total > 0)
        {
            double passPercentage = Math.Round((passed * 100.0) / total, 2);
            Console.WriteLine($"Total: {total}, Passed: {passed}, Failed: {failed}, Pass %: {passPercentage}");
        }
        else
        {
            Console.WriteLine("No test cases found.");
        }
    }

}
