#if ANDROID
using global::Android.Content;
using global::Android.OS;

namespace appc.Platforms.Android;

public static class ServiceController
{
    public static void Start()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(TrackingForegroundService));

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public static void Stop()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(TrackingForegroundService));
        context.StopService(intent);
    }
}
#endif