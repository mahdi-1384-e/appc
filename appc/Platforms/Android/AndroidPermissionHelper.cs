#if ANDROID
using System.Threading.Tasks;
using global::Android;
using global::Android.OS;
using global::AndroidX.Core.App;
using global::AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;

namespace appc.Platforms.Android;

public static class AndroidPermissionHelper
{
    public static Task EnsurePostNotificationsAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return Task.CompletedTask;

        var activity = Platform.CurrentActivity;
        if (activity == null)
            return Task.CompletedTask;

        var granted = ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications)
                      == global::Android.Content.PM.Permission.Granted;

        if (granted) return Task.CompletedTask;

        ActivityCompat.RequestPermissions(activity,
            new[] { Manifest.Permission.PostNotifications },
            9876);

        return Task.CompletedTask;
    }
}
#endif