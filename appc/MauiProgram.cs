using appc.Data;
using appc.Sync;
using Microsoft.Extensions.Logging;
using appc.Sync;
namespace appc
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton(sp =>
            {
                var dbPath = Path.Combine(FileSystem.AppDataDirectory, "track.db3");
                var db = new TrackDb(dbPath);
                Task.Run(db.InitAsync); // init یک‌بار
                return db;
            });
            builder.Services.AddSingleton<BatchSyncer>();
            builder.Services.AddSingleton<ConfigClient>();
            return builder.Build();
        }
    }
}
