#if ANDROID
using global::Android;
using global::Android.OS;

namespace appc.Platforms.Android;

public class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? new[] { (Manifest.Permission.PostNotifications, true) }
            : Array.Empty<(string, bool)>();
}
#endif