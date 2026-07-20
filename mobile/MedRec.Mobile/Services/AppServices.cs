using Microsoft.Extensions.DependencyInjection;

namespace MedRec.Mobile.Services;

public static class AppServices
{
    public static IServiceProvider? Current { get; set; }

    public static T GetRequiredService<T>() where T : notnull
    {
        if (Current is null)
        {
            throw new InvalidOperationException("Application services are not ready.");
        }

        return Current.GetRequiredService<T>();
    }
}
