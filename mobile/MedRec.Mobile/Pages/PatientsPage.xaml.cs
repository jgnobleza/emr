using System.Collections.ObjectModel;
using MedRec.Mobile.Models;
using MedRec.Mobile.Services;

namespace MedRec.Mobile.Pages;

public partial class PatientsPage : ContentPage
{
    private readonly MobileLocalStore localStore;
    private readonly ObservableCollection<MobilePatient> patients = [];

    public PatientsPage()
    {
        InitializeComponent();
        localStore = AppServices.GetRequiredService<MobileLocalStore>();
        PatientsList.ItemsSource = patients;
        SexPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void SavePatientClicked(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("Patient required", "Enter the patient's full name.", "OK");
            return;
        }

        _ = int.TryParse(AgeEntry.Text, out var age);
        await localStore.AddPatientAsync(new MobilePatient
        {
            FullName = name,
            Age = age,
            Sex = SexPicker.SelectedItem?.ToString() ?? "Female",
            ContactNumber = ContactEntry.Text?.Trim() ?? string.Empty,
            Address = AddressEditor.Text?.Trim() ?? string.Empty
        });

        NameEntry.Text = string.Empty;
        AgeEntry.Text = string.Empty;
        ContactEntry.Text = string.Empty;
        AddressEditor.Text = string.Empty;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var snapshot = await localStore.GetSnapshotAsync();
        patients.Clear();
        foreach (var patient in snapshot.Patients)
        {
            patients.Add(patient);
        }
    }
}
