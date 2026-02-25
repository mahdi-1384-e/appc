using SQLite;

namespace appc.Data;

public class LocationPoint
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string ClientUid { get; set; } = Guid.NewGuid().ToString("N");

    public long TimestampUtc { get; set; }   // unix seconds
    public double Lat { get; set; }
    public double Lng { get; set; }
    public float Acc { get; set; }

    public float? SpeedMps { get; set; }
    public float? BearingDeg { get; set; }
    public float? AltitudeM { get; set; }
    public int IsMock { get; set; } // 0/1

    public int Sent { get; set; } // 0/1
}
public class TrackDb
{
    private readonly SQLiteAsyncConnection _db;

    public TrackDb(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitAsync()
    {
        await _db.CreateTableAsync<LocationPoint>();
    }

    public Task<int> InsertAsync(LocationPoint p) => _db.InsertAsync(p);

    public Task<List<LocationPoint>> GetLastAsync(int n) =>
        _db.Table<LocationPoint>().OrderByDescending(x => x.Id).Take(n).ToListAsync();
    public Task<List<LocationPoint>> GetUnsentAsync(int take) =>
        _db.Table<LocationPoint>()
            .Where(x => x.Sent == 0)
            .OrderBy(x => x.Id)
            .Take(take)
            .ToListAsync();

    public Task<int> MarkSentAsync(IEnumerable<int> ids) =>
        _db.ExecuteAsync($"UPDATE LocationPoint SET Sent=1 WHERE Id IN ({string.Join(",", ids)})");
}