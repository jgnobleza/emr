using MedRec.Mobile.Services;

namespace MedRec.Mobile.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly MobileLocalStore localStore;

    public DashboardPage()
    {
        InitializeComponent();
        localStore = AppServices.GetRequiredService<MobileLocalStore>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void RefreshClicked(object sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var snapshot = await localStore.GetSnapshotAsync();
        PatientsCountLabel.Text = snapshot.Patients.Count.ToString();
        CheckupsCountLabel.Text = snapshot.Checkups.Count.ToString();
        SyncLabel.Text = snapshot.LastSyncAt is null
            ? "No sync yet"
            : $"Last synced {snapshot.LastSyncAt:g}";
    }
}
