using appc.Data;

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

        // رفرش شدن لحظه‌ای اطلاعات روی صفحه
        Dispatcher.StartTimer(TimeSpan.FromSeconds(2), () =>
        {
            if (CfgLabel != null)
            {
                bool isInWindow = TrackingSettings.IsNowInWindow();
                CfgLabel.Text = $"Status: {(TrackingSettings.Enabled ? "ACTIVE" : "DISABLED")}\n" +
                                $"Interval: {TrackingSettings.IntervalSeconds}s\n" +
                                $"Window: {TrackingSettings.WindowStart} to {TrackingSettings.WindowEnd}\n" +
                                $"In Window: {(isInWindow ? "Yes" : "No")}";
            }
            return true;
        });
    }
}