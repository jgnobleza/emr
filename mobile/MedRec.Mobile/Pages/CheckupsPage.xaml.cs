using System.Collections.ObjectModel;
using MedRec.Mobile.Models;
using MedRec.Mobile.Services;

namespace MedRec.Mobile.Pages;

public partial class CheckupsPage : ContentPage
{
    private readonly MobileLocalStore localStore;
    private readonly ObservableCollection<MobilePatient> patients = [];
    private readonly ObservableCollection<MobileCheckup> checkups = [];

    public CheckupsPage()
    {
        InitializeComponent();
        localStore = AppServices.GetRequiredService<MobileLocalStore>();
        PatientPicker.ItemsSource = patients;
        CheckupsList.ItemsSource = checkups;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void SaveCheckupClicked(object sender, EventArgs e)
    {
        if (PatientPicker.SelectedItem is not MobilePatient patient)
        {
            await DisplayAlertAsync("Patient required", "Choose a patient before saving a checkup.", "OK");
            return;
        }

        await localStore.AddCheckupAsync(new MobileCheckup
        {
            PatientClientUid = patient.ClientUid,
            PatientName = patient.FullName,
            ChiefComplaint = ComplaintEntry.Text?.Trim() ?? string.Empty,
            Diagnosis = DiagnosisEntry.Text?.Trim() ?? string.Empty,
            Notes = NotesEditor.Text?.Trim() ?? string.Empty,
            DoctorName = "Doctor",
            VisitDate = DateTime.Now
        });

        ComplaintEntry.Text = string.Empty;
        DiagnosisEntry.Text = string.Empty;
        NotesEditor.Text = string.Empty;
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

        if (PatientPicker.SelectedIndex < 0 && patients.Count > 0)
        {
            PatientPicker.SelectedIndex = 0;
        }

        checkups.Clear();
        foreach (var checkup in snapshot.Checkups)
        {
            checkups.Add(checkup);
        }
    }
}
