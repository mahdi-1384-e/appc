using System.Net.Http.Json;

namespace appc.Sync;

public class ConfigDto
{
    public bool enabled { get; set; }
    public int interval_seconds { get; set; }
    public string window_start { get; set; } = "00:00";
    public string window_end { get; set; } = "23:59";
    public int upload_every_minutes { get; set; }
    public string updated_at_utc { get; set; } = "";
}

public class ConfigClient
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // 🔴 دقیقاً دامنه خودت
    private const string Url = "https://mytestdomaini.ir/api2/config.php";

    public async Task<ConfigDto?> FetchAsync(CancellationToken ct)
    {
        try
        {
            var res = await _http.GetAsync(Url, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                AppLog.Write($"Config HTTP {(int)res.StatusCode}: {body}");
                return null;
            }

            // برای اینکه ببینیم چی برگشته
            AppLog.Write("Config raw: " + body);

            // Parse JSON
            var cfg = System.Text.Json.JsonSerializer.Deserialize<ConfigDto>(body);
            return cfg;
        }
        catch (Exception ex)
        {
            AppLog.Write("Config exception: " + ex.Message);
            return null;
        }
    }
}