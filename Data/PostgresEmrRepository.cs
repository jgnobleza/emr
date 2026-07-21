using medrec.Models;
using medrec.ViewModels;
using Npgsql;

namespace medrec.Data;

public sealed class PostgresEmrRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresEmrRepository> _logger;

    public PostgresEmrRepository(PostgresConnectionFactory connectionFactory, ILogger<PostgresEmrRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<DashboardViewModel> GetDashboardAsync()
    {
        if (!_connectionFactory.IsConfigured)
        {
            return DemoData(includeDatabaseFlag: false);
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await EnsureClinicalRecordVitalsColumnsAsync(connection);

            var live = new DashboardViewModel
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

            return live;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load EMR data from PostgreSQL. Demo data is being shown.");
            var demo = DemoData(includeDatabaseFlag: true);
            demo.DataNotice = "PostgreSQL is configured, but the app could not read from it. Demo data is shown until the database is reachable and schema.sql has been applied.";
            return demo;
        }
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
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            INSERT INTO patients
              (client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company,
               email, partner_name, partner_contact_number, referred_by, age_of_menarche,
               menopause_age, previous_menstrual_period, period_cycle_days, period_duration_days,
               menstrual_amount, menstrual_pattern, sexually_active, contraception_method,
               height_cm, weight_kg, blood_pressure, fetal_heart_tone, last_menstrual_period, photo_url,
               sync_status)
            VALUES
              (gen_random_uuid()::text, @patientNumber, @fullName, @age, @address, @sex, @civilStatus, @contactNumber, @occupation, @company,
               @email, @partnerName, @partnerContactNumber, @referredBy, @ageOfMenarche,
               @menopauseAge, @previousMenstrualPeriod, @periodCycleDays, @periodDurationDays,
               @menstrualAmount, @menstrualPattern, @sexuallyActive, @contraceptionMethod,
               @heightCm, @weightKg, @bloodPressure, @fetalHeartTone, @lastMenstrualPeriod, @photoUrl,
               'Pending') RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@patientNumber", $"OB-{DateTime.Now:yyyyMMddHHmmssfff}");
        AddPatientParameters(command, form);
        command.Parameters.AddWithValue("@photoUrl", string.IsNullOrWhiteSpace(photoUrl) ? DBNull.Value : photoUrl.Trim());

        var patientId = Convert.ToInt32(await command.ExecuteScalarAsync());
        await AddSyncQueueItemAsync(connection, transaction, "Patient", patientId, "Create");
        await transaction.CommitAsync();
        return patientId;
    }

    public async Task UpdatePatientAsync(PatientEditFormModel form, string? photoUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            UPDATE patients
            SET full_name = @fullName,
                age = @age,
                address = @address,
                sex = @sex,
                civil_status = @civilStatus,
                contact_number = @contactNumber,
                occupation = @occupation,
                company = @company,
                email = @email,
                partner_name = @partnerName,
                partner_contact_number = @partnerContactNumber,
                referred_by = @referredBy,
                age_of_menarche = @ageOfMenarche,
                menopause_age = @menopauseAge,
                previous_menstrual_period = @previousMenstrualPeriod,
                period_cycle_days = @periodCycleDays,
                period_duration_days = @periodDurationDays,
                menstrual_amount = @menstrualAmount,
                menstrual_pattern = @menstrualPattern,
                sexually_active = @sexuallyActive,
                contraception_method = @contraceptionMethod,
                height_cm = @heightCm,
                weight_kg = @weightKg,
                blood_pressure = @bloodPressure,
                fetal_heart_tone = @fetalHeartTone,
                last_menstrual_period = @lastMenstrualPeriod,
                photo_url = COALESCE(@photoUrl, photo_url),
                sync_status = 'Pending',
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND archived_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", form.Id);
        AddPatientParameters(command, form);
        command.Parameters.AddWithValue("@photoUrl", string.IsNullOrWhiteSpace(photoUrl) ? DBNull.Value : photoUrl.Trim());

        await command.ExecuteNonQueryAsync();
        await AddSyncQueueItemAsync(connection, transaction, "Patient", form.Id, "Update");
        await transaction.CommitAsync();
    }

    public async Task ArchivePatientAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            UPDATE patients
            SET archived_at = CURRENT_TIMESTAMP,
                sync_status = 'Pending',
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND archived_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();

        await AddSyncQueueItemAsync(connection, transaction, "Patient", id, "Delete");
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<Patient>> GetArchivedPatientsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        const string sql = """
            SELECT id, patient_number, full_name, age, address, sex, archived_at, updated_at, sync_status
            FROM patients
            WHERE archived_at IS NOT NULL
            ORDER BY archived_at DESC, full_name ASC;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var patients = new List<Patient>();
        while (await reader.ReadAsync())
        {
            patients.Add(new Patient
            {
                Id = reader.GetInt32("id"),
                PatientNumber = reader.GetString("patient_number"),
                FullName = reader.GetString("full_name"),
                Age = reader.GetInt32("age"),
                Address = reader.GetString("address"),
                Sex = reader.GetString("sex"),
                ArchivedAt = reader.GetDateTime("archived_at"),
                LastUpdatedAt = reader.GetDateTime("updated_at"),
                SyncStatus = reader.GetString("sync_status")
            });
        }

        return patients;
    }

    public async Task UnarchivePatientAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            UPDATE patients
            SET archived_at = NULL,
                sync_status = 'Pending',
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND archived_at IS NOT NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new InvalidOperationException("Archived patient was not found.");
        }

        await AddSyncQueueItemAsync(connection, transaction, "Patient", id, "Update");
        await transaction.CommitAsync();
    }

    public async Task<int> CreateClinicalRecordAsync(RecordFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            INSERT INTO clinical_records
              (client_uid, patient_id, visit_date, height_cm, weight_kg, blood_pressure, fetal_heart_rate, temperature_c,
               chief_complaint, diagnosis, notes, doctor_name, sync_status)
            VALUES
              (gen_random_uuid()::text, @patientId, @visitDate, @heightCm, @weightKg, @bloodPressure, @fetalHeartRate, @temperatureC,
               @chiefComplaint, @diagnosis, @notes, @doctorName, 'Pending') RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@visitDate", form.VisitDate);
        AddClinicalRecordVitalsParameters(command, form);
        command.Parameters.AddWithValue("@chiefComplaint", form.ChiefComplaint.Trim());
        command.Parameters.AddWithValue("@diagnosis", form.Diagnosis?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@notes", form.Notes?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@doctorName", form.DoctorName.Trim());

        var recordId = Convert.ToInt32(await command.ExecuteScalarAsync());
        await AddSyncQueueItemAsync(connection, transaction, "ClinicalRecord", recordId, "Create");
        await transaction.CommitAsync();
        return recordId;
    }

    public async Task UpdateDiagnosisAsync(DiagnosisFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            UPDATE clinical_records
            SET diagnosis = @diagnosis,
                notes = @notes,
                sync_status = 'Pending',
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", form.RecordId);
        command.Parameters.AddWithValue("@diagnosis", form.Diagnosis.Trim());
        command.Parameters.AddWithValue("@notes", form.Notes.Trim());
        await command.ExecuteNonQueryAsync();

        await AddSyncQueueItemAsync(connection, transaction, "ClinicalRecord", form.RecordId, "Update");
        await transaction.CommitAsync();
    }

    public async Task UpdateCheckupAsync(CheckupEditFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            UPDATE clinical_records
            SET chief_complaint = @chiefComplaint,
                height_cm = @heightCm,
                weight_kg = @weightKg,
                blood_pressure = @bloodPressure,
                fetal_heart_rate = @fetalHeartRate,
                temperature_c = @temperatureC,
                sync_status = 'Pending',
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", form.RecordId);
        command.Parameters.AddWithValue("@chiefComplaint", form.ChiefComplaint.Trim());
        AddClinicalRecordVitalsParameters(command, form);
        await command.ExecuteNonQueryAsync();

        await AddSyncQueueItemAsync(connection, transaction, "ClinicalRecord", form.RecordId, "Update");
        await transaction.CommitAsync();
    }

    public async Task<int> CreateLabResultAsync(LabResultFormModel form, string fileUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string sql = """
            INSERT INTO lab_results
              (client_uid, patient_id, clinical_record_id, test_name, requested_date, result_date, status, file_url, notes, sync_status)
            VALUES
              (gen_random_uuid()::text, @patientId, @recordId, @testName, @requestedDate, @resultDate, 'Uploaded', @fileUrl, @notes, 'Pending') RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@recordId", form.ClinicalRecordId.HasValue ? form.ClinicalRecordId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@testName", form.TestName.Trim());
        command.Parameters.AddWithValue("@requestedDate", form.RequestedDate);
        command.Parameters.AddWithValue("@resultDate", form.ResultDate);
        command.Parameters.AddWithValue("@fileUrl", fileUrl);
        command.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(form.Notes) ? DBNull.Value : form.Notes.Trim());

        var labId = Convert.ToInt32(await command.ExecuteScalarAsync());
        await AddSyncQueueItemAsync(connection, transaction, "LabResult", labId, "Create");
        await transaction.CommitAsync();
        return labId;
    }

    public async Task UpdateLabResultAsync(LabEditFormModel form, string fileUrl)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            UPDATE lab_results
            SET clinical_record_id = @recordId, test_name = @testName, requested_date = @requestedDate,
                result_date = @resultDate, file_url = @fileUrl, notes = @notes,
                sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP
            WHERE id = @labId AND patient_id = @patientId
              AND EXISTS (SELECT 1 FROM clinical_records WHERE id = @recordId AND patient_id = @patientId);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@labId", form.LabId);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@recordId", form.ClinicalRecordId!.Value);
        command.Parameters.AddWithValue("@testName", form.TestName.Trim());
        command.Parameters.AddWithValue("@requestedDate", form.RequestedDate);
        command.Parameters.AddWithValue("@resultDate", form.ResultDate);
        command.Parameters.AddWithValue("@fileUrl", fileUrl);
        command.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(form.Notes) ? DBNull.Value : form.Notes.Trim());
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            throw new InvalidOperationException("Lab and check up must belong to the same patient.");
        }
        await AddSyncQueueItemAsync(connection, transaction, "LabResult", form.LabId, "Update");
        await transaction.CommitAsync();
    }

    public async Task DeleteLabResultAsync(int labId, int patientId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("DELETE FROM lab_results WHERE id = @labId AND patient_id = @patientId RETURNING client_uid;", connection, transaction);
        command.Parameters.AddWithValue("@labId", labId);
        command.Parameters.AddWithValue("@patientId", patientId);
        var clientUid = Convert.ToString(await command.ExecuteScalarAsync());
        if (string.IsNullOrWhiteSpace(clientUid))
        {
            throw new InvalidOperationException("Lab result was not found.");
        }
        await using var queue = new NpgsqlCommand("INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status) VALUES ('LabResult', @labId, 'Delete', jsonb_build_object('clientUid', @clientUid), 'Pending');", connection, transaction);
        queue.Parameters.AddWithValue("@labId", labId);
        queue.Parameters.AddWithValue("@clientUid", clientUid);
        await queue.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task AttachLabToCheckUpAsync(LabAttachmentFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string validateSql = """
            SELECT COUNT(*)
            FROM lab_results l
            INNER JOIN clinical_records r ON r.id = @recordId
            WHERE l.id = @labId
              AND l.patient_id = @patientId
              AND r.patient_id = l.patient_id;
            """;

        await using (var validateCommand = new NpgsqlCommand(validateSql, connection, transaction))
        {
            validateCommand.Parameters.AddWithValue("@labId", form.LabId);
            validateCommand.Parameters.AddWithValue("@patientId", form.PatientId);
            validateCommand.Parameters.AddWithValue("@recordId", form.ClinicalRecordId!.Value);

            var matchCount = Convert.ToInt32(await validateCommand.ExecuteScalarAsync());
            if (matchCount == 0)
            {
                throw new InvalidOperationException("Lab and check up must belong to the same patient.");
            }
        }

        const string sql = """
            UPDATE lab_results
            SET clinical_record_id = @recordId,
                requested_date = @requestedDate,
                sync_status = 'Pending',
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @labId
              AND patient_id = @patientId;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@labId", form.LabId);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@recordId", form.ClinicalRecordId!.Value);
        command.Parameters.AddWithValue("@requestedDate", form.RequestedDate);
        await command.ExecuteNonQueryAsync();

        await AddSyncQueueItemAsync(connection, transaction, "LabResult", form.LabId, "Update");
        await transaction.CommitAsync();
    }

    public async Task<int> CreatePrescriptionAsync(PrescriptionFormModel form)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var items = form.NormalizedItems().ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Add at least one drug.");
        }

        var firstItem = items[0];

        const string sql = """
            INSERT INTO prescriptions
              (client_uid, patient_id, clinical_record_id, issued_at, medication, dosage, frequency,
               duration, instructions, prescriber, sync_status)
            VALUES
              (gen_random_uuid()::text, @patientId, @recordId, CURRENT_TIMESTAMP, @medication, @dosage, @frequency,
               @duration, @instructions, @prescriber, 'Pending') RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@patientId", form.PatientId);
        command.Parameters.AddWithValue("@recordId", form.ClinicalRecordId.HasValue ? form.ClinicalRecordId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@medication", firstItem.Medication.Trim());
        command.Parameters.AddWithValue("@dosage", firstItem.Dosage.Trim());
        command.Parameters.AddWithValue("@frequency", firstItem.Frequency.Trim());
        command.Parameters.AddWithValue("@duration", firstItem.Duration.Trim());
        command.Parameters.AddWithValue("@instructions", string.IsNullOrWhiteSpace(form.Instructions) ? DBNull.Value : form.Instructions.Trim());
        command.Parameters.AddWithValue("@prescriber", form.Prescriber.Trim());

        var prescriptionId = Convert.ToInt32(await command.ExecuteScalarAsync());

        for (var index = 0; index < items.Count; index++)
        {
            await AddPrescriptionItemAsync(connection, transaction, prescriptionId, items[index], index);
        }

        await AddSyncQueueItemAsync(connection, transaction, "Prescription", prescriptionId, "Create");
        await transaction.CommitAsync();
        return prescriptionId;
    }

    private static async Task AddPrescriptionItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int prescriptionId,
        PrescriptionItemFormModel item,
        int sortOrder)
    {
        const string sql = """
            INSERT INTO prescription_items
              (prescription_id, medication, dosage, frequency, duration, sort_order)
            VALUES
              (@prescriptionId, @medication, @dosage, @frequency, @duration, @sortOrder);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@prescriptionId", prescriptionId);
        command.Parameters.AddWithValue("@medication", item.Medication.Trim());
        command.Parameters.AddWithValue("@dosage", item.Dosage.Trim());
        command.Parameters.AddWithValue("@frequency", item.Frequency.Trim());
        command.Parameters.AddWithValue("@duration", item.Duration.Trim());
        command.Parameters.AddWithValue("@sortOrder", sortOrder);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RegisterPrescriptionPrintAsync(int id)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            UPDATE prescriptions
            SET print_count = print_count + 1,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdatePrintLayoutAsync(PrintLayoutFormModel form, string documentType, string? logoUrl)
    {
        await using var connection = await OpenConnectionAsync();
        var normalizedDocumentType = PrintLayout.NormalizeDocumentType(documentType);
        var defaultLayoutId = PrintLayout.LayoutId(normalizedDocumentType);
        var layoutId = await ResolvePrintLayoutIdAsync(connection, normalizedDocumentType, defaultLayoutId);
        var documentTitle = string.IsNullOrWhiteSpace(form.DocumentTitle)
            ? normalizedDocumentType
            : form.DocumentTitle.Trim();
        var layoutJson = PrintLayoutFormModel.SerializeBlocks(PrintLayoutFormModel.ParseBlocks(form.LayoutJson, normalizedDocumentType));

        const string sql = """
            INSERT INTO print_layouts
              (id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule, clinic_address,
               logo_url, logo_position, details_alignment, signatory_name, signatory_title, layout_json)
            VALUES
              (@id, @documentType, @documentTitle, @clinicName, @doctorName, @licenseNumber, @clinicSchedule, @clinicAddress,
               @logoUrl, @logoPosition, @detailsAlignment, @signatoryName, @signatoryTitle, @layoutJson::jsonb)
            ON CONFLICT (id) DO UPDATE SET
              document_type = EXCLUDED.document_type,
              document_title = EXCLUDED.document_title,
              clinic_name = EXCLUDED.clinic_name,
              doctor_name = EXCLUDED.doctor_name,
              license_number = EXCLUDED.license_number,
              clinic_schedule = EXCLUDED.clinic_schedule,
              clinic_address = EXCLUDED.clinic_address,
              logo_url = EXCLUDED.logo_url,
              logo_position = EXCLUDED.logo_position,
              details_alignment = EXCLUDED.details_alignment,
              signatory_name = EXCLUDED.signatory_name,
              signatory_title = EXCLUDED.signatory_title,
              layout_json = EXCLUDED.layout_json,
              updated_at = CURRENT_TIMESTAMP;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", layoutId);
        command.Parameters.AddWithValue("@documentType", normalizedDocumentType);
        command.Parameters.AddWithValue("@documentTitle", documentTitle);
        command.Parameters.AddWithValue("@clinicName", form.ClinicName.Trim());
        command.Parameters.AddWithValue("@doctorName", form.DoctorName.Trim());
        command.Parameters.AddWithValue("@licenseNumber", form.LicenseNumber?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@clinicSchedule", form.ClinicSchedule?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@clinicAddress", form.ClinicAddress?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@logoUrl", string.IsNullOrWhiteSpace(logoUrl) ? DBNull.Value : logoUrl.Trim());
        command.Parameters.AddWithValue("@logoPosition", NormalizeLayoutOption(form.LogoPosition));
        command.Parameters.AddWithValue("@detailsAlignment", NormalizeLayoutOption(form.DetailsAlignment));
        command.Parameters.AddWithValue("@signatoryName", form.SignatoryName.Trim());
        command.Parameters.AddWithValue("@signatoryTitle", form.SignatoryTitle?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@layoutJson", layoutJson);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ResolvePrintLayoutIdAsync(NpgsqlConnection connection, string documentType, int defaultId)
    {
        const string sql = "SELECT id FROM print_layouts WHERE LOWER(document_type) = LOWER(@documentType) ORDER BY updated_at DESC LIMIT 1;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@documentType", documentType);
        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? defaultId : Convert.ToInt32(result);
    }

    public async Task<int> ManualSyncAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string countSql = "SELECT COUNT(*) FROM sync_queue WHERE status = 'Pending';";
        await using var countCommand = new NpgsqlCommand(countSql, connection, transaction);
        var pendingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        var statements = new[]
        {
            "UPDATE patients SET sync_status = 'Synced', last_synced_at = CURRENT_TIMESTAMP WHERE sync_status = 'Pending';",
            "UPDATE clinical_records SET sync_status = 'Synced', last_synced_at = CURRENT_TIMESTAMP WHERE sync_status = 'Pending';",
            "UPDATE lab_results SET sync_status = 'Synced' WHERE sync_status = 'Pending';",
            "UPDATE prescriptions SET sync_status = 'Synced' WHERE sync_status = 'Pending';",
            "UPDATE sync_queue SET status = 'Synced', synced_at = CURRENT_TIMESTAMP WHERE status = 'Pending';",
            """
            INSERT INTO sync_runs (sync_type, finished_at, records_uploaded, status, message)
            VALUES ('Manual', CURRENT_TIMESTAMP, @pendingCount, 'Completed', 'Manual sync completed');
            """
        };

        foreach (var statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection, transaction);
            command.Parameters.AddWithValue("@pendingCount", pendingCount);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return pendingCount;
    }

        public async Task<string> CleanupRecordDataAsync()
        {
                await using var connection = await OpenConnectionAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                const string normalizeComplaintSql = "UPDATE clinical_records SET chief_complaint = 'Not specified' WHERE TRIM(chief_complaint) = '';";
                const string normalizeDiagnosisSql = "UPDATE clinical_records SET diagnosis = 'Pending diagnosis' WHERE TRIM(diagnosis) = '';";
                const string normalizeDoctorSql = "UPDATE clinical_records SET doctor_name = 'Doctor' WHERE TRIM(doctor_name) = '';";

                const string fixLabLinksSql = """
                        UPDATE lab_results l
                        SET clinical_record_id = NULL,
                            sync_status = 'Pending',
                            updated_at = CURRENT_TIMESTAMP
                        WHERE l.clinical_record_id IS NOT NULL
                          AND NOT EXISTS (
                            SELECT 1
                            FROM clinical_records r
                            WHERE r.id = l.clinical_record_id
                              AND r.patient_id = l.patient_id
                          );
                        """;

                const string fixPrescriptionLinksSql = """
                        UPDATE prescriptions p
                        SET clinical_record_id = NULL,
                            sync_status = 'Pending',
                            updated_at = CURRENT_TIMESTAMP
                        WHERE p.clinical_record_id IS NOT NULL
                          AND NOT EXISTS (
                            SELECT 1
                            FROM clinical_records r
                            WHERE r.id = p.clinical_record_id
                              AND r.patient_id = p.patient_id
                          );
                        """;

                const string insertMissingItemsSql = """
                        INSERT INTO prescription_items
                            (prescription_id, medication, dosage, frequency, duration, sort_order)
                        SELECT p.id, p.medication, p.dosage, p.frequency, p.duration, 0
                        FROM prescriptions p
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM prescription_items pi
                            WHERE pi.prescription_id = p.id
                        );
                        """;

                var normalizedComplaints = await ExecuteNonQueryAsync(connection, transaction, normalizeComplaintSql);
                var normalizedDiagnoses = await ExecuteNonQueryAsync(connection, transaction, normalizeDiagnosisSql);
                var normalizedDoctors = await ExecuteNonQueryAsync(connection, transaction, normalizeDoctorSql);
                var fixedLabLinks = await ExecuteNonQueryAsync(connection, transaction, fixLabLinksSql);
                var fixedPrescriptionLinks = await ExecuteNonQueryAsync(connection, transaction, fixPrescriptionLinksSql);
                var addedPrescriptionItems = await ExecuteNonQueryAsync(connection, transaction, insertMissingItemsSql);

                await transaction.CommitAsync();

                return $"clinical_records normalized: complaints={normalizedComplaints}, diagnoses={normalizedDiagnoses}, doctors={normalizedDoctors}; links fixed: labs={fixedLabLinks}, prescriptions={fixedPrescriptionLinks}; prescription_items added={addedPrescriptionItems}.";
        }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        if (!_connectionFactory.IsConfigured)
        {
            throw new InvalidOperationException("PostgreSQL is not configured.");
        }

        var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await EnsureClinicalRecordVitalsColumnsAsync(connection);
        return connection;
    }

    private static async Task EnsureClinicalRecordVitalsColumnsAsync(NpgsqlConnection connection)
    {
        var changed = false;
        changed |= await AddColumnIfMissingAsync(connection, "clinical_records", "height_cm", "DECIMAL(6,2) NULL", "visit_date");
        changed |= await AddColumnIfMissingAsync(connection, "clinical_records", "weight_kg", "DECIMAL(6,2) NULL", "height_cm");
        changed |= await AddColumnIfMissingAsync(connection, "clinical_records", "blood_pressure", "VARCHAR(40) NOT NULL DEFAULT ''", "weight_kg");
        changed |= await AddColumnIfMissingAsync(connection, "clinical_records", "fetal_heart_rate", "VARCHAR(40) NOT NULL DEFAULT ''", "blood_pressure");
        changed |= await AddColumnIfMissingAsync(connection, "clinical_records", "temperature_c", "DECIMAL(5,2) NULL", "fetal_heart_rate");

        if (changed)
        {
            await ExecuteNonQueryAsync(connection, """
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

        await ExecuteNonQueryAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        return true;
    }

    private static async Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection connection, string table, string column)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column;",
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task AddSyncQueueItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string entityType,
        int entityId,
        string operation)
    {
        const string sql = """
            INSERT INTO sync_queue (entity_type, entity_id, operation, status)
            VALUES (@entityType, @entityId, @operation, 'Pending');
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@entityType", entityType);
        command.Parameters.AddWithValue("@entityId", entityId);
        command.Parameters.AddWithValue("@operation", operation);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddPatientParameters(NpgsqlCommand command, PatientFormModel form)
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
        command.Parameters.AddWithValue("@ageOfMenarche", (object?)form.AgeOfMenarche ?? DBNull.Value);
        command.Parameters.AddWithValue("@menopauseAge", (object?)form.MenopauseAge ?? DBNull.Value);
        command.Parameters.AddWithValue("@previousMenstrualPeriod", DbDate(form.PreviousMenstrualPeriod));
        command.Parameters.AddWithValue("@periodCycleDays", (object?)form.PeriodCycleDays ?? DBNull.Value);
        command.Parameters.AddWithValue("@periodDurationDays", (object?)form.PeriodDurationDays ?? DBNull.Value);
        command.Parameters.AddWithValue("@menstrualAmount", form.MenstrualAmount?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@menstrualPattern", form.MenstrualPattern?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@sexuallyActive", (object?)form.SexuallyActive ?? DBNull.Value);
        command.Parameters.AddWithValue("@contraceptionMethod", form.ContraceptionMethod?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@heightCm", (object?)form.HeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@weightKg", (object?)form.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartTone", form.FetalHeartTone?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@lastMenstrualPeriod", DbDate(form.LastMenstrualPeriod));
    }

    private static void AddPatientParameters(NpgsqlCommand command, PatientEditFormModel form)
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
        command.Parameters.AddWithValue("@ageOfMenarche", (object?)form.AgeOfMenarche ?? DBNull.Value);
        command.Parameters.AddWithValue("@menopauseAge", (object?)form.MenopauseAge ?? DBNull.Value);
        command.Parameters.AddWithValue("@previousMenstrualPeriod", DbDate(form.PreviousMenstrualPeriod));
        command.Parameters.AddWithValue("@periodCycleDays", (object?)form.PeriodCycleDays ?? DBNull.Value);
        command.Parameters.AddWithValue("@periodDurationDays", (object?)form.PeriodDurationDays ?? DBNull.Value);
        command.Parameters.AddWithValue("@menstrualAmount", form.MenstrualAmount?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@menstrualPattern", form.MenstrualPattern?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@sexuallyActive", (object?)form.SexuallyActive ?? DBNull.Value);
        command.Parameters.AddWithValue("@contraceptionMethod", form.ContraceptionMethod?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@heightCm", (object?)form.HeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@weightKg", (object?)form.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartTone", form.FetalHeartTone?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@lastMenstrualPeriod", DbDate(form.LastMenstrualPeriod));
    }

    private static void AddClinicalRecordVitalsParameters(NpgsqlCommand command, RecordFormModel form)
    {
        command.Parameters.AddWithValue("@heightCm", (object?)form.HeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@weightKg", (object?)form.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartRate", form.FetalHeartRate?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@temperatureC", (object?)form.TemperatureC ?? DBNull.Value);
    }

    private static void AddClinicalRecordVitalsParameters(NpgsqlCommand command, CheckupEditFormModel form)
    {
        command.Parameters.AddWithValue("@heightCm", (object?)form.HeightCm ?? DBNull.Value);
        command.Parameters.AddWithValue("@weightKg", (object?)form.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("@bloodPressure", form.BloodPressure?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@fetalHeartRate", form.FetalHeartRate?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@temperatureC", (object?)form.TemperatureC ?? DBNull.Value);
    }

    private static object DbDate(DateOnly? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static string NormalizeLayoutOption(string value)
    {
        return value.Equals("Center", StringComparison.OrdinalIgnoreCase)
            ? "Center"
            : value.Equals("Right", StringComparison.OrdinalIgnoreCase)
                ? "Right"
                : "Left";
    }

    private static async Task<IReadOnlyList<Patient>> GetPatientsAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT id, client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number,
                   occupation, company, email, partner_name, partner_contact_number,
                   referred_by, age_of_menarche, menopause_age, previous_menstrual_period,
                   period_cycle_days, period_duration_days, menstrual_amount,
                   menstrual_pattern, sexually_active, contraception_method,
                   height_cm, weight_kg, blood_pressure, fetal_heart_tone,
                   last_menstrual_period, photo_url,
                   (
                       SELECT cr.chief_complaint
                       FROM clinical_records cr
                       WHERE cr.patient_id = p.id
                       ORDER BY cr.visit_date DESC, cr.id DESC
                       LIMIT 1
                   ) AS last_checkup_complaint,
                   updated_at, sync_status
            FROM patients p
            WHERE archived_at IS NULL
            ORDER BY full_name ASC;
            """;

        var patients = new List<Patient>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            patients.Add(new Patient
            {
                Id = reader.GetInt32("id"),
                ClientUid = Convert.ToString(reader["client_uid"]) ?? string.Empty,
                PatientNumber = reader.GetString("patient_number"),
                FullName = reader.GetString("full_name"),
                Age = reader.GetInt32("age"),
                Address = reader.GetString("address"),
                Sex = reader.GetString("sex"),
                CivilStatus = reader.GetString("civil_status"),
                ContactNumber = reader.GetString("contact_number"),
                Occupation = reader.GetString("occupation"),
                Company = reader.GetString("company"),
                Email = reader.GetString("email"),
                PartnerName = reader.GetString("partner_name"),
                PartnerContactNumber = reader.GetString("partner_contact_number"),
                ReferredBy = reader.GetString("referred_by"),
                AgeOfMenarche = reader.IsDBNull(reader.GetOrdinal("age_of_menarche")) ? null : reader.GetInt32("age_of_menarche"),
                MenopauseAge = reader.IsDBNull(reader.GetOrdinal("menopause_age")) ? null : reader.GetInt32("menopause_age"),
                PreviousMenstrualPeriod = reader.IsDBNull(reader.GetOrdinal("previous_menstrual_period")) ? null : DateOnly.FromDateTime(reader.GetDateTime("previous_menstrual_period")),
                PeriodCycleDays = reader.IsDBNull(reader.GetOrdinal("period_cycle_days")) ? null : reader.GetInt32("period_cycle_days"),
                PeriodDurationDays = reader.IsDBNull(reader.GetOrdinal("period_duration_days")) ? null : reader.GetInt32("period_duration_days"),
                MenstrualAmount = reader.GetString("menstrual_amount"),
                MenstrualPattern = reader.GetString("menstrual_pattern"),
                SexuallyActive = reader.IsDBNull(reader.GetOrdinal("sexually_active")) ? null : reader.GetBoolean("sexually_active"),
                ContraceptionMethod = reader.GetString("contraception_method"),
                HeightCm = reader.IsDBNull(reader.GetOrdinal("height_cm")) ? null : reader.GetDecimal("height_cm"),
                WeightKg = reader.IsDBNull(reader.GetOrdinal("weight_kg")) ? null : reader.GetDecimal("weight_kg"),
                BloodPressure = reader.GetString("blood_pressure"),
                FetalHeartTone = reader.GetString("fetal_heart_tone"),
                LastMenstrualPeriod = reader.IsDBNull(reader.GetOrdinal("last_menstrual_period")) ? null : DateOnly.FromDateTime(reader.GetDateTime("last_menstrual_period")),
                PhotoUrl = reader.IsDBNull(reader.GetOrdinal("photo_url")) ? null : reader.GetString("photo_url"),
                LastCheckupComplaint = reader.IsDBNull(reader.GetOrdinal("last_checkup_complaint")) ? string.Empty : reader.GetString("last_checkup_complaint"),
                LastUpdatedAt = reader.GetDateTime("updated_at"),
                SyncStatus = reader.GetString("sync_status")
            });
        }

        return patients;
    }

    private static async Task<IReadOnlyList<ClinicalRecord>> GetClinicalRecordsAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT r.id, r.client_uid, r.patient_id, p.full_name AS patient_name, p.address AS patient_address,
                   p.age AS patient_age, p.sex AS patient_sex, r.visit_date,
                   r.height_cm, r.weight_kg, r.blood_pressure, r.fetal_heart_rate, r.temperature_c,
                   r.chief_complaint, r.diagnosis, r.notes, r.doctor_name, r.sync_status, r.updated_at
            FROM clinical_records r
            INNER JOIN patients p ON p.id = r.patient_id
            ORDER BY r.visit_date DESC;
            """;

        var records = new List<ClinicalRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new ClinicalRecord
            {
                Id = reader.GetInt32("id"),
                ClientUid = Convert.ToString(reader["client_uid"]) ?? string.Empty,
                PatientId = reader.GetInt32("patient_id"),
                PatientName = reader.GetString("patient_name"),
                PatientAddress = reader.GetString("patient_address"),
                PatientAge = reader.GetInt32("patient_age"),
                PatientSex = reader.GetString("patient_sex"),
                VisitDate = reader.GetDateTime("visit_date"),
                HeightCm = reader.IsDBNull(reader.GetOrdinal("height_cm")) ? null : reader.GetDecimal("height_cm"),
                WeightKg = reader.IsDBNull(reader.GetOrdinal("weight_kg")) ? null : reader.GetDecimal("weight_kg"),
                BloodPressure = reader.GetString("blood_pressure"),
                FetalHeartRate = reader.GetString("fetal_heart_rate"),
                TemperatureC = reader.IsDBNull(reader.GetOrdinal("temperature_c")) ? null : reader.GetDecimal("temperature_c"),
                ChiefComplaint = reader.GetString("chief_complaint"),
                Diagnosis = reader.GetString("diagnosis"),
                Notes = reader.GetString("notes"),
                DoctorName = reader.GetString("doctor_name"),
                SyncStatus = reader.GetString("sync_status"),
                UpdatedAt = reader.GetDateTime("updated_at")
            });
        }

        return records;
    }

    private static async Task<IReadOnlyList<LabResult>> GetLabResultsAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT l.id, l.client_uid, l.patient_id, l.clinical_record_id, p.full_name AS patient_name,
                   l.test_name, l.requested_date, l.result_date, l.status, l.file_url, l.notes,
                   r.visit_date AS check_up_date, r.chief_complaint AS check_up_complaint, l.updated_at
            FROM lab_results l
            INNER JOIN patients p ON p.id = l.patient_id
            LEFT JOIN clinical_records r ON r.id = l.clinical_record_id
            ORDER BY l.requested_date DESC, l.result_date DESC;
            """;

        var labs = new List<LabResult>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            labs.Add(new LabResult
            {
                Id = reader.GetInt32("id"),
                ClientUid = Convert.ToString(reader["client_uid"]) ?? string.Empty,
                PatientId = reader.GetInt32("patient_id"),
                ClinicalRecordId = reader.IsDBNull(reader.GetOrdinal("clinical_record_id")) ? null : reader.GetInt32("clinical_record_id"),
                PatientName = reader.GetString("patient_name"),
                TestName = reader.GetString("test_name"),
                RequestedDate = reader.GetDateTime("requested_date"),
                ResultDate = reader.GetDateTime("result_date"),
                CheckUpDate = reader.IsDBNull(reader.GetOrdinal("check_up_date")) ? null : reader.GetDateTime("check_up_date"),
                CheckUpComplaint = reader.IsDBNull(reader.GetOrdinal("check_up_complaint")) ? string.Empty : reader.GetString("check_up_complaint"),
                Status = reader.GetString("status"),
                FileUrl = reader.GetString("file_url"),
                Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? string.Empty : reader.GetString("notes"),
                UpdatedAt = reader.GetDateTime("updated_at")
            });
        }

        return labs;
    }

    private static async Task<IReadOnlyList<Prescription>> GetPrescriptionsAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT pr.id, pr.client_uid, pr.patient_id, pr.clinical_record_id, p.full_name AS patient_name,
                   p.address AS patient_address, p.age AS patient_age, p.sex AS patient_sex,
                   pr.issued_at, pr.medication, pr.dosage, pr.frequency, pr.duration,
                   pr.instructions, pr.prescriber, pr.updated_at
            FROM prescriptions pr
            INNER JOIN patients p ON p.id = pr.patient_id
            ORDER BY pr.issued_at DESC;
            """;

        var prescriptions = new List<Prescription>();
        await using var command = new NpgsqlCommand(sql, connection);
        {
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                prescriptions.Add(new Prescription
                {
                    Id = reader.GetInt32("id"),
                    ClientUid = Convert.ToString(reader["client_uid"]) ?? string.Empty,
                    PatientId = reader.GetInt32("patient_id"),
                    ClinicalRecordId = reader.IsDBNull(reader.GetOrdinal("clinical_record_id")) ? null : reader.GetInt32("clinical_record_id"),
                    PatientName = reader.GetString("patient_name"),
                    PatientAddress = reader.GetString("patient_address"),
                    PatientAge = reader.GetInt32("patient_age"),
                    PatientSex = reader.GetString("patient_sex"),
                    IssuedAt = reader.GetDateTime("issued_at"),
                    Medication = reader.GetString("medication"),
                    Dosage = reader.GetString("dosage"),
                    Frequency = reader.GetString("frequency"),
                    Duration = reader.GetString("duration"),
                    Instructions = reader.IsDBNull(reader.GetOrdinal("instructions")) ? string.Empty : reader.GetString("instructions"),
                    Prescriber = reader.GetString("prescriber"),
                    UpdatedAt = reader.GetDateTime("updated_at")
                });
            }
        }

        await LoadPrescriptionItemsAsync(connection, prescriptions);
        return prescriptions;
    }

    private static async Task LoadPrescriptionItemsAsync(NpgsqlConnection connection, IReadOnlyList<Prescription> prescriptions)
    {
        if (prescriptions.Count == 0)
        {
            return;
        }

        var prescriptionIds = prescriptions.Select(item => item.Id).ToHashSet();

        const string sql = """
            SELECT id, prescription_id, medication, dosage, frequency, duration, sort_order
            FROM prescription_items
            ORDER BY prescription_id, sort_order, id;
            """;

        var grouped = new Dictionary<int, List<PrescriptionItem>>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var prescriptionId = reader.GetInt32("prescription_id");
            if (!prescriptionIds.Contains(prescriptionId))
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
                Id = reader.GetInt32("id"),
                PrescriptionId = prescriptionId,
                Medication = reader.GetString("medication"),
                Dosage = reader.GetString("dosage"),
                Frequency = reader.GetString("frequency"),
                Duration = reader.GetString("duration"),
                SortOrder = reader.GetInt32("sort_order")
            });
        }

        foreach (var prescription in prescriptions)
        {
            if (grouped.TryGetValue(prescription.Id, out var items) && items.Count > 0)
            {
                prescription.Items = items;
                continue;
            }

            prescription.Items =
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

    private static async Task<PrintLayout> GetPrintLayoutAsync(NpgsqlConnection connection, string documentType)
    {
        var normalizedDocumentType = PrintLayout.NormalizeDocumentType(documentType);

        const string sql = """
            SELECT id, document_type, document_title, clinic_name, doctor_name, license_number,
                   clinic_schedule, clinic_address, logo_url, logo_position, details_alignment,
                   signatory_name, signatory_title, layout_json, updated_at
            FROM print_layouts
            WHERE document_type = @documentType OR id = @id
            ORDER BY CASE WHEN document_type = @documentType THEN 0 ELSE 1 END
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@documentType", normalizedDocumentType);
        command.Parameters.AddWithValue("@id", PrintLayout.LayoutId(normalizedDocumentType));
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return PrintLayout.Default(normalizedDocumentType);
        }

        var storedDocumentType = reader.IsDBNull(reader.GetOrdinal("document_type"))
            ? normalizedDocumentType
            : reader.GetString("document_type");
        var storedTitle = reader.IsDBNull(reader.GetOrdinal("document_title"))
            ? PrintLayout.Default(normalizedDocumentType).DocumentTitle
            : reader.GetString("document_title");
        var layoutJson = reader.IsDBNull(reader.GetOrdinal("layout_json"))
            ? null
            : reader.GetString("layout_json");

        return new PrintLayout
        {
            Id = reader.GetInt32("id"),
            DocumentType = PrintLayout.NormalizeDocumentType(storedDocumentType),
            DocumentTitle = storedTitle,
            ClinicName = reader.GetString("clinic_name"),
            DoctorName = reader.GetString("doctor_name"),
            LicenseNumber = reader.GetString("license_number"),
            ClinicSchedule = reader.GetString("clinic_schedule"),
            ClinicAddress = reader.GetString("clinic_address"),
            LogoUrl = reader.IsDBNull(reader.GetOrdinal("logo_url")) ? null : reader.GetString("logo_url"),
            LogoPosition = reader.GetString("logo_position"),
            DetailsAlignment = reader.GetString("details_alignment"),
            SignatoryName = reader.GetString("signatory_name"),
            SignatoryTitle = reader.GetString("signatory_title"),
            Blocks = PrintLayoutFormModel.ParseBlocks(layoutJson, normalizedDocumentType),
            UpdatedAt = reader.GetDateTime("updated_at")
        };
    }

    private static async Task<IReadOnlyList<SyncItem>> GetSyncQueueAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT id, entity_type, entity_id, operation, updated_at, synced_at, status
            FROM sync_queue
            ORDER BY updated_at DESC
            LIMIT 10;
            """;

        var queue = new List<SyncItem>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            queue.Add(new SyncItem
            {
                Id = reader.GetInt32("id"),
                EntityType = reader.GetString("entity_type"),
                EntityId = reader.GetInt32("entity_id"),
                Operation = reader.GetString("operation"),
                UpdatedAt = reader.GetDateTime("updated_at"),
                SyncedAt = reader.IsDBNull(reader.GetOrdinal("synced_at")) ? null : reader.GetDateTime("synced_at"),
                Status = reader.GetString("status")
            });
        }

        return queue;
    }

    private static async Task<string> GetLastSyncLabelAsync(NpgsqlConnection connection)
    {
        const string sql = "SELECT MAX(synced_at) FROM sync_queue WHERE synced_at IS NOT NULL;";
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is DateTime syncedAt ? syncedAt.ToString("MMM d, h:mm tt") : "No cloud sync yet";
    }

    private static DashboardViewModel DemoData(bool includeDatabaseFlag)
    {
        var today = DateTime.Today;
        var samplePdfs = new[]
        {
            "/uploads/labs/bmp-demo.pdf",
            "/uploads/labs/09935cc016c645e8b51c79b3b7d14943.pdf",
            "/uploads/labs/0c7019e26f2e4591aa7a968f030a65db.pdf",
            "/uploads/labs/130e37c0b86b48ac806d137c03c9fb13.pdf",
            "/uploads/labs/25a27c0fb0f34d8fa7e72cb77ba6f100.pdf",
            "/uploads/labs/4ecc181f16dd4d8398149bd6005781ec.pdf",
            "/uploads/labs/82a40f34d9794e21a233df75dae5c932.pdf",
            "/uploads/labs/ab4619fad5c94e0a980ee623cabccfce.pdf",
            "/uploads/labs/c2188978e7f74f6c87e19c817957cf87.pdf",
            "/uploads/labs/f355b639a43b401ba8127779613259ef.pdf"
        };

        var samplePhotos = new[]
        {
            "/uploads/patients/8341641762fb43028ff18f438cac30c1.JPG",
            "/uploads/patients/e05aedf7e4ba4eaca447fe1974360f76.jpg",
            "/uploads/patients/f4c2cb72d965415ca21ea64f7a4e23ec.jpg",
            "/uploads/patients/f4f49c49de0b455abb5e7263eb3b56f6.png"
        };

        Patient CreatePatient(
            int id,
            string name,
            int age,
            string city,
            string civilStatus,
            string contact,
            string occupation,
            string company,
            string partner,
            string partnerContact,
            int lmpDaysAgo,
            string syncStatus,
            int updatedDaysAgo)
        {
            return new Patient
            {
                Id = id,
                PatientNumber = $"OB-2026-{id:0000}",
                FullName = name,
                Age = age,
                Address = city,
                Sex = "Female",
                CivilStatus = civilStatus,
                ContactNumber = contact,
                Occupation = occupation,
                Company = company,
                Email = name.ToLowerInvariant().Replace(" ", ".") + "@example.com",
                PartnerName = partner,
                PartnerContactNumber = partnerContact,
                ReferredBy = string.IsNullOrWhiteSpace(partner) ? "Walk-in" : "Dr. Reyes",
                AgeOfMenarche = 12,
                PreviousMenstrualPeriod = DateOnly.FromDateTime(today.AddDays(-(lmpDaysAgo - 28))),
                PeriodCycleDays = 28,
                PeriodDurationDays = 5,
                MenstrualAmount = "Moderate",
                MenstrualPattern = "Regular",
                SexuallyActive = !string.IsNullOrWhiteSpace(partner),
                ContraceptionMethod = string.IsNullOrWhiteSpace(partner) ? string.Empty : "None",
                HeightCm = 156 + (id % 8),
                WeightKg = 54 + (id % 12),
                BloodPressure = $"11{(id % 4)}/{70 + (id % 10)}",
                FetalHeartTone = lmpDaysAgo > 60 ? (138 + (id % 12)).ToString() : string.Empty,
                LastMenstrualPeriod = DateOnly.FromDateTime(today.AddDays(-lmpDaysAgo)),
                PhotoUrl = samplePhotos[(id - 1) % samplePhotos.Length],
                LastUpdatedAt = today.AddDays(-updatedDaysAgo).AddHours(8 + (id % 9)).AddMinutes((id * 7) % 60),
                SyncStatus = syncStatus
            };
        }

        var patients = new List<Patient>
        {
            CreatePatient(1, "Maria Santos", 31, "Quezon City", "Married", "0917 555 0142", "Teacher", "Quezon City High School", "Luis Santos", "0917 555 0199", 84, "Pending", 0),
            CreatePatient(2, "Angela Dela Cruz", 29, "Pasig City", "Married", "0918 223 4109", "HR Officer", "Metro Retail Group", "Marco Dela Cruz", "0918 223 4110", 70, "Synced", 1),
            CreatePatient(3, "Bea Villanueva", 24, "Caloocan City", "Single", "0927 510 2204", "Call Center Agent", "NorthCom BPO", "", "", 35, "Pending", 2),
            CreatePatient(4, "Katrina Reyes", 33, "Mandaluyong City", "Married", "0995 140 7711", "Bank Teller", "PhilTrust Bank", "Noel Reyes", "0995 140 7712", 112, "Synced", 1),
            CreatePatient(5, "Jessa Manalo", 26, "Taguig City", "Single", "0916 600 8887", "Marketing Associate", "Skyline Foods", "", "", 21, "Synced", 3),
            CreatePatient(6, "Pauline Bautista", 30, "Marikina City", "Married", "0939 440 7710", "Accountant", "Pinnacle Logistics", "Jeric Bautista", "0939 440 7711", 95, "Pending", 1),
            CreatePatient(7, "Liza Mendoza", 28, "Paranaque City", "Married", "0919 777 1201", "Nurse", "South City Clinic", "Anton Mendoza", "0919 777 1202", 63, "Synced", 1),
            CreatePatient(8, "Carla Navarro", 34, "San Juan City", "Married", "0922 300 4451", "Store Manager", "Urban Mart", "Ryan Navarro", "0922 300 4452", 118, "Pending", 2),
            CreatePatient(9, "Rina Aquino", 27, "Valenzuela City", "Single", "0935 445 1008", "QA Analyst", "BlueWave Tech", "", "", 42, "Synced", 4),
            CreatePatient(10, "Hazel Domingo", 25, "Muntinlupa City", "Single", "0915 880 3310", "Pharmacist", "HealthPlus", "", "", 31, "Pending", 2),
            CreatePatient(11, "Janine Flores", 32, "Las Pinas City", "Married", "0998 210 9011", "Admin Officer", "City Hall", "Arvin Flores", "0998 210 9012", 88, "Synced", 1),
            CreatePatient(12, "Tricia Ramos", 30, "Antipolo City", "Married", "0947 611 7612", "Sales Lead", "Summit Pharma", "Carlo Ramos", "0947 611 7613", 76, "Pending", 2),
            CreatePatient(13, "Camille Soriano", 29, "Pasay City", "Single", "0966 421 3313", "Flight Attendant", "Pacific Air", "", "", 39, "Synced", 3),
            CreatePatient(14, "Denise Alonzo", 35, "Makati City", "Married", "0928 540 6014", "Lawyer", "Del Rosario Law", "Miguel Alonzo", "0928 540 6015", 122, "Pending", 1),
            CreatePatient(15, "Mikaela Torres", 23, "Navotas City", "Single", "0977 120 8815", "Cashier", "Harbor Mall", "", "", 27, "Synced", 3),
            CreatePatient(16, "Joy Mercado", 31, "Malabon City", "Married", "0917 620 3316", "Chef", "Bayan Bistro", "Paolo Mercado", "0917 620 3317", 92, "Pending", 1),
            CreatePatient(17, "Krisha Valdez", 28, "Taguig City", "Single", "0933 710 5517", "Graphic Artist", "PixelForge", "", "", 33, "Synced", 2),
            CreatePatient(18, "Nica Pineda", 34, "Quezon City", "Married", "0955 331 9018", "Operations Manager", "PrimeCare Labs", "Jules Pineda", "0955 331 9019", 108, "Pending", 1)
        };

        var checkupTemplates = new[]
        {
            (PatientId: 1, Complaint: "Prenatal check up", Diagnosis: "Normal intrauterine pregnancy", Notes: "Routine prenatal assessment.", DaysAgo: 12, Hour: 10, Sync: "Pending"),
            (PatientId: 2, Complaint: "Follow-up prenatal", Diagnosis: "Stable prenatal findings", Notes: "Continue prenatal vitamins and hydration.", DaysAgo: 18, Hour: 14, Sync: "Synced"),
            (PatientId: 3, Complaint: "Irregular menstruation", Diagnosis: "For hormonal workup", Notes: "Requested CBC and ultrasound.", DaysAgo: 25, Hour: 11, Sync: "Pending"),
            (PatientId: 4, Complaint: "Third trimester check", Diagnosis: "Singleton viable pregnancy", Notes: "Monitor BP and fetal movement.", DaysAgo: 33, Hour: 9, Sync: "Synced"),
            (PatientId: 5, Complaint: "Dysmenorrhea", Diagnosis: "Primary dysmenorrhea", Notes: "Pain diary advised.", DaysAgo: 40, Hour: 8, Sync: "Synced"),
            (PatientId: 6, Complaint: "Prenatal consultation", Diagnosis: "Mild anemia in pregnancy", Notes: "Advised iron supplementation.", DaysAgo: 48, Hour: 8, Sync: "Pending"),
            (PatientId: 7, Complaint: "Second trimester check", Diagnosis: "Normal fetal growth", Notes: "Continue prenatal care plan.", DaysAgo: 55, Hour: 10, Sync: "Synced"),
            (PatientId: 8, Complaint: "Postpartum follow-up", Diagnosis: "Recovering well", Notes: "Schedule follow-up after 2 weeks.", DaysAgo: 62, Hour: 9, Sync: "Pending"),
            (PatientId: 9, Complaint: "Amenorrhea", Diagnosis: "For pregnancy confirmation", Notes: "Ordered beta-hCG.", DaysAgo: 70, Hour: 15, Sync: "Synced"),
            (PatientId: 10, Complaint: "Pelvic pain", Diagnosis: "Possible ovarian cyst", Notes: "Requested TVS.", DaysAgo: 78, Hour: 13, Sync: "Pending"),
            (PatientId: 11, Complaint: "Prenatal check up", Diagnosis: "Gestational hypertension monitoring", Notes: "Daily BP monitoring.", DaysAgo: 20, Hour: 11, Sync: "Synced"),
            (PatientId: 12, Complaint: "Follow-up prenatal", Diagnosis: "Normal fetal heartbeat", Notes: "Continue current meds.", DaysAgo: 28, Hour: 16, Sync: "Pending"),
            (PatientId: 13, Complaint: "PCOS follow-up", Diagnosis: "PCOS under management", Notes: "Continue cycle regulation plan.", DaysAgo: 36, Hour: 10, Sync: "Synced"),
            (PatientId: 14, Complaint: "Prenatal consultation", Diagnosis: "Advanced maternal age pregnancy", Notes: "Requested anomaly scan.", DaysAgo: 45, Hour: 9, Sync: "Pending"),
            (PatientId: 15, Complaint: "Missed period", Diagnosis: "Early pregnancy", Notes: "Schedule first trimester ultrasound.", DaysAgo: 53, Hour: 14, Sync: "Synced"),
            (PatientId: 16, Complaint: "Third trimester check", Diagnosis: "Normal progression", Notes: "Kick count education provided.", DaysAgo: 61, Hour: 15, Sync: "Pending"),
            (PatientId: 17, Complaint: "Menstrual irregularity", Diagnosis: "For endocrine assessment", Notes: "Requested hormone profile.", DaysAgo: 72, Hour: 12, Sync: "Synced"),
            (PatientId: 18, Complaint: "Prenatal check up", Diagnosis: "Stable prenatal findings", Notes: "Continue prenatal visits every 2 weeks.", DaysAgo: 84, Hour: 8, Sync: "Pending")
        };

        var followUpTemplates = checkupTemplates.Select((template, index) =>
        {
            var followUpDaysAgo = Math.Max(2, template.DaysAgo - (8 + (index % 7)));
            return (
                template.PatientId,
                Complaint: $"{template.Complaint} (follow-up)",
                Diagnosis: template.Diagnosis,
                Notes: $"Follow-up visit: {template.Notes}",
                DaysAgo: followUpDaysAgo,
                Hour: 8 + ((template.Hour + index) % 9),
                Sync: index % 2 == 0 ? "Synced" : "Pending");
        }).ToArray();

        var allCheckupTemplates = checkupTemplates.Concat(followUpTemplates).ToList();

        var patientNames = patients.ToDictionary(item => item.Id, item => item.FullName);
        var patientsById = patients.ToDictionary(item => item.Id);
        var records = allCheckupTemplates
            .Select((template, index) => new ClinicalRecord
            {
                Id = index + 1,
                PatientId = template.PatientId,
                PatientName = patientNames[template.PatientId],
                VisitDate = today.AddDays(-template.DaysAgo).AddHours(template.Hour),
                HeightCm = patientsById[template.PatientId].HeightCm,
                WeightKg = patientsById[template.PatientId].WeightKg + (index % 3),
                BloodPressure = index % 5 == 0 ? "130/84" : patientsById[template.PatientId].BloodPressure,
                FetalHeartRate = patientsById[template.PatientId].FetalHeartTone,
                TemperatureC = 36.4m + (index % 4 * 0.2m),
                ChiefComplaint = template.Complaint,
                Diagnosis = template.Diagnosis,
                Notes = template.Notes,
                DoctorName = "Dr. Cruz",
                SyncStatus = template.Sync
            })
            .OrderByDescending(record => record.VisitDate)
            .ToList();

        var latestDemoComplaintByPatientId = records
            .GroupBy(record => record.PatientId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record.VisitDate).First().ChiefComplaint);

        foreach (var patient in patients)
        {
            if (latestDemoComplaintByPatientId.TryGetValue(patient.Id, out var complaint))
            {
                patient.LastCheckupComplaint = complaint;
            }
        }

        var labTests = new[] { "CBC", "Urinalysis", "Transvaginal Ultrasound", "OGTT", "Hemoglobin", "TSH", "Pelvic Ultrasound", "Lipid Profile" };
        var labs = records.Select((record, index) => new LabResult
        {
            Id = index + 1,
            PatientId = record.PatientId,
            ClinicalRecordId = record.Id,
            PatientName = record.PatientName,
            TestName = labTests[index % labTests.Length],
            RequestedDate = record.VisitDate,
            ResultDate = record.VisitDate.AddHours(3),
            CheckUpDate = record.VisitDate,
            CheckUpComplaint = record.ChiefComplaint,
            Status = index % 3 == 0 ? "Reviewed" : "Uploaded",
            FileUrl = samplePdfs[index % samplePdfs.Length]
        }).ToList();

        return new DashboardViewModel
        {
            DatabaseConfigured = includeDatabaseFlag,
            DataNotice = includeDatabaseFlag ? null : "PostgreSQL is not configured yet. Demo data is shown until ConnectionStrings:DefaultConnection points to your database.",
            Patients = patients,
            RecentRecords = records,
            LabResults = labs,
            Prescriptions =
            [
                new()
                {
                    Id = 1,
                    PatientId = 1,
                    ClinicalRecordId = 1,
                    PatientName = "Maria Santos",
                    IssuedAt = DateTime.Today.AddHours(10).AddMinutes(30),
                    Medication = "Prenatal vitamins",
                    Dosage = "1 tablet",
                    Frequency = "Once daily",
                    Duration = "30 days",
                    Instructions = "Take after meals.",
                    Prescriber = "Dr. Cruz",
                    Items =
                    [
                        new() { PrescriptionId = 1, Medication = "Prenatal vitamins", Dosage = "1 tablet", Frequency = "Once daily", Duration = "30 days" }
                    ]
                },
                new()
                {
                    Id = 2,
                    PatientId = 4,
                    ClinicalRecordId = 4,
                    PatientName = "Katrina Reyes",
                    IssuedAt = today.AddDays(-3).AddHours(9).AddMinutes(25),
                    Medication = "Ferrous sulfate",
                    Dosage = "1 capsule",
                    Frequency = "Twice daily",
                    Duration = "30 days",
                    Instructions = "Take after meals with vitamin C.",
                    Prescriber = "Dr. Cruz",
                    Items =
                    [
                        new() { PrescriptionId = 2, Medication = "Ferrous sulfate", Dosage = "1 capsule", Frequency = "Twice daily", Duration = "30 days" },
                        new() { PrescriptionId = 2, Medication = "Calcium carbonate", Dosage = "1 tablet", Frequency = "Once daily", Duration = "30 days" }
                    ]
                },
                new()
                {
                    Id = 3,
                    PatientId = 14,
                    ClinicalRecordId = 14,
                    PatientName = "Denise Alonzo",
                    IssuedAt = today.AddDays(-1).AddHours(9).AddMinutes(30),
                    Medication = "Low-dose aspirin",
                    Dosage = "80 mg",
                    Frequency = "Once daily",
                    Duration = "60 days",
                    Instructions = "Take at bedtime unless advised otherwise.",
                    Prescriber = "Dr. Cruz",
                    Items =
                    [
                        new() { PrescriptionId = 3, Medication = "Low-dose aspirin", Dosage = "80 mg", Frequency = "Once daily", Duration = "60 days" }
                    ]
                }
            ],
            PrescriptionLayout = PrintLayout.Default("Prescription"),
            DiagnosisLayout = PrintLayout.Default("Diagnosis"),
            SyncQueue =
            [
                new() { Id = 1, EntityType = "Patient", EntityId = 1, Operation = "Update", UpdatedAt = DateTime.Today.AddHours(9).AddMinutes(20), Status = "Pending" },
                new() { Id = 2, EntityType = "ClinicalRecord", EntityId = 1, Operation = "Create", UpdatedAt = DateTime.Today.AddHours(10), Status = "Pending" },
                new() { Id = 3, EntityType = "Prescription", EntityId = 1, Operation = "Create", UpdatedAt = DateTime.Today.AddHours(10).AddMinutes(30), SyncedAt = DateTime.Today.AddHours(18), Status = "Synced" }
            ],
            LastSyncLabel = "Today, 6:00 AM"
        };
    }

}



