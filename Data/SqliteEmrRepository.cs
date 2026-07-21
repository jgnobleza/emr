using medrec.Models;
using medrec.Services;
using medrec.ViewModels;
using Microsoft.Data.Sqlite;

namespace medrec.Data;

public sealed class SqliteEmrRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly CloudSyncService _cloudSync;

    public SqliteEmrRepository(SqliteConnectionFactory connectionFactory, CloudSyncService cloudSync)
    {
        _connectionFactory = connectionFactory;
        _cloudSync = cloudSync;
    }

    public async Task<DashboardViewModel> GetDashboardAsync()
    {
        await using var connection = await OpenConnectionAsync();
        return new DashboardViewModel
        {
            DatabaseConfigured = true,
            Patients = await GetPatientsAsync(connection),
            RecentRecords = await GetClinicalRecordsAsync(connection),
            LabResults = await GetLabResultsAsync(connection),
            Prescriptions = await GetPrescriptionsAsync(connection),
            PrescriptionLayout = await GetPrintLayoutAsync(connection, "Prescription"),
            DiagnosisLayout = await GetPrintLayoutAsync(connection, "Diagnosis"),
            SyncQueue = await GetSyncQueueAsync(connection),
            LastSyncLabel = await GetLastSyncLabelAsync(connection)
        };
    }

    public async Task<OfflineSyncSnapshot> GetOfflineSnapshotAsync(AppUser user)
    {
        await using var connection = await OpenConnectionAsync();
        return new OfflineSyncSnapshot
        {
            User = user,
            Patients = await GetPatientsAsync(connection),
            Records = await GetClinicalRecordsAsync(connection),
            LabResults = await GetLabResultsAsync(connection),
            Prescriptions = await GetPrescriptionsAsync(connection),
            PrescriptionLayout = await GetPrintLayoutAsync(connection, "Prescription"),
            DiagnosisLayout = await GetPrintLayoutAsync(connection, "Diagnosis")
        };
    }

    public async Task<int> CreatePatientAsync(PatientFormModel form, string? photoUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        const string sql = """
            INSERT INTO patients
              (client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company,
               email, partner_name, partner_contact_number, referred_by, age_of_menarche, menopause_age,
               previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount, menstrual_pattern,
               sexually_active, contraception_method, height_cm, weight_kg, blood_pressure, fetal_heart_tone,
               last_menstrual_period, photo_url, sync_status)
            VALUES
              (@clientUid, @patientNumber, @fullName, @age, @address, @sex, @civilStatus, @contactNumber, @occupation, @company,
               @email, @partnerName, @partnerContactNumber, @referredBy, @ageOfMenarche, @menopauseAge,
               @previousMenstrualPeriod, @periodCycleDays, @periodDurationDays, @menstrualAmount, @menstrualPattern,
               @sexuallyActive, @contraceptionMethod, @heightCm, @weightKg, @bloodPressure, @fetalHeartTone,
               @lastMenstrualPeriod, @photoUrl, 'Pending');
            SELECT last_insert_rowid();
            """;

        await using var command = new SqliteCommand(sql, connection, transaction);
        var clientUid = Guid.NewGuid().ToString();
        command.Parameters.AddWithValue("@clientUid", clientUid);
        command.Parameters.AddWithValue("@patientNumber", $"OB-{DateTime.Now:yyyyMMddHHmmssfff}");
        AddPatientParameters(command, form);
        AddNullable(command, "@photoUrl", photoUrl?.Trim());
        var patientId = Convert.ToInt32(await command.ExecuteScalarAsync());
        await AddSyncQueueItemAsync(connection, transaction, "Patient", patientId, clientUid, "Create");
        await transaction.CommitAsync();
        return patientId;
    }

    public async Task UpdatePatientAsync(PatientEditFormModel form, string? photoUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "patients", form.Id);
        const string sql = """
            UPDATE patients
            SET full_name = @fullName, age = @age, address = @address, sex = @sex, civil_status = @civilStatus,
                contact_number = @contactNumber, occupation = @occupation, company = @company, email = @email,
                partner_name = @partnerName, partner_contact_number = @partnerContactNumber, referred_by = @referredBy,
                age_of_menarche = @ageOfMenarche, menopause_age = @menopauseAge,
                previous_menstrual_period = @previousMenstrualPeriod, period_cycle_days = @periodCycleDays,
                period_duration_days = @periodDurationDays, menstrual_amount = @menstrualAmount,
                menstrual_pattern = @menstrualPattern, sexually_active = @sexuallyActive,
                contraception_method = @contraceptionMethod, height_cm = @heightCm, weight_kg = @weightKg,
                blood_pressure = @bloodPressure, fetal_heart_tone = @fetalHeartTone,
                last_menstrual_period = @lastMenstrualPeriod, photo_url = COALESCE(@photoUrl, photo_url),
                sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND archived_at IS NULL;
            """;

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", form.Id);
        AddPatientParameters(command, form);
        AddNullable(command, "@photoUrl", photoUrl?.Trim());
        await command.ExecuteNonQueryAsync();
        await AddSyncQueueItemAsync(connection, transaction, "Patient", form.Id, clientUid, "Update");
        await transaction.CommitAsync();
    }

    public async Task ArchivePatientAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "patients", id);
        await ExecuteAsync(connection, transaction, "UPDATE patients SET archived_at = CURRENT_TIMESTAMP, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP WHERE id = @id AND archived_at IS NULL;", ("@id", id));
        await AddSyncQueueItemAsync(connection, transaction, "Patient", id, clientUid, "Delete");
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<Patient>> GetArchivedPatientsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        const string sql = """
            SELECT id, client_uid, patient_number, full_name, age, address, sex, archived_at, updated_at, sync_status
            FROM patients
            WHERE archived_at IS NOT NULL
            ORDER BY archived_at DESC, full_name ASC;
            """;
        var patients = new List<Patient>();
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            patients.Add(new Patient
            {
                Id = Int(reader, "id"),
                ClientUid = Text(reader, "client_uid"),
                PatientNumber = Text(reader, "patient_number"),
                FullName = Text(reader, "full_name"),
                Age = Int(reader, "age"),
                Address = Text(reader, "address"),
                Sex = Text(reader, "sex"),
                ArchivedAt = DateTimeValue(reader, "archived_at"),
                LastUpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now,
                SyncStatus = Text(reader, "sync_status")
            });
        }

        return patients;
    }

    public async Task UnarchivePatientAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "patients", id);
        var updated = await ExecuteAsync(connection, transaction, "UPDATE patients SET archived_at = NULL, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP WHERE id = @id AND archived_at IS NOT NULL;", ("@id", id));
        if (updated == 0)
        {
            throw new InvalidOperationException("Archived patient was not found.");
        }

        await AddSyncQueueItemAsync(connection, transaction, "Patient", id, clientUid, "Update");
        await transaction.CommitAsync();
    }

    public async Task<int> CreateClinicalRecordAsync(RecordFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        const string sql = """
            INSERT INTO clinical_records
              (client_uid, patient_id, visit_date, height_cm, weight_kg, blood_pressure, fetal_heart_rate, temperature_c,
               chief_complaint, diagnosis, notes, doctor_name, sync_status)
            VALUES
              (@clientUid, @patientId, @visitDate, @heightCm, @weightKg, @bloodPressure, @fetalHeartRate, @temperatureC,
               @chiefComplaint, @diagnosis, @notes, @doctorName, 'Pending');
            SELECT last_insert_rowid();
            """;
        await using var command = new SqliteCommand(sql, connection, transaction);
        var clientUid = Guid.NewGuid().ToString();
        command.Parameters.AddWithValue("@clientUid", clientUid);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@visitDate", DbDateTime(form.VisitDate));
        AddClinicalRecordVitalsParameters(command, form);
        command.Parameters.AddWithValue("@chiefComplaint", form.ChiefComplaint.Trim());
        command.Parameters.AddWithValue("@diagnosis", form.Diagnosis?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@notes", form.Notes?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@doctorName", form.DoctorName.Trim());
        var recordId = Convert.ToInt32(await command.ExecuteScalarAsync());
        await AddSyncQueueItemAsync(connection, transaction, "ClinicalRecord", recordId, clientUid, "Create");
        await transaction.CommitAsync();
        return recordId;
    }

    public async Task UpdateDiagnosisAsync(DiagnosisFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "clinical_records", form.RecordId);
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE clinical_records SET diagnosis = @diagnosis, notes = @notes, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP WHERE id = @id;",
            ("@id", form.RecordId),
            ("@diagnosis", form.Diagnosis.Trim()),
            ("@notes", form.Notes.Trim()));
        await AddSyncQueueItemAsync(connection, transaction, "ClinicalRecord", form.RecordId, clientUid, "Update");
        await transaction.CommitAsync();
    }

    public async Task UpdateCheckupAsync(CheckupEditFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "clinical_records", form.RecordId);
        const string sql = """
            UPDATE clinical_records
            SET chief_complaint = @chiefComplaint, height_cm = @heightCm, weight_kg = @weightKg,
                blood_pressure = @bloodPressure, fetal_heart_rate = @fetalHeartRate, temperature_c = @temperatureC,
                sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP
            WHERE id = @id;
            """;
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", form.RecordId);
        command.Parameters.AddWithValue("@chiefComplaint", form.ChiefComplaint.Trim());
        AddClinicalRecordVitalsParameters(command, form);
        await command.ExecuteNonQueryAsync();
        await AddSyncQueueItemAsync(connection, transaction, "ClinicalRecord", form.RecordId, clientUid, "Update");
        await transaction.CommitAsync();
    }

    public async Task<int> CreateLabResultAsync(LabResultFormModel form, string fileUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        const string sql = """
            INSERT INTO lab_results
              (client_uid, patient_id, clinical_record_id, test_name, requested_date, result_date, status, file_url, notes, sync_status)
            VALUES
              (@clientUid, @patientId, @recordId, @testName, @requestedDate, @resultDate, 'Uploaded', @fileUrl, @notes, 'Pending');
            SELECT last_insert_rowid();
            """;
        await using var command = new SqliteCommand(sql, connection, transaction);
        var clientUid = Guid.NewGuid().ToString();
        command.Parameters.AddWithValue("@clientUid", clientUid);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        AddNullable(command, "@recordId", form.ClinicalRecordId);
        command.Parameters.AddWithValue("@testName", form.TestName.Trim());
        command.Parameters.AddWithValue("@requestedDate", DbDateTime(form.RequestedDate));
        command.Parameters.AddWithValue("@resultDate", DbDateTime(form.ResultDate));
        command.Parameters.AddWithValue("@fileUrl", fileUrl);
        AddNullable(command, "@notes", form.Notes?.Trim());
        var labId = Convert.ToInt32(await command.ExecuteScalarAsync());
        await AddSyncQueueItemAsync(connection, transaction, "LabResult", labId, clientUid, "Create");
        await transaction.CommitAsync();
        return labId;
    }

    public async Task UpdateLabResultAsync(LabEditFormModel form, string fileUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "lab_results", form.LabId);
        const string sql = """
            UPDATE lab_results
            SET clinical_record_id = @recordId, test_name = @testName, requested_date = @requestedDate,
                result_date = @resultDate, file_url = @fileUrl, notes = @notes,
                sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP
            WHERE id = @labId AND patient_id = @patientId
              AND EXISTS (SELECT 1 FROM clinical_records WHERE id = @recordId AND patient_id = @patientId);
            """;
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@labId", form.LabId);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@recordId", form.ClinicalRecordId!.Value);
        command.Parameters.AddWithValue("@testName", form.TestName.Trim());
        command.Parameters.AddWithValue("@requestedDate", DbDateTime(form.RequestedDate));
        command.Parameters.AddWithValue("@resultDate", DbDateTime(form.ResultDate));
        command.Parameters.AddWithValue("@fileUrl", fileUrl);
        AddNullable(command, "@notes", form.Notes?.Trim());
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new InvalidOperationException("Lab and check up must belong to the same patient.");
        }
        await AddSyncQueueItemAsync(connection, transaction, "LabResult", form.LabId, clientUid, "Update");
        await transaction.CommitAsync();
    }

    public async Task DeleteLabResultAsync(int labId, int patientId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var clientUid = await GetClientUidAsync(connection, transaction, "lab_results", labId);
        await AddSyncQueueItemAsync(connection, transaction, "LabResult", labId, clientUid, "Delete");
        var deleted = await ExecuteAsync(connection, transaction, "DELETE FROM lab_results WHERE id = @labId AND patient_id = @patientId;", ("@labId", labId), ("@patientId", patientId));
        if (deleted == 0)
        {
            throw new InvalidOperationException("Lab result was not found.");
        }
        await transaction.CommitAsync();
    }

    public async Task AttachLabToCheckUpAsync(LabAttachmentFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        const string validateSql = """
            SELECT COUNT(*)
            FROM lab_results l
            INNER JOIN clinical_records r ON r.id = @recordId
            WHERE l.id = @labId AND l.patient_id = @patientId AND r.patient_id = l.patient_id;
            """;
        await using (var validateCommand = new SqliteCommand(validateSql, connection, transaction))
        {
            validateCommand.Parameters.AddWithValue("@labId", form.LabId);
            validateCommand.Parameters.AddWithValue("@patientId", form.PatientId);
            validateCommand.Parameters.AddWithValue("@recordId", form.ClinicalRecordId!.Value);
            if (Convert.ToInt32(await validateCommand.ExecuteScalarAsync()) == 0)
            {
                throw new InvalidOperationException("Lab and check up must belong to the same patient.");
            }
        }

        var clientUid = await GetClientUidAsync(connection, transaction, "lab_results", form.LabId);
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE lab_results SET clinical_record_id = @recordId, requested_date = @requestedDate, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP WHERE id = @labId AND patient_id = @patientId;",
            ("@labId", form.LabId),
            ("@patientId", form.PatientId),
            ("@recordId", form.ClinicalRecordId!.Value),
            ("@requestedDate", DbDateTime(form.RequestedDate)));
        await AddSyncQueueItemAsync(connection, transaction, "LabResult", form.LabId, clientUid, "Update");
        await transaction.CommitAsync();
    }

    public async Task<int> CreatePrescriptionAsync(PrescriptionFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var items = form.NormalizedItems().ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Add at least one drug.");
        }

        var firstItem = items[0];
        const string sql = """
            INSERT INTO prescriptions
              (client_uid, patient_id, clinical_record_id, issued_at, medication, dosage, frequency, duration, instructions, prescriber, sync_status)
            VALUES
              (@clientUid, @patientId, @recordId, CURRENT_TIMESTAMP, @medication, @dosage, @frequency, @duration, @instructions, @prescriber, 'Pending');
            SELECT last_insert_rowid();
            """;
        await using var command = new SqliteCommand(sql, connection, transaction);
        var clientUid = Guid.NewGuid().ToString();
        command.Parameters.AddWithValue("@clientUid", clientUid);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        AddNullable(command, "@recordId", form.ClinicalRecordId);
        command.Parameters.AddWithValue("@medication", firstItem.Medication.Trim());
        command.Parameters.AddWithValue("@dosage", firstItem.Dosage.Trim());
        command.Parameters.AddWithValue("@frequency", firstItem.Frequency.Trim());
        command.Parameters.AddWithValue("@duration", firstItem.Duration.Trim());
        AddNullable(command, "@instructions", form.Instructions?.Trim());
        command.Parameters.AddWithValue("@prescriber", form.Prescriber.Trim());
        var prescriptionId = Convert.ToInt32(await command.ExecuteScalarAsync());
        for (var index = 0; index < items.Count; index++)
        {
            await AddPrescriptionItemAsync(connection, transaction, prescriptionId, items[index], index);
        }

        await AddSyncQueueItemAsync(connection, transaction, "Prescription", prescriptionId, clientUid, "Create");
        await transaction.CommitAsync();
        return prescriptionId;
    }

    public async Task RegisterPrescriptionPrintAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, null, "UPDATE prescriptions SET print_count = print_count + 1, updated_at = CURRENT_TIMESTAMP WHERE id = @id;", ("@id", id));
    }

    public async Task UpdatePrintLayoutAsync(PrintLayoutFormModel form, string documentType, string? logoUrl)
    {
        await using var connection = await OpenConnectionAsync();
        var normalizedDocumentType = PrintLayout.NormalizeDocumentType(documentType);
        var layoutId = PrintLayout.LayoutId(normalizedDocumentType);
        var layoutJson = PrintLayoutFormModel.SerializeBlocks(PrintLayoutFormModel.ParseBlocks(form.LayoutJson, normalizedDocumentType));
        const string sql = """
            INSERT INTO print_layouts
              (id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule, clinic_address,
               logo_url, logo_position, details_alignment, signatory_name, signatory_title, layout_json, sync_status, updated_at)
            VALUES
              (@id, @documentType, @documentTitle, @clinicName, @doctorName, @licenseNumber, @clinicSchedule, @clinicAddress,
               @logoUrl, @logoPosition, @detailsAlignment, @signatoryName, @signatoryTitle, @layoutJson, 'Pending', CURRENT_TIMESTAMP)
            ON CONFLICT(document_type) DO UPDATE SET
              document_title = excluded.document_title, clinic_name = excluded.clinic_name, doctor_name = excluded.doctor_name,
              license_number = excluded.license_number, clinic_schedule = excluded.clinic_schedule, clinic_address = excluded.clinic_address,
              logo_url = COALESCE(excluded.logo_url, print_layouts.logo_url), logo_position = excluded.logo_position,
              details_alignment = excluded.details_alignment, signatory_name = excluded.signatory_name,
              signatory_title = excluded.signatory_title, layout_json = excluded.layout_json,
              sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP;
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@id", layoutId);
        command.Parameters.AddWithValue("@documentType", normalizedDocumentType);
        command.Parameters.AddWithValue("@documentTitle", string.IsNullOrWhiteSpace(form.DocumentTitle) ? normalizedDocumentType : form.DocumentTitle.Trim());
        command.Parameters.AddWithValue("@clinicName", form.ClinicName.Trim());
        command.Parameters.AddWithValue("@doctorName", form.DoctorName.Trim());
        command.Parameters.AddWithValue("@licenseNumber", form.LicenseNumber?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@clinicSchedule", form.ClinicSchedule?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@clinicAddress", form.ClinicAddress?.Trim() ?? string.Empty);
        AddNullable(command, "@logoUrl", logoUrl?.Trim());
        command.Parameters.AddWithValue("@logoPosition", NormalizeLayoutOption(form.LogoPosition));
        command.Parameters.AddWithValue("@detailsAlignment", NormalizeLayoutOption(form.DetailsAlignment));
        command.Parameters.AddWithValue("@signatoryName", form.SignatoryName.Trim());
        command.Parameters.AddWithValue("@signatoryTitle", form.SignatoryTitle?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@layoutJson", layoutJson);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ManualSyncAsync()
    {
        return await _cloudSync.SyncAsync();
    }

    public async Task<string> CleanupRecordDataAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var normalizedComplaints = await ExecuteAsync(connection, transaction, "UPDATE clinical_records SET chief_complaint = 'Not specified' WHERE TRIM(chief_complaint) = '';");
        var normalizedDiagnoses = await ExecuteAsync(connection, transaction, "UPDATE clinical_records SET diagnosis = 'Pending diagnosis' WHERE TRIM(diagnosis) = '';");
        var normalizedDoctors = await ExecuteAsync(connection, transaction, "UPDATE clinical_records SET doctor_name = 'Doctor' WHERE TRIM(doctor_name) = '';");
        var fixedLabLinks = await ExecuteAsync(connection, transaction, """
            UPDATE lab_results
            SET clinical_record_id = NULL, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP
            WHERE clinical_record_id IS NOT NULL
              AND NOT EXISTS (
                SELECT 1 FROM clinical_records r
                WHERE r.id = lab_results.clinical_record_id AND r.patient_id = lab_results.patient_id
              );
            """);
        var fixedPrescriptionLinks = await ExecuteAsync(connection, transaction, """
            UPDATE prescriptions
            SET clinical_record_id = NULL, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP
            WHERE clinical_record_id IS NOT NULL
              AND NOT EXISTS (
                SELECT 1 FROM clinical_records r
                WHERE r.id = prescriptions.clinical_record_id AND r.patient_id = prescriptions.patient_id
              );
            """);
        var addedPrescriptionItems = await ExecuteAsync(connection, transaction, """
            INSERT INTO prescription_items (prescription_id, medication, dosage, frequency, duration, sort_order)
            SELECT p.id, p.medication, p.dosage, p.frequency, p.duration, 0
            FROM prescriptions p
            WHERE NOT EXISTS (SELECT 1 FROM prescription_items pi WHERE pi.prescription_id = p.id);
            """);
        await transaction.CommitAsync();
        return $"clinical_records normalized: complaints={normalizedComplaints}, diagnoses={normalizedDiagnoses}, doctors={normalizedDoctors}; links fixed: labs={fixedLabLinks}, prescriptions={fixedPrescriptionLinks}; prescription_items added={addedPrescriptionItems}.";
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static async Task<IReadOnlyList<Patient>> GetPatientsAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT id, client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number,
                   occupation, company, email, partner_name, partner_contact_number, referred_by, age_of_menarche,
                   menopause_age, previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount,
                   menstrual_pattern, sexually_active, contraception_method, height_cm, weight_kg, blood_pressure,
                   fetal_heart_tone, last_menstrual_period, photo_url,
                   (SELECT cr.chief_complaint FROM clinical_records cr WHERE cr.patient_id = p.id ORDER BY cr.visit_date DESC, cr.id DESC LIMIT 1) AS last_checkup_complaint,
                   updated_at, sync_status
            FROM patients p
            WHERE archived_at IS NULL
            ORDER BY full_name ASC;
            """;
        var patients = new List<Patient>();
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            patients.Add(ReadPatient(reader));
        }

        return patients;
    }

    private static async Task<IReadOnlyList<ClinicalRecord>> GetClinicalRecordsAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT r.id, r.client_uid, r.patient_id, p.full_name AS patient_name, p.address AS patient_address,
                   p.age AS patient_age, p.sex AS patient_sex, r.visit_date, r.height_cm, r.weight_kg,
                   r.blood_pressure, r.fetal_heart_rate, r.temperature_c, r.chief_complaint, r.diagnosis,
                   r.notes, r.doctor_name, r.sync_status, r.updated_at
            FROM clinical_records r
            INNER JOIN patients p ON p.id = r.patient_id
            WHERE p.archived_at IS NULL
            ORDER BY r.visit_date DESC, r.id DESC;
            """;
        var records = new List<ClinicalRecord>();
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new ClinicalRecord
            {
                Id = Int(reader, "id"),
                ClientUid = Text(reader, "client_uid"),
                PatientId = Int(reader, "patient_id"),
                PatientName = Text(reader, "patient_name"),
                PatientAddress = Text(reader, "patient_address"),
                PatientAge = Int(reader, "patient_age"),
                PatientSex = Text(reader, "patient_sex"),
                VisitDate = DateTimeValue(reader, "visit_date") ?? DateTime.Now,
                HeightCm = Decimal(reader, "height_cm"),
                WeightKg = Decimal(reader, "weight_kg"),
                BloodPressure = Text(reader, "blood_pressure"),
                FetalHeartRate = Text(reader, "fetal_heart_rate"),
                TemperatureC = Decimal(reader, "temperature_c"),
                ChiefComplaint = Text(reader, "chief_complaint"),
                Diagnosis = Text(reader, "diagnosis"),
                Notes = Text(reader, "notes"),
                DoctorName = Text(reader, "doctor_name"),
                SyncStatus = Text(reader, "sync_status"),
                UpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now
            });
        }

        return records;
    }

    private static async Task<IReadOnlyList<LabResult>> GetLabResultsAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT l.id, l.client_uid, l.patient_id, l.clinical_record_id, p.full_name AS patient_name,
                   l.test_name, l.requested_date, l.result_date, l.status, l.file_url, l.notes,
                   r.visit_date AS check_up_date, r.chief_complaint AS check_up_complaint, l.updated_at
            FROM lab_results l
            INNER JOIN patients p ON p.id = l.patient_id
            LEFT JOIN clinical_records r ON r.id = l.clinical_record_id
            WHERE p.archived_at IS NULL
            ORDER BY l.requested_date DESC, l.result_date DESC, l.id DESC;
            """;
        var labs = new List<LabResult>();
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            labs.Add(new LabResult
            {
                Id = Int(reader, "id"),
                ClientUid = Text(reader, "client_uid"),
                PatientId = Int(reader, "patient_id"),
                ClinicalRecordId = NullableInt(reader, "clinical_record_id"),
                PatientName = Text(reader, "patient_name"),
                TestName = Text(reader, "test_name"),
                RequestedDate = DateTimeValue(reader, "requested_date") ?? DateTime.Now,
                ResultDate = DateTimeValue(reader, "result_date") ?? DateTime.Now,
                CheckUpDate = DateTimeValue(reader, "check_up_date"),
                CheckUpComplaint = Text(reader, "check_up_complaint"),
                Status = Text(reader, "status"),
                FileUrl = Text(reader, "file_url"),
                Notes = Text(reader, "notes"),
                UpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now
            });
        }

        return labs;
    }

    private static async Task<IReadOnlyList<Prescription>> GetPrescriptionsAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT pr.id, pr.client_uid, pr.patient_id, pr.clinical_record_id, p.full_name AS patient_name,
                   p.address AS patient_address, p.age AS patient_age, p.sex AS patient_sex, pr.issued_at,
                   pr.medication, pr.dosage, pr.frequency, pr.duration, pr.instructions, pr.prescriber, pr.updated_at
            FROM prescriptions pr
            INNER JOIN patients p ON p.id = pr.patient_id
            WHERE p.archived_at IS NULL
            ORDER BY pr.issued_at DESC, pr.id DESC;
            """;
        var prescriptions = new List<Prescription>();
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            prescriptions.Add(new Prescription
            {
                Id = Int(reader, "id"),
                ClientUid = Text(reader, "client_uid"),
                PatientId = Int(reader, "patient_id"),
                ClinicalRecordId = NullableInt(reader, "clinical_record_id"),
                PatientName = Text(reader, "patient_name"),
                PatientAddress = Text(reader, "patient_address"),
                PatientAge = Int(reader, "patient_age"),
                PatientSex = Text(reader, "patient_sex"),
                IssuedAt = DateTimeValue(reader, "issued_at") ?? DateTime.Now,
                Medication = Text(reader, "medication"),
                Dosage = Text(reader, "dosage"),
                Frequency = Text(reader, "frequency"),
                Duration = Text(reader, "duration"),
                Instructions = Text(reader, "instructions"),
                Prescriber = Text(reader, "prescriber"),
                UpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now
            });
        }

        await LoadPrescriptionItemsAsync(connection, prescriptions);
        return prescriptions;
    }

    private static async Task LoadPrescriptionItemsAsync(SqliteConnection connection, IReadOnlyList<Prescription> prescriptions)
    {
        if (prescriptions.Count == 0)
        {
            return;
        }

        var ids = prescriptions.Select(item => item.Id).ToHashSet();
        var grouped = new Dictionary<int, List<PrescriptionItem>>();
        await using var command = new SqliteCommand("SELECT id, prescription_id, medication, dosage, frequency, duration, sort_order FROM prescription_items ORDER BY prescription_id, sort_order, id;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var prescriptionId = Int(reader, "prescription_id");
            if (!ids.Contains(prescriptionId))
            {
                continue;
            }

            if (!grouped.TryGetValue(prescriptionId, out var items))
            {
                items = [];
                grouped[prescriptionId] = items;
            }

            items.Add(new PrescriptionItem
            {
                Id = Int(reader, "id"),
                PrescriptionId = prescriptionId,
                Medication = Text(reader, "medication"),
                Dosage = Text(reader, "dosage"),
                Frequency = Text(reader, "frequency"),
                Duration = Text(reader, "duration"),
                SortOrder = Int(reader, "sort_order")
            });
        }

        foreach (var prescription in prescriptions)
        {
            prescription.Items = grouped.TryGetValue(prescription.Id, out var items) && items.Count > 0
                ? items
                :
                [
                    new()
                    {
                        PrescriptionId = prescription.Id,
                        Medication = prescription.Medication,
                        Dosage = prescription.Dosage,
                        Frequency = prescription.Frequency,
                        Duration = prescription.Duration
                    }
                ];
        }
    }

    private static async Task<PrintLayout> GetPrintLayoutAsync(SqliteConnection connection, string documentType)
    {
        var normalizedDocumentType = PrintLayout.NormalizeDocumentType(documentType);
        await using var command = new SqliteCommand("""
            SELECT id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule,
                   clinic_address, logo_url, logo_position, details_alignment, signatory_name, signatory_title,
                   layout_json, updated_at
            FROM print_layouts
            WHERE document_type = @documentType OR id = @id
            ORDER BY CASE WHEN document_type = @documentType THEN 0 ELSE 1 END
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("@documentType", normalizedDocumentType);
        command.Parameters.AddWithValue("@id", PrintLayout.LayoutId(normalizedDocumentType));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return PrintLayout.Default(normalizedDocumentType);
        }

        var layoutJson = TextOrNull(reader, "layout_json");
        return new PrintLayout
        {
            Id = Int(reader, "id"),
            DocumentType = PrintLayout.NormalizeDocumentType(Text(reader, "document_type")),
            DocumentTitle = Text(reader, "document_title"),
            ClinicName = Text(reader, "clinic_name"),
            DoctorName = Text(reader, "doctor_name"),
            LicenseNumber = Text(reader, "license_number"),
            ClinicSchedule = Text(reader, "clinic_schedule"),
            ClinicAddress = Text(reader, "clinic_address"),
            LogoUrl = TextOrNull(reader, "logo_url"),
            LogoPosition = Text(reader, "logo_position"),
            DetailsAlignment = Text(reader, "details_alignment"),
            SignatoryName = Text(reader, "signatory_name"),
            SignatoryTitle = Text(reader, "signatory_title"),
            Blocks = PrintLayoutFormModel.ParseBlocks(layoutJson, normalizedDocumentType),
            UpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now
        };
    }

    private static async Task<IReadOnlyList<SyncItem>> GetSyncQueueAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT id, entity_type, entity_uid, operation, updated_at, synced_at, status
            FROM sync_queue
            ORDER BY updated_at DESC
            LIMIT 10;
            """;
        var queue = new List<SyncItem>();
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            queue.Add(new SyncItem
            {
                Id = Int(reader, "id"),
                EntityType = Text(reader, "entity_type"),
                EntityId = int.TryParse(Text(reader, "entity_uid"), out var entityId) ? entityId : 0,
                Operation = Text(reader, "operation"),
                UpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now,
                SyncedAt = DateTimeValue(reader, "synced_at"),
                Status = Text(reader, "status")
            });
        }

        return queue;
    }

    private static async Task<string> GetLastSyncLabelAsync(SqliteConnection connection)
    {
        var result = await ScalarAsync(connection, null, "SELECT MAX(synced_at) FROM sync_queue WHERE synced_at IS NOT NULL;");
        if (result is null || result is DBNull || string.IsNullOrWhiteSpace(Convert.ToString(result)))
        {
            return "Local changes pending";
        }

        return DateTime.Parse(Convert.ToString(result)!).ToString("MMM d, h:mm tt");
    }

    private static async Task AddPrescriptionItemAsync(SqliteConnection connection, SqliteTransaction transaction, int prescriptionId, PrescriptionItemFormModel item, int sortOrder)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO prescription_items (prescription_id, medication, dosage, frequency, duration, sort_order) VALUES (@prescriptionId, @medication, @dosage, @frequency, @duration, @sortOrder);",
            ("@prescriptionId", prescriptionId),
            ("@medication", item.Medication.Trim()),
            ("@dosage", item.Dosage.Trim()),
            ("@frequency", item.Frequency.Trim()),
            ("@duration", item.Duration.Trim()),
            ("@sortOrder", sortOrder));
    }

    private static async Task AddSyncQueueItemAsync(SqliteConnection connection, SqliteTransaction transaction, string entityType, int entityId, string clientUid, string operation)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO sync_queue (entity_type, entity_uid, operation, status) VALUES (@entityType, @entityUid, @operation, 'Pending');",
            ("@entityType", entityType),
            ("@entityUid", string.IsNullOrWhiteSpace(clientUid) ? entityId.ToString() : clientUid),
            ("@operation", operation));
    }

    private static async Task<string> GetClientUidAsync(SqliteConnection connection, SqliteTransaction transaction, string table, int id)
    {
        await using var command = new SqliteCommand($"SELECT client_uid FROM {table} WHERE id = @id LIMIT 1;", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result) ?? id.ToString();
    }

    private static void AddPatientParameters(SqliteCommand command, PatientFormModel form)
    {
        command.Parameters.AddWithValue("@fullName", form.FullName.Trim());
        command.Parameters.AddWithValue("@age", form.Age);
        command.Parameters.AddWithValue("@address", form.Address?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@sex", form.Sex?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@civilStatus", form.CivilStatus?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@contactNumber", form.ContactNumber?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@occupation", form.Occupation?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@company", form.Company?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@email", form.Email?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@partnerName", form.PartnerName?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@partnerContactNumber", form.PartnerContactNumber?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@referredBy", form.ReferredBy?.Trim() ?? string.Empty);
        AddNullable(command, "@ageOfMenarche", form.AgeOfMenarche);
        AddNullable(command, "@menopauseAge", form.MenopauseAge);
        AddNullable(command, "@previousMenstrualPeriod", DbDate(form.PreviousMenstrualPeriod));
        AddNullable(command, "@periodCycleDays", form.PeriodCycleDays);
        AddNullable(command, "@periodDurationDays", form.PeriodDurationDays);
        command.Parameters.AddWithValue("@menstrualAmount", form.MenstrualAmount?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@menstrualPattern", form.MenstrualPattern?.Trim() ?? string.Empty);
        AddNullable(command, "@sexuallyActive", form.SexuallyActive.HasValue ? form.SexuallyActive.Value ? 1 : 0 : null);
        command.Parameters.AddWithValue("@contraceptionMethod", form.ContraceptionMethod?.Trim() ?? string.Empty);
        AddNullable(command, "@heightCm", form.HeightCm);
        AddNullable(command, "@weightKg", form.WeightKg);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartTone", form.FetalHeartTone?.Trim() ?? string.Empty);
        AddNullable(command, "@lastMenstrualPeriod", DbDate(form.LastMenstrualPeriod));
    }

    private static void AddPatientParameters(SqliteCommand command, PatientEditFormModel form)
    {
        command.Parameters.AddWithValue("@fullName", form.FullName.Trim());
        command.Parameters.AddWithValue("@age", form.Age);
        command.Parameters.AddWithValue("@address", form.Address?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@sex", form.Sex?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@civilStatus", form.CivilStatus?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@contactNumber", form.ContactNumber?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@occupation", form.Occupation?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@company", form.Company?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@email", form.Email?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@partnerName", form.PartnerName?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@partnerContactNumber", form.PartnerContactNumber?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@referredBy", form.ReferredBy?.Trim() ?? string.Empty);
        AddNullable(command, "@ageOfMenarche", form.AgeOfMenarche);
        AddNullable(command, "@menopauseAge", form.MenopauseAge);
        AddNullable(command, "@previousMenstrualPeriod", DbDate(form.PreviousMenstrualPeriod));
        AddNullable(command, "@periodCycleDays", form.PeriodCycleDays);
        AddNullable(command, "@periodDurationDays", form.PeriodDurationDays);
        command.Parameters.AddWithValue("@menstrualAmount", form.MenstrualAmount?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@menstrualPattern", form.MenstrualPattern?.Trim() ?? string.Empty);
        AddNullable(command, "@sexuallyActive", form.SexuallyActive.HasValue ? form.SexuallyActive.Value ? 1 : 0 : null);
        command.Parameters.AddWithValue("@contraceptionMethod", form.ContraceptionMethod?.Trim() ?? string.Empty);
        AddNullable(command, "@heightCm", form.HeightCm);
        AddNullable(command, "@weightKg", form.WeightKg);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartTone", form.FetalHeartTone?.Trim() ?? string.Empty);
        AddNullable(command, "@lastMenstrualPeriod", DbDate(form.LastMenstrualPeriod));
    }

    private static void AddClinicalRecordVitalsParameters(SqliteCommand command, RecordFormModel form)
    {
        AddNullable(command, "@heightCm", form.HeightCm);
        AddNullable(command, "@weightKg", form.WeightKg);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartRate", form.FetalHeartRate?.Trim() ?? string.Empty);
        AddNullable(command, "@temperatureC", form.TemperatureC);
    }

    private static void AddClinicalRecordVitalsParameters(SqliteCommand command, CheckupEditFormModel form)
    {
        AddNullable(command, "@heightCm", form.HeightCm);
        AddNullable(command, "@weightKg", form.WeightKg);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartRate", form.FetalHeartRate?.Trim() ?? string.Empty);
        AddNullable(command, "@temperatureC", form.TemperatureC);
    }

    private static Patient ReadPatient(SqliteDataReader reader) => new()
    {
        Id = Int(reader, "id"),
        ClientUid = Text(reader, "client_uid"),
        PatientNumber = Text(reader, "patient_number"),
        FullName = Text(reader, "full_name"),
        Age = Int(reader, "age"),
        Address = Text(reader, "address"),
        Sex = Text(reader, "sex"),
        CivilStatus = Text(reader, "civil_status"),
        ContactNumber = Text(reader, "contact_number"),
        Occupation = Text(reader, "occupation"),
        Company = Text(reader, "company"),
        Email = Text(reader, "email"),
        PartnerName = Text(reader, "partner_name"),
        PartnerContactNumber = Text(reader, "partner_contact_number"),
        ReferredBy = Text(reader, "referred_by"),
        AgeOfMenarche = NullableInt(reader, "age_of_menarche"),
        MenopauseAge = NullableInt(reader, "menopause_age"),
        PreviousMenstrualPeriod = DateOnlyValue(reader, "previous_menstrual_period"),
        PeriodCycleDays = NullableInt(reader, "period_cycle_days"),
        PeriodDurationDays = NullableInt(reader, "period_duration_days"),
        MenstrualAmount = Text(reader, "menstrual_amount"),
        MenstrualPattern = Text(reader, "menstrual_pattern"),
        SexuallyActive = Bool(reader, "sexually_active"),
        ContraceptionMethod = Text(reader, "contraception_method"),
        HeightCm = Decimal(reader, "height_cm"),
        WeightKg = Decimal(reader, "weight_kg"),
        BloodPressure = Text(reader, "blood_pressure"),
        FetalHeartTone = Text(reader, "fetal_heart_tone"),
        LastMenstrualPeriod = DateOnlyValue(reader, "last_menstrual_period"),
        PhotoUrl = TextOrNull(reader, "photo_url"),
        LastCheckupComplaint = Text(reader, "last_checkup_complaint"),
        LastUpdatedAt = DateTimeValue(reader, "updated_at") ?? DateTime.Now,
        SyncStatus = Text(reader, "sync_status")
    };

    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new SqliteCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            AddNullable(command, parameter.Name, parameter.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new SqliteCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            AddNullable(command, parameter.Name, parameter.Value);
        }

        return await command.ExecuteScalarAsync();
    }

    private static void AddNullable(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string NormalizeLayoutOption(string value)
    {
        return value.Equals("Center", StringComparison.OrdinalIgnoreCase)
            ? "Center"
            : value.Equals("Right", StringComparison.OrdinalIgnoreCase)
                ? "Right"
                : "Left";
    }

    private static string DbDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? string.Empty;

    private static string DbDateTime(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss");

    private static int Ord(SqliteDataReader reader, string name) => reader.GetOrdinal(name);

    private static string Text(SqliteDataReader reader, string name)
    {
        var ordinal = Ord(reader, name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static string? TextOrNull(SqliteDataReader reader, string name)
    {
        var value = Text(reader, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int Int(SqliteDataReader reader, string name) => Convert.ToInt32(reader.GetValue(Ord(reader, name)));

    private static int? NullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = Ord(reader, name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal? Decimal(SqliteDataReader reader, string name)
    {
        var ordinal = Ord(reader, name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static bool? Bool(SqliteDataReader reader, string name)
    {
        var ordinal = Ord(reader, name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal)) != 0;
    }

    private static DateOnly? DateOnlyValue(SqliteDataReader reader, string name)
    {
        var text = Text(reader, name);
        if (string.IsNullOrWhiteSpace(text) || !DateTime.TryParse(text, out var value))
        {
            return null;
        }

        return DateOnly.FromDateTime(value);
    }

    private static DateTime? DateTimeValue(SqliteDataReader reader, string name)
    {
        var ordinal = Ord(reader, name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        return DateTime.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }
}
