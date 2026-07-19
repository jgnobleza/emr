using System.Text.Json;
using medrec.Data;
using medrec.Models;
using Npgsql;

namespace medrec.Services;

public sealed class OfflineSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PostgresConnectionFactory _connections;
    private readonly EmrRepository _repository;
    private readonly IWebHostEnvironment _environment;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaReady;

    public OfflineSyncService(PostgresConnectionFactory connections, EmrRepository repository, IWebHostEnvironment environment)
    {
        _connections = connections;
        _repository = repository;
        _environment = environment;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaReady) return;
        await _schemaGate.WaitAsync();
        try
        {
            if (_schemaReady) return;
            await using var connection = _connections.CreateConnection();
            await connection.OpenAsync();
            foreach (var table in new[] { "patients", "clinical_records", "lab_results", "prescriptions" })
            {
                if (!await ColumnExistsAsync(connection, table, "client_uid"))
                {
                    await ExecuteAsync(connection, $"ALTER TABLE {table} ADD COLUMN client_uid CHAR(36) NULL;");
                    await ExecuteAsync(connection, $"UPDATE {table} SET client_uid = gen_random_uuid()::text WHERE client_uid IS NULL OR client_uid = '';");
                    await ExecuteAsync(connection, $"ALTER TABLE {table} ALTER COLUMN client_uid SET NOT NULL;");
                }

                if (!await IndexExistsAsync(connection, table, $"ux_{table}_client_uid"))
                {
                    await ExecuteAsync(connection, $"CREATE UNIQUE INDEX IF NOT EXISTS ux_{table}_client_uid ON {table} (client_uid);");
                }
            }

            var clinicalRecordVitalsChanged = false;
            clinicalRecordVitalsChanged |= await AddColumnIfMissingAsync(connection, "clinical_records", "height_cm", "DECIMAL(6,2) NULL", "visit_date");
            clinicalRecordVitalsChanged |= await AddColumnIfMissingAsync(connection, "clinical_records", "weight_kg", "DECIMAL(6,2) NULL", "height_cm");
            clinicalRecordVitalsChanged |= await AddColumnIfMissingAsync(connection, "clinical_records", "blood_pressure", "VARCHAR(40) NOT NULL DEFAULT ''", "weight_kg");
            clinicalRecordVitalsChanged |= await AddColumnIfMissingAsync(connection, "clinical_records", "fetal_heart_rate", "VARCHAR(40) NOT NULL DEFAULT ''", "blood_pressure");
            clinicalRecordVitalsChanged |= await AddColumnIfMissingAsync(connection, "clinical_records", "temperature_c", "DECIMAL(5,2) NULL", "fetal_heart_rate");

            if (clinicalRecordVitalsChanged)
            {
                await ExecuteAsync(connection, """
                    UPDATE clinical_records cr
                    SET height_cm = p.height_cm,
                        weight_kg = p.weight_kg,
                        blood_pressure = p.blood_pressure,
                        fetal_heart_rate = p.fetal_heart_tone
                    FROM patients p
                    WHERE cr.height_cm IS NULL
                      AND cr.weight_kg IS NULL
                      AND blood_pressure = ''
                      AND fetal_heart_rate = ''
                      AND p.id = cr.patient_id;
                    """);
            }
            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    public async Task<OfflineSyncSnapshot> GetSnapshotAsync(AppUser user)
    {
        await EnsureSchemaAsync();
        return await _repository.GetOfflineSnapshotAsync(user);
    }

    public async Task<OfflineSyncResult> ApplyBatchAsync(OfflineSyncBatch batch, AppUser user)
    {
        await EnsureSchemaAsync();
        if (batch.Operations.Count > 200) throw new InvalidOperationException("A sync batch cannot exceed 200 operations.");
        var result = new OfflineSyncResult();
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var operation in batch.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.Id) || string.IsNullOrWhiteSpace(operation.EntityUid)) continue;
            try
            {
                var conflict = operation.Type switch
                {
                    "patient.upsert" => await UpsertPatientAsync(connection, transaction, operation),
                    "patient.archive" => await ArchivePatientAsync(connection, transaction, operation),
                    "record.upsert" => await UpsertRecordAsync(connection, transaction, operation, user),
                    "lab.upsert" => await UpsertLabAsync(connection, transaction, operation),
                    "prescription.upsert" => await UpsertPrescriptionAsync(connection, transaction, operation, user),
                    "profile.upsert" => await UpsertProfileAsync(connection, transaction, operation, user),
                    _ => "Unsupported offline operation."
                };

                if (conflict is null) result.AcceptedOperationIds.Add(operation.Id);
                else result.Conflicts.Add(new OfflineSyncConflict { OperationId = operation.Id, EntityUid = operation.EntityUid, Message = conflict });
            }
            catch (Exception ex)
            {
                result.Conflicts.Add(new OfflineSyncConflict { OperationId = operation.Id, EntityUid = operation.EntityUid, Message = ex.Message });
            }
        }

        await transaction.CommitAsync();
        result.Snapshot = await GetSnapshotAsync(user);
        return result;
    }

    private async Task<string?> UpsertPatientAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfflineSyncOperation operation)
    {
        var patient = operation.Payload.Deserialize<Patient>(JsonOptions) ?? throw new InvalidOperationException("Patient payload is invalid.");
        patient.ClientUid = operation.EntityUid;
        if (operation.Payload.TryGetProperty("photoBase64", out var encodedPhoto) && !string.IsNullOrWhiteSpace(encodedPhoto.GetString()))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encodedPhoto.GetString()!); }
            catch (FormatException) { throw new InvalidOperationException("The offline patient image is invalid."); }
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Patient image must be 5 MB or smaller.");
            var extension = SignatureExtension(bytes) ?? throw new InvalidOperationException("Patient image must be PNG, JPEG, GIF, or WebP.");
            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "patients");
            Directory.CreateDirectory(uploadRoot);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await File.WriteAllBytesAsync(Path.Combine(uploadRoot, fileName), bytes);
            patient.PhotoUrl = $"/uploads/patients/{fileName}";
        }
        var existing = await EntityStateAsync(connection, transaction, "patients", operation.EntityUid);
        if (HasConflict(existing, operation.BaseUpdatedAt)) return "The patient was changed on the server after this device's offline copy.";

        const string sql = """
            INSERT INTO patients
              (client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company,
               email, partner_name, partner_contact_number, referred_by, age_of_menarche, menopause_age,
               previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount, menstrual_pattern,
               sexually_active, contraception_method, height_cm, weight_kg, blood_pressure, fetal_heart_tone,
               last_menstrual_period, photo_url, sync_status, archived_at)
            VALUES
              (@uid, @number, @name, @age, @address, @sex, @civil, @contact, @occupation, @company,
               @email, @partner, @partnerContact, @referred, @menarche, @menopause,
               @pmp, @cycle, @duration, @amount, @pattern, @active, @contraception, @height, @weight, @bp, @fht,
               @lmp, @photo, 'Synced', @archived)
            ON CONFLICT (client_uid) DO UPDATE SET
              full_name=EXCLUDED.full_name, age=EXCLUDED.age, address=EXCLUDED.address, sex=EXCLUDED.sex,
              civil_status=EXCLUDED.civil_status, contact_number=EXCLUDED.contact_number, occupation=EXCLUDED.occupation,
              company=EXCLUDED.company, email=EXCLUDED.email, partner_name=EXCLUDED.partner_name,
              partner_contact_number=EXCLUDED.partner_contact_number, referred_by=EXCLUDED.referred_by,
              age_of_menarche=EXCLUDED.age_of_menarche, menopause_age=EXCLUDED.menopause_age,
              previous_menstrual_period=EXCLUDED.previous_menstrual_period, period_cycle_days=EXCLUDED.period_cycle_days,
              period_duration_days=EXCLUDED.period_duration_days, menstrual_amount=EXCLUDED.menstrual_amount,
              menstrual_pattern=EXCLUDED.menstrual_pattern, sexually_active=EXCLUDED.sexually_active,
              contraception_method=EXCLUDED.contraception_method, height_cm=EXCLUDED.height_cm, weight_kg=EXCLUDED.weight_kg,
              blood_pressure=EXCLUDED.blood_pressure, fetal_heart_tone=EXCLUDED.fetal_heart_tone,
              last_menstrual_period=EXCLUDED.last_menstrual_period, photo_url=COALESCE(EXCLUDED.photo_url, patients.photo_url),
              archived_at=EXCLUDED.archived_at, sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@uid", patient.ClientUid);
        command.Parameters.AddWithValue("@number", string.IsNullOrWhiteSpace(patient.PatientNumber) ? $"OFF-{DateTime.UtcNow:yyyyMMddHHmmssfff}" : patient.PatientNumber);
        command.Parameters.AddWithValue("@name", patient.FullName.Trim());
        command.Parameters.AddWithValue("@age", patient.Age);
        command.Parameters.AddWithValue("@address", patient.Address ?? "");
        command.Parameters.AddWithValue("@sex", patient.Sex ?? "");
        command.Parameters.AddWithValue("@civil", patient.CivilStatus ?? "");
        command.Parameters.AddWithValue("@contact", patient.ContactNumber ?? "");
        command.Parameters.AddWithValue("@occupation", patient.Occupation ?? "");
        command.Parameters.AddWithValue("@company", patient.Company ?? "");
        command.Parameters.AddWithValue("@email", patient.Email ?? "");
        command.Parameters.AddWithValue("@partner", patient.PartnerName ?? "");
        command.Parameters.AddWithValue("@partnerContact", patient.PartnerContactNumber ?? "");
        command.Parameters.AddWithValue("@referred", patient.ReferredBy ?? "");
        command.Parameters.AddWithValue("@menarche", (object?)patient.AgeOfMenarche ?? DBNull.Value);
        command.Parameters.AddWithValue("@menopause", (object?)patient.MenopauseAge ?? DBNull.Value);
        command.Parameters.AddWithValue("@pmp", DbDate(patient.PreviousMenstrualPeriod));
        command.Parameters.AddWithValue("@cycle", (object?)patient.PeriodCycleDays ?? DBNull.Value);
        command.Parameters.AddWithValue("@duration", (object?)patient.PeriodDurationDays ?? DBNull.Value);
        command.Parameters.AddWithValue("@amount", patient.MenstrualAmount ?? "");
        command.Parameters.AddWithValue("@pattern", patient.MenstrualPattern ?? "");
        command.Parameters.AddWithValue("@active", (object?)patient.SexuallyActive ?? DBNull.Value);
        command.Parameters.AddWithValue("@contraception", patient.ContraceptionMethod ?? "");
        command.Parameters.AddWithValue("@height", (object?)patient.HeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@weight", (object?)patient.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("@bp", patient.BloodPressure ?? "");
        command.Parameters.AddWithValue("@fht", patient.FetalHeartTone ?? "");
        command.Parameters.AddWithValue("@lmp", DbDate(patient.LastMenstrualPeriod));
        command.Parameters.AddWithValue("@photo", string.IsNullOrWhiteSpace(patient.PhotoUrl) ? DBNull.Value : patient.PhotoUrl);
        command.Parameters.AddWithValue("@archived", (object?)patient.ArchivedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        await QueueServerSyncAsync(connection, transaction, "Patient", await EntityIdAsync(connection, transaction, "patients", patient.ClientUid), existing is null ? "Create" : "Update");
        return null;
    }

    private static async Task<string?> ArchivePatientAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfflineSyncOperation operation)
    {
        var existing = await EntityStateAsync(connection, transaction, "patients", operation.EntityUid);
        if (existing is null) return "Patient no longer exists on the server.";
        if (HasConflict(existing, operation.BaseUpdatedAt)) return "The patient was changed on the server after this device's offline copy.";
        await using var command = new NpgsqlCommand("UPDATE patients SET archived_at=CURRENT_TIMESTAMP, sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP WHERE client_uid=@uid;", connection, transaction);
        command.Parameters.AddWithValue("@uid", operation.EntityUid);
        await command.ExecuteNonQueryAsync();
        await QueueServerSyncAsync(connection, transaction, "Patient", existing.Value.Id, "Delete");
        return null;
    }

    private static async Task<string?> UpsertRecordAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfflineSyncOperation operation, AppUser user)
    {
        var record = operation.Payload.Deserialize<OfflineRecordPayload>(JsonOptions) ?? throw new InvalidOperationException("Checkup payload is invalid.");
        var patientId = await EntityIdAsync(connection, transaction, "patients", record.PatientClientUid);
        var existing = await EntityStateAsync(connection, transaction, "clinical_records", operation.EntityUid);
        if (HasConflict(existing, operation.BaseUpdatedAt)) return "The checkup was changed on the server after this device's offline copy.";
        const string sql = """
            INSERT INTO clinical_records
              (client_uid, patient_id, doctor_id, visit_date, height_cm, weight_kg, blood_pressure, fetal_heart_rate, temperature_c,
               chief_complaint, diagnosis, notes, doctor_name, sync_status, last_synced_at)
            VALUES (@uid, @patientId, @doctorId, @visitDate, @height, @weight, @bp, @fhr, @temperature,
                    @complaint, @diagnosis, @notes, @doctorName, 'Synced', CURRENT_TIMESTAMP)
            ON CONFLICT (client_uid) DO UPDATE SET visit_date=EXCLUDED.visit_date, chief_complaint=EXCLUDED.chief_complaint,
              height_cm=EXCLUDED.height_cm, weight_kg=EXCLUDED.weight_kg, blood_pressure=EXCLUDED.blood_pressure,
              fetal_heart_rate=EXCLUDED.fetal_heart_rate, temperature_c=EXCLUDED.temperature_c,
              diagnosis=EXCLUDED.diagnosis, notes=EXCLUDED.notes, doctor_id=EXCLUDED.doctor_id, doctor_name=EXCLUDED.doctor_name,
              sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@uid", operation.EntityUid);
        command.Parameters.AddWithValue("@patientId", patientId);
        command.Parameters.AddWithValue("@doctorId", user.Id);
        command.Parameters.AddWithValue("@visitDate", record.VisitDate);
        command.Parameters.AddWithValue("@height", (object?)record.HeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@weight", (object?)record.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("@bp", record.BloodPressure ?? "");
        command.Parameters.AddWithValue("@fhr", record.FetalHeartRate ?? "");
        command.Parameters.AddWithValue("@temperature", (object?)record.TemperatureC ?? DBNull.Value);
        command.Parameters.AddWithValue("@complaint", record.ChiefComplaint.Trim());
        command.Parameters.AddWithValue("@diagnosis", record.Diagnosis ?? "");
        command.Parameters.AddWithValue("@notes", record.Notes ?? "");
        command.Parameters.AddWithValue("@doctorName", user.FullName);
        await command.ExecuteNonQueryAsync();
        await QueueServerSyncAsync(connection, transaction, "ClinicalRecord", await EntityIdAsync(connection, transaction, "clinical_records", operation.EntityUid), existing is null ? "Create" : "Update");
        return null;
    }

    private async Task<string?> UpsertLabAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfflineSyncOperation operation)
    {
        var lab = operation.Payload.Deserialize<OfflineLabPayload>(JsonOptions) ?? throw new InvalidOperationException("Lab payload is invalid.");
        var patientId = await EntityIdAsync(connection, transaction, "patients", lab.PatientClientUid);
        int? recordId = string.IsNullOrWhiteSpace(lab.RecordClientUid) ? null : await EntityIdAsync(connection, transaction, "clinical_records", lab.RecordClientUid);
        var existing = await EntityStateAsync(connection, transaction, "lab_results", operation.EntityUid);
        if (HasConflict(existing, operation.BaseUpdatedAt)) return "The lab result was changed on the server after this device's offline copy.";

        var fileUrl = lab.FileUrl;
        if (!string.IsNullOrWhiteSpace(lab.FileBase64))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(lab.FileBase64); }
            catch (FormatException) { throw new InvalidOperationException("The offline lab attachment is invalid."); }
            if (bytes.Length == 0 || bytes.Length > 15 * 1024 * 1024) throw new InvalidOperationException("Lab attachment must be between 1 byte and 15 MB.");
            var extension = LabFileExtension(bytes) ?? throw new InvalidOperationException("Lab attachment must be PDF, PNG, JPEG, GIF, or WebP.");
            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "labs");
            Directory.CreateDirectory(uploadRoot);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await File.WriteAllBytesAsync(Path.Combine(uploadRoot, fileName), bytes);
            fileUrl = $"/uploads/labs/{fileName}";
        }
        if (string.IsNullOrWhiteSpace(fileUrl)) throw new InvalidOperationException("A lab PDF is required.");

        const string sql = """
            INSERT INTO lab_results
              (client_uid, patient_id, clinical_record_id, test_name, requested_date, result_date, status, file_url, notes, sync_status)
            VALUES (@uid,@patientId,@recordId,@testName,@requestedDate,@resultDate,@status,@fileUrl,@notes,'Synced')
            ON CONFLICT (client_uid) DO UPDATE SET clinical_record_id=EXCLUDED.clinical_record_id, test_name=EXCLUDED.test_name,
              requested_date=EXCLUDED.requested_date, result_date=EXCLUDED.result_date, status=EXCLUDED.status,
              file_url=EXCLUDED.file_url, notes=EXCLUDED.notes, sync_status='Synced';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@uid", operation.EntityUid);
        command.Parameters.AddWithValue("@patientId", patientId);
        command.Parameters.AddWithValue("@recordId", (object?)recordId ?? DBNull.Value);
        command.Parameters.AddWithValue("@testName", lab.TestName.Trim());
        command.Parameters.AddWithValue("@requestedDate", lab.RequestedDate);
        command.Parameters.AddWithValue("@resultDate", lab.ResultDate);
        command.Parameters.AddWithValue("@status", new[] { "Uploaded", "Reviewed", "Archived" }.Contains(lab.Status) ? lab.Status : "Uploaded");
        command.Parameters.AddWithValue("@fileUrl", fileUrl);
        command.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(lab.Notes) ? DBNull.Value : lab.Notes.Trim());
        await command.ExecuteNonQueryAsync();
        await QueueServerSyncAsync(connection, transaction, "LabResult", await EntityIdAsync(connection, transaction, "lab_results", operation.EntityUid), existing is null ? "Create" : "Update");
        return null;
    }

    private async Task<string?> UpsertProfileAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfflineSyncOperation operation, AppUser user)
    {
        var profile = operation.Payload.Deserialize<OfflineProfilePayload>(JsonOptions) ?? throw new InvalidOperationException("Doctor profile payload is invalid.");
        if (string.IsNullOrWhiteSpace(profile.FullName)) throw new InvalidOperationException("Doctor name is required.");
        var signatureUrl = user.SignatureUrl;
        if (!string.IsNullOrWhiteSpace(profile.SignatureBase64))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(profile.SignatureBase64); }
            catch (FormatException) { throw new InvalidOperationException("The offline signature image is invalid."); }
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Signature image must be 5 MB or smaller.");
            var extension = SignatureExtension(bytes) ?? throw new InvalidOperationException("Signature must be PNG, JPEG, GIF, or WebP.");
            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "signatures");
            Directory.CreateDirectory(uploadRoot);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await File.WriteAllBytesAsync(Path.Combine(uploadRoot, fileName), bytes);
            signatureUrl = $"/uploads/signatures/{fileName}";
        }
        await using var command = new NpgsqlCommand("UPDATE users SET full_name=@name, specialty=@specialty, license_number=@license, contact_number=@contact, signature_url=@signature WHERE id=@id AND is_active=TRUE;", connection, transaction);
        command.Parameters.AddWithValue("@id", user.Id);
        command.Parameters.AddWithValue("@name", profile.FullName.Trim());
        command.Parameters.AddWithValue("@specialty", profile.Specialty?.Trim() ?? "");
        command.Parameters.AddWithValue("@license", profile.LicenseNumber?.Trim() ?? "");
        command.Parameters.AddWithValue("@contact", profile.ContactNumber?.Trim() ?? "");
        command.Parameters.AddWithValue("@signature", (object?)signatureUrl ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        user.FullName = profile.FullName.Trim(); user.Specialty = profile.Specialty?.Trim() ?? ""; user.LicenseNumber = profile.LicenseNumber?.Trim() ?? ""; user.ContactNumber = profile.ContactNumber?.Trim() ?? ""; user.SignatureUrl = signatureUrl;
        return null;
    }

    private static string? SignatureExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        if (bytes.Length >= 6 && (System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a")) return ".gif";
        if (bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        return null;
    }

    private static string? LabFileExtension(byte[] bytes)
    {
        if (bytes.Length >= 5 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F' && bytes[4] == (byte)'-') return ".pdf";
        return SignatureExtension(bytes);
    }

    private static async Task<string?> UpsertPrescriptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfflineSyncOperation operation, AppUser user)
    {
        var prescription = operation.Payload.Deserialize<OfflinePrescriptionPayload>(JsonOptions) ?? throw new InvalidOperationException("Prescription payload is invalid.");
        if (prescription.Items.Count == 0) throw new InvalidOperationException("Prescription requires at least one medication.");
        var patientId = await EntityIdAsync(connection, transaction, "patients", prescription.PatientClientUid);
        int? recordId = string.IsNullOrWhiteSpace(prescription.RecordClientUid) ? null : await EntityIdAsync(connection, transaction, "clinical_records", prescription.RecordClientUid);
        var existing = await EntityStateAsync(connection, transaction, "prescriptions", operation.EntityUid);
        if (HasConflict(existing, operation.BaseUpdatedAt)) return "The prescription was changed on the server after this device's offline copy.";
        var first = prescription.Items[0];
        const string sql = """
            INSERT INTO prescriptions
              (client_uid, patient_id, clinical_record_id, issued_at, medication, dosage, frequency, duration, instructions, prescriber, sync_status)
            VALUES (@uid, @patientId, @recordId, @issuedAt, @medication, @dosage, @frequency, @duration, @instructions, @prescriber, 'Synced')
            ON CONFLICT (client_uid) DO UPDATE SET clinical_record_id=EXCLUDED.clinical_record_id, issued_at=EXCLUDED.issued_at,
              medication=EXCLUDED.medication, dosage=EXCLUDED.dosage, frequency=EXCLUDED.frequency, duration=EXCLUDED.duration,
              instructions=EXCLUDED.instructions, prescriber=EXCLUDED.prescriber, sync_status='Synced';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@uid", operation.EntityUid);
        command.Parameters.AddWithValue("@patientId", patientId);
        command.Parameters.AddWithValue("@recordId", (object?)recordId ?? DBNull.Value);
        command.Parameters.AddWithValue("@issuedAt", prescription.IssuedAt == default ? DateTime.Now : prescription.IssuedAt);
        command.Parameters.AddWithValue("@medication", first.Medication);
        command.Parameters.AddWithValue("@dosage", first.Dosage);
        command.Parameters.AddWithValue("@frequency", first.Frequency);
        command.Parameters.AddWithValue("@duration", first.Duration);
        command.Parameters.AddWithValue("@instructions", prescription.Instructions ?? "");
        command.Parameters.AddWithValue("@prescriber", user.FullName);
        await command.ExecuteNonQueryAsync();
        var prescriptionId = await EntityIdAsync(connection, transaction, "prescriptions", operation.EntityUid);
        await using (var delete = new NpgsqlCommand("DELETE FROM prescription_items WHERE prescription_id=@id;", connection, transaction))
        {
            delete.Parameters.AddWithValue("@id", prescriptionId);
            await delete.ExecuteNonQueryAsync();
        }
        for (var i = 0; i < prescription.Items.Count; i++)
        {
            var item = prescription.Items[i];
            await using var insert = new NpgsqlCommand("INSERT INTO prescription_items (prescription_id, medication, dosage, frequency, duration, sort_order) VALUES (@id,@med,@dose,@freq,@duration,@sort);", connection, transaction);
            insert.Parameters.AddWithValue("@id", prescriptionId);
            insert.Parameters.AddWithValue("@med", item.Medication);
            insert.Parameters.AddWithValue("@dose", item.Dosage);
            insert.Parameters.AddWithValue("@freq", item.Frequency);
            insert.Parameters.AddWithValue("@duration", item.Duration);
            insert.Parameters.AddWithValue("@sort", i);
            await insert.ExecuteNonQueryAsync();
        }
        await QueueServerSyncAsync(connection, transaction, "Prescription", prescriptionId, existing is null ? "Create" : "Update");
        return null;
    }

    private static bool HasConflict((int Id, DateTime UpdatedAt)? existing, DateTime? baseUpdatedAt) =>
        existing.HasValue && baseUpdatedAt.HasValue && existing.Value.UpdatedAt.ToUniversalTime() > baseUpdatedAt.Value.ToUniversalTime().AddSeconds(1);

    private static async Task<(int Id, DateTime UpdatedAt)?> EntityStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string uid)
    {
        await using var command = new NpgsqlCommand($"SELECT id, updated_at FROM {table} WHERE client_uid=@uid LIMIT 1;", connection, transaction);
        command.Parameters.AddWithValue("@uid", uid);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetInt32("id"), reader.GetDateTime("updated_at")) : null;
    }

    private static async Task<int> EntityIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string uid)
    {
        await using var command = new NpgsqlCommand($"SELECT id FROM {table} WHERE client_uid=@uid LIMIT 1;", connection, transaction);
        command.Parameters.AddWithValue("@uid", uid);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("A related offline record could not be resolved."));
    }

    private static async Task QueueServerSyncAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string type, int id, string operation)
    {
        await using var command = new NpgsqlCommand("INSERT INTO sync_queue (entity_type, entity_id, operation, status) VALUES (@type,@id,@operation,'Synced');", connection, transaction);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@operation", operation);
        await command.ExecuteNonQueryAsync();
    }

    private static object DbDate(DateOnly? value) => value.HasValue ? value.Value : DBNull.Value;

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection connection, string table, string column)
    {
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name=@table AND column_name=@column;", connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string table, string index)
    {
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() AND tablename=@table AND indexname=@index;", connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> AddColumnIfMissingAsync(
        NpgsqlConnection connection,
        string table,
        string column,
        string definition,
        string afterColumn)
    {
        if (await ColumnExistsAsync(connection, table, column))
        {
            return false;
        }

        await ExecuteAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        return true;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}



