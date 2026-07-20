using MedRec.Mobile.Services;

namespace MedRec.Mobile.Pages;

public partial class SyncPage : ContentPage
{
    private readonly MobileLocalStore localStore;
    private readonly MobileSyncClient syncClient;

    public SyncPage()
    {
        InitializeComponent();
        localStore = AppServices.GetRequiredService<MobileLocalStore>();
        syncClient = AppServices.GetRequiredService<MobileSyncClient>();
        CloudUrlEntry.Text = Preferences.Get("CloudBaseUrl", MobileSyncClient.DefaultCloudBaseUrl);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshPendingAsync();
    }

    private async void SaveCloudUrlClicked(object sender, EventArgs e)
    {
        Preferences.Set("CloudBaseUrl", CloudUrlEntry.Text?.Trim() ?? MobileSyncClient.DefaultCloudBaseUrl);
        StatusLabel.Text = "Cloud URL saved.";
        await RefreshPendingAsync();
    }

    private async void ManualSyncClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "Checking cloud...";
        var snapshot = await localStore.GetSnapshotAsync();
        var result = await syncClient.SyncAsync(snapshot);
        StatusLabel.Text = result.Message;

        if (result.Success && result.SyncedAt is DateTime syncedAt)
        {
            await localStore.MarkSyncedAsync(syncedAt);
        }

        await RefreshPendingAsync();
    }

    private async Task RefreshPendingAsync()
    {
        var snapshot = await localStore.GetSnapshotAsync();
        var pending = snapshot.Patients.Count(p => p.SyncStatus != "Synced")
            + snapshot.Checkups.Count(c => c.SyncStatus != "Synced");
        PendingLabel.Text = $"{pending} pending changes";
    }
}
