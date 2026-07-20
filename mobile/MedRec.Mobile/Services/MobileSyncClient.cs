using MedRec.Mobile.Models;

namespace MedRec.Mobile.Services;

public sealed class MobileSyncClient
{
    public const string DefaultCloudBaseUrl = "https://emr-vwwl.onrender.com";

    private readonly HttpClient httpClient;

    public MobileSyncClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<MobileSyncResult> SyncAsync(MobileStoreSnapshot snapshot)
    {
        var baseUrl = Preferences.Get("CloudBaseUrl", DefaultCloudBaseUrl).TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return new MobileSyncResult
            {
                Success = false,
                Message = "Cloud URL is not valid."
            };
        }

        httpClient.BaseAddress = uri;

        try
        {
            using var response = await httpClient.GetAsync("/service-worker.js?health=mobile");
            if (!response.IsSuccessStatusCode)
            {
                return new MobileSyncResult
                {
                    Success = false,
                    Message = $"Cloud is reachable but returned {(int)response.StatusCode}."
                };
            }

            return new MobileSyncResult
            {
                Success = false,
                Message = "Mobile shell is online. The backend still needs mobile sync endpoints before records can upload.",
            };
        }
        catch (Exception ex)
        {
            return new MobileSyncResult
            {
                Success = false,
                Message = $"Could not reach cloud: {ex.Message}"
            };
        }
    }
}
