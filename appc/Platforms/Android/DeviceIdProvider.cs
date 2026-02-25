#if ANDROID
using global::Android.Content;
using global::Android.Provider;

namespace appc.Platforms.Android;

public static class DeviceIdProvider
{
    const string Key = "device_id";

    public static string Get(Context ctx)
    {
        // اگر قبلاً ذخیره شده، همونو بده (پایدار)
        var saved = Preferences.Get(Key, "");
        if (!string.IsNullOrWhiteSpace(saved))
            return saved;

        // AndroidId (معمولاً پایدار)
        var androidId = Settings.Secure.GetString(ctx.ContentResolver, Settings.Secure.AndroidId);
        if (string.IsNullOrWhiteSpace(androidId))
            androidId = Guid.NewGuid().ToString("N");

        // prefix برای اینکه معلوم باشه از اندرویده
        var id = "and-" + androidId;

        Preferences.Set(Key, id);
        return id;
    }
}
#endif