using System.Text;

namespace appc;

public static class AppLog
{
    public static event Action<string>? Message;

    public static void Write(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss} | {text}";
        MainThread.BeginInvokeOnMainThread(() => Message?.Invoke(line));
    }
}