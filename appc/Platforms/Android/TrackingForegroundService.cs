#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;

using appc.Data;

using global::Android.App;
using global::Android.Content;
using global::Android.Gms.Location;
using global::Android.OS;
using global::AndroidX.Core.App;

namespace appc.Platforms.Android;

[Service(
    Name = "com.lactiveyt.appc.TrackingForegroundService",
    Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync
)]
public class TrackingForegroundService : Service
{
    public const int NotificationId = 1001;
    public const string ChannelId = "tracking_min_channel";

    private CancellationTokenSource? _cts;
    private IFusedLocationProviderClient? _fused;
    private TrackDb? _db;

    private appc.Sync.BatchSyncer? _sync;
    private DateTime _nextUploadUtc = DateTime.MinValue;

    private appc.Sync.ConfigClient? _cfgClient;
    private DateTime _nextConfigFetchUtc = DateTime.MinValue;

    private string _deviceId = "";

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        AppLog.Write("FGS START");

        ServiceCompat.StartForeground(
            this,
            NotificationId,
            BuildNotification("Service running"),
            (int)global::Android.Content.PM.ForegroundService.TypeDataSync
        );

        _cts ??= new CancellationTokenSource();
        _fused ??= LocationServices.GetFusedLocationProviderClient(this);
        _db ??= MauiApplication.Current.Services.GetService<TrackDb>();
        _sync ??= MauiApplication.Current.Services.GetService<appc.Sync.BatchSyncer>();
        _cfgClient ??= MauiApplication.Current.Services.GetService<appc.Sync.ConfigClient>();

        _deviceId = DeviceIdProvider.Get(this);

        _ = LoopAsync(_cts.Token);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        AppLog.Write("FGS STOP");
        _cts?.Cancel();
        _cts = null;
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private async Task LoopAsync(CancellationToken ct)
    {
        AppLog.Write($"Loop started | deviceId={_deviceId}");

        while (!ct.IsCancellationRequested)
        {
            var intervalSeconds = appc.TrackingSettings.IntervalSeconds;

            try
            {
                // هر دور (با throttling داخلی) کانفیگ رو بگیر
                await TryFetchConfigAsync(ct);

                // اگر پنل disable کرده
                if (!appc.TrackingSettings.Enabled)
                {
                    AppLog.Write("Disabled by config ⛔");
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                    continue;
                }

                // ارسال batch طبق UploadEveryMinutes
                await TryUploadAsync(ct);

                // منطق بازه + لوکیشن
                if (!appc.TrackingSettings.IsNowInWindow())
                {
                    AppLog.Write("Outside window ⏸");
                }
                else if (!IsLocationServiceEnabled())
                {
                    AppLog.Write("Location OFF ❌");
                }
                else
                {
                    var loc = await GetOneShotAsync(timeoutSeconds: 8, ct);

                    if (loc == null)
                    {
                        AppLog.Write("Location: null (timeout/no fix)");
                    }
                    else
                    {
                        if (_db != null)
                        {
                            await _db.InsertAsync(new LocationPoint
                            {
                                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                Lat = loc.Latitude,
                                Lng = loc.Longitude,
                                Acc = loc.Accuracy
                            });

                            AppLog.Write("Saved ✅");
                        }
                        else
                        {
                            AppLog.Write("DB is null ❌");
                        }

                        AppLog.Write($"FUSED: {loc.Latitude:0.000000}, {loc.Longitude:0.000000} acc={loc.Accuracy:0.0}m");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Error: " + ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
        }

        AppLog.Write("Loop stopped");
    }

    private async Task TryUploadAsync(CancellationToken ct)
    {
        if (_sync == null) return;

        var uploadEveryMin = appc.TrackingSettings.UploadEveryMinutes;
        if (uploadEveryMin <= 0) uploadEveryMin = 5;

        if (DateTime.UtcNow < _nextUploadUtc) return;
        _nextUploadUtc = DateTime.UtcNow.AddMinutes(uploadEveryMin);

        try
        {
            var sent = await _sync.UploadOnceAsync(_deviceId);
            if (sent > 0) AppLog.Write($"Uploaded ✅ count={sent}");
            else AppLog.Write("Upload: nothing to send");
        }
        catch (Exception ex)
        {
            AppLog.Write("Upload error: " + ex.Message);
        }
    }

    private async Task TryFetchConfigAsync(CancellationToken ct)
    {
        if (_cfgClient == null) return;

        // هر 60 ثانیه یکبار
        if (DateTime.UtcNow < _nextConfigFetchUtc) return;
        _nextConfigFetchUtc = DateTime.UtcNow.AddSeconds(60);

        var cfg = await _cfgClient.FetchAsync(ct);
        if (cfg == null)
        {
            AppLog.Write("Config: fetch failed");
            return;
        }

        // اگر updated_at تغییر نکرده بود، دوباره اعمال نکن
        if (!string.IsNullOrWhiteSpace(cfg.updated_at_utc) &&
            cfg.updated_at_utc == appc.TrackingSettings.UpdatedAtUtcRaw)
            return;

        appc.TrackingSettings.Enabled = cfg.enabled;
        appc.TrackingSettings.IntervalSeconds = Math.Clamp(cfg.interval_seconds, 2, 3600);
        appc.TrackingSettings.WindowStart = cfg.window_start ?? "00:00";
        appc.TrackingSettings.WindowEnd = cfg.window_end ?? "23:59";
        appc.TrackingSettings.UploadEveryMinutes = Math.Clamp(cfg.upload_every_minutes, 1, 1440);
        appc.TrackingSettings.UpdatedAtUtcRaw = cfg.updated_at_utc ?? "";

        AppLog.Write($"Config applied ✅ interval={appc.TrackingSettings.IntervalSeconds}s upload={appc.TrackingSettings.UploadEveryMinutes}m window={appc.TrackingSettings.WindowStart}-{appc.TrackingSettings.WindowEnd} enabled={appc.TrackingSettings.Enabled}");
    }

    private Task<global::Android.Locations.Location?> GetOneShotAsync(int timeoutSeconds, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<global::Android.Locations.Location?>();

        var req = LocationRequest.Create();
        req.SetInterval(1000);
        req.SetFastestInterval(500);
        req.SetPriority(LocationRequest.PriorityHighAccuracy);
        req.SetNumUpdates(1);

        LocationCallback? cb = null;
        cb = new OneShotCallback(loc =>
        {
            try { _fused?.RemoveLocationUpdates(cb); } catch { }
            tcs.TrySetResult(loc);
        });

        _fused!.RequestLocationUpdates(req, cb, Looper.MainLooper);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct);
            if (!tcs.Task.IsCompleted)
            {
                try { _fused?.RemoveLocationUpdates(cb); } catch { }
                tcs.TrySetResult(null);
            }
        }, ct);

        return tcs.Task;
    }

