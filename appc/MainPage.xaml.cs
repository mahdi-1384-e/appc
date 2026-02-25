using System.Text;
using appc.Data;
using Microsoft.Maui.ApplicationModel;

namespace appc;

public partial class MainPage : ContentPage
{
    private readonly StringBuilder _sb = new();
    private const int MaxLines = 80;

    public MainPage()
    {
        InitializeComponent();
        AppLog.Message += OnLog;
    }

    protected override void OnDisappearing()
    {
        AppLog.Message -= OnLog;
        base.OnDisappearing();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // نمایش تنظیمات فعلی (هر 1 ثانیه)
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            // اگر تو XAML یک Label به اسم CfgLabel اضافه کرده‌ای
            // <Label x:Name="CfgLabel" ... />
            if (CfgLabel != null)
            {
                CfgLabel.Text =
                    $"Enabled={TrackingSettings.Enabled} | Interval={TrackingSettings.IntervalSeconds}s | Upload={TrackingSettings.UploadEveryMinutes}m | Window={TrackingSettings.WindowStart}-{TrackingSettings.WindowEnd}";
            }
            return true;
        });
    }

    private void OnLog(string line)
    {
        // لاگ‌ها از thread سرویس میاد، باید روی UI thread ست کنیم
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _sb.AppendLine(line);

            var lines = _sb.ToString().Split('\n');
            if (lines.Length > MaxLines)
            {
                _sb.Clear();
                foreach (var l in lines.Skip(lines.Length - MaxLines))
                    _sb.AppendLine(l);
            }

            LogLabel.Text = _sb.ToString();
        });
    }

    private async void OnStartClicked(object sender, EventArgs e)
    {
#if ANDROID
        // 1) Location permission
        var loc = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (loc != PermissionStatus.Granted)
            loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

        if (loc != PermissionStatus.Granted)
        {
            await DisplayAlert("اجازه لازم است", "برای گرفتن لوکیشن باید اجازه Location داده شود.", "باشه");
            return;
        }

        // 2) Android 13+ Notification permission (POST_NOTIFICATIONS)
        await appc.Platforms.Android.AndroidPermissionHelper.EnsurePostNotificationsAsync();

        appc.Platforms.Android.ServiceController.Start();
#endif
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
#if ANDROID
        appc.Platforms.Android.ServiceController.Stop();
#endif
    }

    private async void OnShowPointsClicked(object sender, EventArgs e)
    {
        var db = MauiApplication.Current.Services.GetService<TrackDb>();
        if (db == null)
        {
            await DisplayAlert("DB", "DB not ready", "OK");
            return;
        }

        var items = await db.GetLastAsync(20);
        AppLog.Write($"Last points: {items.Count}");

        foreach (var p in items)
            AppLog.Write($"{p.Id} | {p.Lat:0.000000},{p.Lng:0.000000} acc={p.Acc:0.0} t={p.TimestampUtc}");
    }
}