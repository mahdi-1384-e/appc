#if ANDROID
using Android.App;
using global::Android.Content;

namespace appc.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { ActionNotificationDismissed })]
public class NotificationDismissedReceiver : BroadcastReceiver
{
    public const string ActionNotificationDismissed = "appc.NOTIF_DISMISSED";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;

        // اگر نوتیف را کشید و حذف کرد، سرویس را دوباره استارت کن
        ServiceController.Start();
    }
}
#endif