    private sealed class OneShotCallback : LocationCallback
    {
        private readonly Action<global::Android.Locations.Location?> _onLoc;
        public OneShotCallback(Action<global::Android.Locations.Location?> onLoc) => _onLoc = onLoc;
        public override void OnLocationResult(LocationResult result) => _onLoc(result.LastLocation);
    }

    private bool IsLocationServiceEnabled()
    {
        var lm = (global::Android.Locations.LocationManager?)GetSystemService(LocationService);
        if (lm == null) return false;

        bool gps = false, net = false;
        try { gps = lm.IsProviderEnabled(global::Android.Locations.LocationManager.GpsProvider); } catch { }
        try { net = lm.IsProviderEnabled(global::Android.Locations.LocationManager.NetworkProvider); } catch { }

        return gps || net;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var mgr = (NotificationManager?)GetSystemService(NotificationService);
        if (mgr == null) return;

        var channel = new NotificationChannel(ChannelId, "Tracking", NotificationImportance.Min)
        {
            Description = "Minimal tracking notification"
        };

        channel.SetSound(null, null);
        channel.EnableVibration(false);
        channel.EnableLights(false);
        channel.SetShowBadge(false);

        mgr.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string text)
    {
        int iconId = Resources.GetIdentifier("ic_tracking", "drawable", PackageName);
        if (iconId == 0) iconId = global::Android.Resource.Drawable.IcDialogInfo;

        var deleteIntent = new Intent(this, typeof(NotificationDismissedReceiver));
        deleteIntent.SetAction(NotificationDismissedReceiver.ActionNotificationDismissed);

        var pendingDelete = PendingIntent.GetBroadcast(
            this, 2001, deleteIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
        );

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Tracking")
            .SetContentText(text)
            .SetSmallIcon(iconId)
            .SetOngoing(true)
            .SetAutoCancel(false)
            .SetOnlyAlertOnce(true)
            .SetCategory(NotificationCompat.CategoryService)
            .SetPriority((int)NotificationPriority.Min)
            .SetDeleteIntent(pendingDelete);

        var n = builder.Build();
        n.Flags |= NotificationFlags.OngoingEvent;
        n.Flags |= NotificationFlags.NoClear;
        return n;
    }
}
#endif