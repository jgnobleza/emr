using System.Text.Json;
using MedRec.Mobile.Models;

namespace MedRec.Mobile.Services;

public sealed class MobileLocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);

    private string StorePath => Path.Combine(FileSystem.AppDataDirectory, "medrec-mobile.json");

    public async Task<MobileStoreSnapshot> GetSnapshotAsync()
    {
        await gate.WaitAsync();
        try
        {
            return await LoadUnsafeAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MobilePatient> AddPatientAsync(MobilePatient patient)
    {
        await gate.WaitAsync();
        try
        {
            var snapshot = await LoadUnsafeAsync();
            patient.ClientUid = string.IsNullOrWhiteSpace(patient.ClientUid) ? Guid.NewGuid().ToString("N") : patient.ClientUid;
            patient.UpdatedAt = DateTime.Now;
            patient.SyncStatus = "Pending";
            snapshot.Patients.Insert(0, patient);
            await SaveUnsafeAsync(snapshot);
            return patient;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MobileCheckup> AddCheckupAsync(MobileCheckup checkup)
    {
        await gate.WaitAsync();
        try
        {
            var snapshot = await LoadUnsafeAsync();
            checkup.ClientUid = string.IsNullOrWhiteSpace(checkup.ClientUid) ? Guid.NewGuid().ToString("N") : checkup.ClientUid;
            checkup.UpdatedAt = DateTime.Now;
            checkup.SyncStatus = "Pending";
            snapshot.Checkups.Insert(0, checkup);
            await SaveUnsafeAsync(snapshot);
            return checkup;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkSyncedAsync(DateTime syncedAt)
    {
        await gate.WaitAsync();
        try
        {
            var snapshot = await LoadUnsafeAsync();
            foreach (var patient in snapshot.Patients)
            {
                patient.SyncStatus = "Synced";
            }

            foreach (var checkup in snapshot.Checkups)
            {
                checkup.SyncStatus = "Synced";
            }

            snapshot.LastSyncAt = syncedAt;
            await SaveUnsafeAsync(snapshot);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MobileStoreSnapshot> LoadUnsafeAsync()
    {
        if (!File.Exists(StorePath))
        {
            return new MobileStoreSnapshot();
        }

        await using var stream = File.OpenRead(StorePath);
        return await JsonSerializer.DeserializeAsync<MobileStoreSnapshot>(stream, JsonOptions) ?? new MobileStoreSnapshot();
    }

    private async Task SaveUnsafeAsync(MobileStoreSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await using var stream = File.Create(StorePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
    }
}
