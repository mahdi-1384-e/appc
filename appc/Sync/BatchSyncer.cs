using System.Net.Http.Json;
using appc.Data;

namespace appc.Sync;

public class BatchSyncer
{
    private readonly TrackDb _db;
    private readonly HttpClient _http;

    // ✅ آدرس سرور توی یک جا
    private const string BaseUrl = "https://mytestdomaini.ir//api2";

    public BatchSyncer(TrackDb db)
    {
        _db = db;
        _http = new HttpClient();
    }

    public async Task<int> UploadOnceAsync(string deviceId, int take = 200)
    {
        var items = await _db.GetUnsentAsync(take);
        if (items.Count == 0) return 0;

        var payload = new
        {
            device_id = deviceId,
            points = items.Select(p => new
            {
                client_uid = p.ClientUid,
                timestamp_utc = DateTimeOffset.FromUnixTimeSeconds(p.TimestampUtc).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                lat = p.Lat,
                lng = p.Lng,
                accuracy_m = p.Acc,
                speed_mps = p.SpeedMps,
                bearing_deg = p.BearingDeg,
                altitude_m = p.AltitudeM,
                is_mock = p.IsMock
            }).ToList()
        };

        var res = await _http.PostAsJsonAsync($"{BaseUrl}/batch.php", payload);
        if (!res.IsSuccessStatusCode) return 0;

        var ids = items.Select(x => x.Id).ToArray();
        await _db.MarkSentAsync(ids);

        return items.Count;
    }
}