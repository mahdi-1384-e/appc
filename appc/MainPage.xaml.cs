using System.Text;
using appc.Data;
using Microsoft.Maui.ApplicationModel;
using Android.Content;
namespace appc;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
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


    private async void OnStartClicked(object sender, EventArgs e)
    {
#if ANDROID
        // ۱. چک کردن اجازه لوکیشن (همان کد قبلی شما)
        var loc = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (loc != PermissionStatus.Granted)
            loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

        if (loc != PermissionStatus.Granted) return;

        // ۲. باز کردن تنظیمات Accessibility
        // اینجا از Context استفاده می‌کنیم تا خطا ندهد
        var context = global::Android.App.Application.Context;
        var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);

        // رفع خطای ActivityFlags: از global::Android.Content.ActivityFlags استفاده کن
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);

        context.StartActivity(intent);
#endif
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        // در سیستم جدید، کاربر باید خودش دستی از تنظیمات گوشی سرویس رو آف کنه
        // یا می‌تونی اینجا پیام بدی که برو توی تنظیمات خاموشش کن
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