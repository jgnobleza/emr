using MedRec.Mobile.Pages;
using MedRec.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace MedRec.Mobile;

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

        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<MobileLocalStore>();
        builder.Services.AddSingleton<MobileSyncClient>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<PatientsPage>();
        builder.Services.AddTransient<CheckupsPage>();
        builder.Services.AddTransient<SyncPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        AppServices.Current = app.Services;
        return app;
    }
}
