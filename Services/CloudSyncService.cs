using medrec.Data;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace medrec.Services;

public sealed class CloudSyncService
{
    private readonly SqliteConnectionFactory _sqliteConnections;
    private readonly PostgresConnectionFactory _postgresConnections;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly LocalAppPaths _localPaths;
    private readonly GoogleDriveStorage _googleDrive;

    public CloudSyncService(
        SqliteConnectionFactory sqliteConnections,
        PostgresConnectionFactory postgresConnections,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        LocalAppPaths localPaths,
        GoogleDriveStorage googleDrive)
    {
        _sqliteConnections = sqliteConnections;
        _postgresConnections = postgresConnections;
        _environment = environment;
        _configuration = configuration;
        _localPaths = localPaths;
        _googleDrive = googleDrive;
    }

    public async Task<int> SyncAsync()
    {
        if (!_postgresConnections.IsConfigured)
        {
            throw new InvalidOperationException("Cloud sync is not configured. Set DATABASE_URL or ConnectionStrings:DefaultConnection to your Render external PostgreSQL URL.");
        }

        await using var local = _sqliteConnections.CreateConnection();
        await local.OpenAsync();
        await using var cloud = _postgresConnections.CreateConnection();
        await cloud.OpenAsync();
        await EnsureCloudSchemaAsync(cloud);

        await using var localTransaction = local.BeginTransaction();
        await using var cloudTransaction = await cloud.BeginTransactionAsync();

        var fileUploads = await UploadLocalFilesToGoogleDriveAsync(local, localTransaction);
        var pushed = 0;
        pushed += await PushPatientsAsync(local, localTransaction, cloud, cloudTransaction);
        pushed += await PushClinicalRecordsAsync(local, localTransaction, cloud, cloudTransaction);
        pushed += await PushLabResultsAsync(local, localTransaction, cloud, cloudTransaction);
        pushed += await PushPrescriptionsAsync(local, localTransaction, cloud, cloudTransaction);
        pushed += await PushPrintLayoutsAsync(local, localTransaction, cloud, cloudTransaction);

        var pulled = 0;
        pulled += await PullPatientsAsync(local, localTransaction, cloud, cloudTransaction);
        pulled += await PullClinicalRecordsAsync(local, localTransaction, cloud, cloudTransaction);
        pulled += await PullLabResultsAsync(local, localTransaction, cloud, cloudTransaction);
        pulled += await PullPrescriptionsAsync(local, localTransaction, cloud, cloudTransaction);
        pulled += await PullPrintLayoutsAsync(local, localTransaction, cloud, cloudTransaction);

        await ExecuteLocalAsync(local, localTransaction, "UPDATE sync_queue SET status = 'Synced', synced_at = CURRENT_TIMESTAMP, last_error = NULL WHERE status = 'Pending';");
        await ExecuteLocalAsync(
            local,
            localTransaction,
            "INSERT INTO sync_runs (sync_type, finished_at, records_uploaded, files_uploaded, status, message) VALUES ('Manual', CURRENT_TIMESTAMP, @count, @files, 'Completed', @message);",
            ("@count", pushed),
            ("@files", fileUploads),
            ("@message", $"Cloud sync completed. Pushed {pushed}; pulled {pulled}; uploaded {fileUploads} file(s)."));

        await cloudTransaction.CommitAsync();
        await localTransaction.CommitAsync();
        return pushed + pulled;
    }

    private async Task<int> UploadLocalFilesToGoogleDriveAsync(SqliteConnection local, SqliteTransaction transaction)
    {
        var options = _configuration.GetSection("MedRec").Get<MedRecStorageOptions>() ?? new MedRecStorageOptions();
        if (!options.UseGoogleDriveStorage)
        {
            return 0;
        }

        if (!_googleDrive.IsConfigured)
        {
            throw new InvalidOperationException("Google Drive file sync is enabled, but Google Drive is not configured. Add and test Google Drive settings in Administration.");
        }

        var count = 0;
        count += await UploadLocalFileColumnAsync(local, transaction, "patients", "photo_url", "patients");
        count += await UploadLocalFileColumnAsync(local, transaction, "lab_results", "file_url", "labs");
        count += await UploadLocalFileColumnAsync(local, transaction, "print_layouts", "logo_url", "logos");
        count += await UploadLocalFileColumnAsync(local, transaction, "users", "signature_url", "signatures");
        return count;
    }

    private async Task<int> UploadLocalFileColumnAsync(SqliteConnection local, SqliteTransaction transaction, string table, string column, string folder)
    {
        var rows = new List<(int Id, string Url)>();
        await using (var read = new SqliteCommand($"SELECT id, {column} FROM {table} WHERE {column} LIKE '/local-files/%';", local, transaction))
        await using (var reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((Convert.ToInt32(reader["id"]), Convert.ToString(reader[column]) ?? string.Empty));
            }
        }

        var count = 0;
        foreach (var row in rows)
        {
            var path = ResolveLocalFilePath(row.Url);
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            var driveUrl = await _googleDrive.UploadFileAsync(path, folder);
            await ExecuteLocalAsync(
                local,
                transaction,
                $"UPDATE {table} SET {column} = @url, sync_status = 'Pending', updated_at = CURRENT_TIMESTAMP WHERE id = @id;",
                ("@url", driveUrl),
                ("@id", row.Id));
            count++;
        }

        return count;
    }

    private string? ResolveLocalFilePath(string url)
    {
        const string prefix = "/local-files/";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = Uri.UnescapeDataString(url[prefix.Length..]).Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(_localPaths.FilesRoot);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private async Task EnsureCloudSchemaAsync(NpgsqlConnection cloud)
    {
        var schemaPath = ResolveSchemaPath("schema.sql");
        if (!File.Exists(schemaPath))
        {
            throw new InvalidOperationException("Database/schema.sql was not found.");
        }

        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(schemaPath), cloud);
        await command.ExecuteNonQueryAsync();
    }

    private string ResolveSchemaPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(_environment.ContentRootPath, "Database", fileName),
            Path.Combine(AppContext.BaseDirectory, "Database", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Database", fileName)
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static async Task<int> PushPatientsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string readSql = """
            SELECT client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company,
                   email, partner_name, partner_contact_number, referred_by, age_of_menarche, menopause_age,
                   previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount, menstrual_pattern,
                   sexually_active, contraception_method, height_cm, weight_kg, blood_pressure, fetal_heart_tone,
                   last_menstrual_period, photo_url, archived_at
            FROM patients
            WHERE sync_status = 'Pending';
            """;
        var count = 0;
        await using var read = new SqliteCommand(readSql, local, localTransaction);
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            const string upsertSql = """
                INSERT INTO patients
                  (client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company,
                   email, partner_name, partner_contact_number, referred_by, age_of_menarche, menopause_age,
                   previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount, menstrual_pattern,
                   sexually_active, contraception_method, height_cm, weight_kg, blood_pressure, fetal_heart_tone,
                   last_menstrual_period, photo_url, archived_at, sync_status, last_synced_at)
                VALUES
                  (@uid, @number, @name, @age, @address, @sex, @civil, @contact, @occupation, @company,
                   @email, @partner, @partnerContact, @referred, @menarche, @menopause,
                   @pmp, @cycle, @duration, @amount, @pattern, @active, @contraception, @height, @weight, @bp, @fht,
                   @lmp, @photo, @archived, 'Synced', CURRENT_TIMESTAMP)
                ON CONFLICT (client_uid) DO UPDATE SET
                  patient_number=EXCLUDED.patient_number, full_name=EXCLUDED.full_name, age=EXCLUDED.age,
                  address=EXCLUDED.address, sex=EXCLUDED.sex, civil_status=EXCLUDED.civil_status,
                  contact_number=EXCLUDED.contact_number, occupation=EXCLUDED.occupation, company=EXCLUDED.company,
                  email=EXCLUDED.email, partner_name=EXCLUDED.partner_name, partner_contact_number=EXCLUDED.partner_contact_number,
                  referred_by=EXCLUDED.referred_by, age_of_menarche=EXCLUDED.age_of_menarche, menopause_age=EXCLUDED.menopause_age,
                  previous_menstrual_period=EXCLUDED.previous_menstrual_period, period_cycle_days=EXCLUDED.period_cycle_days,
                  period_duration_days=EXCLUDED.period_duration_days, menstrual_amount=EXCLUDED.menstrual_amount,
                  menstrual_pattern=EXCLUDED.menstrual_pattern, sexually_active=EXCLUDED.sexually_active,
                  contraception_method=EXCLUDED.contraception_method, height_cm=EXCLUDED.height_cm, weight_kg=EXCLUDED.weight_kg,
                  blood_pressure=EXCLUDED.blood_pressure, fetal_heart_tone=EXCLUDED.fetal_heart_tone,
                  last_menstrual_period=EXCLUDED.last_menstrual_period, photo_url=EXCLUDED.photo_url,
                  archived_at=EXCLUDED.archived_at, sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP,
                  updated_at=CURRENT_TIMESTAMP;
                """;
            await using var upsert = new NpgsqlCommand(upsertSql, cloud, cloudTransaction);
            upsert.Parameters.AddWithValue("@uid", Text(reader, "client_uid"));
            upsert.Parameters.AddWithValue("@number", Text(reader, "patient_number"));
            upsert.Parameters.AddWithValue("@name", Text(reader, "full_name"));
            upsert.Parameters.AddWithValue("@age", Int(reader, "age"));
            upsert.Parameters.AddWithValue("@address", Text(reader, "address"));
            upsert.Parameters.AddWithValue("@sex", Text(reader, "sex"));
            upsert.Parameters.AddWithValue("@civil", Text(reader, "civil_status"));
            upsert.Parameters.AddWithValue("@contact", Text(reader, "contact_number"));
            upsert.Parameters.AddWithValue("@occupation", Text(reader, "occupation"));
            upsert.Parameters.AddWithValue("@company", Text(reader, "company"));
            upsert.Parameters.AddWithValue("@email", Text(reader, "email"));
            upsert.Parameters.AddWithValue("@partner", Text(reader, "partner_name"));
            upsert.Parameters.AddWithValue("@partnerContact", Text(reader, "partner_contact_number"));
            upsert.Parameters.AddWithValue("@referred", Text(reader, "referred_by"));
            AddPg(upsert, "@menarche", NullableInt(reader, "age_of_menarche"));
            AddPg(upsert, "@menopause", NullableInt(reader, "menopause_age"));
            AddPg(upsert, "@pmp", SqliteDateOnly(reader, "previous_menstrual_period"));
            AddPg(upsert, "@cycle", NullableInt(reader, "period_cycle_days"));
            AddPg(upsert, "@duration", NullableInt(reader, "period_duration_days"));
            upsert.Parameters.AddWithValue("@amount", Text(reader, "menstrual_amount"));
            upsert.Parameters.AddWithValue("@pattern", Text(reader, "menstrual_pattern"));
            AddPg(upsert, "@active", NullableBool(reader, "sexually_active"));
            upsert.Parameters.AddWithValue("@contraception", Text(reader, "contraception_method"));
            AddPg(upsert, "@height", NullableDecimal(reader, "height_cm"));
            AddPg(upsert, "@weight", NullableDecimal(reader, "weight_kg"));
            upsert.Parameters.AddWithValue("@bp", Text(reader, "blood_pressure"));
            upsert.Parameters.AddWithValue("@fht", Text(reader, "fetal_heart_tone"));
            AddPg(upsert, "@lmp", SqliteDateOnly(reader, "last_menstrual_period"));
            AddPg(upsert, "@photo", TextOrNull(reader, "photo_url"));
            AddPg(upsert, "@archived", DateTimeValue(reader, "archived_at"));
            await upsert.ExecuteNonQueryAsync();
            count++;
        }

        await ExecuteLocalAsync(local, localTransaction, "UPDATE patients SET sync_status = 'Synced', last_synced_at = CURRENT_TIMESTAMP WHERE sync_status = 'Pending';");
        return count;
    }

    private static async Task<int> PushClinicalRecordsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string readSql = """
            SELECT r.client_uid, p.client_uid AS patient_uid, r.visit_date, r.height_cm, r.weight_kg, r.blood_pressure,
                   r.fetal_heart_rate, r.temperature_c, r.chief_complaint, r.diagnosis, r.notes, r.doctor_name
            FROM clinical_records r
            INNER JOIN patients p ON p.id = r.patient_id
            WHERE r.sync_status = 'Pending';
            """;
        var count = 0;
        await using var read = new SqliteCommand(readSql, local, localTransaction);
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            const string sql = """
                INSERT INTO clinical_records
                  (client_uid, patient_id, visit_date, height_cm, weight_kg, blood_pressure, fetal_heart_rate, temperature_c,
                   chief_complaint, diagnosis, notes, doctor_name, sync_status, last_synced_at)
                SELECT @uid, p.id, @visitDate, @height, @weight, @bp, @fhr, @temperature, @complaint, @diagnosis,
                       @notes, @doctorName, 'Synced', CURRENT_TIMESTAMP
                FROM patients p
                WHERE p.client_uid = @patientUid
                ON CONFLICT (client_uid) DO UPDATE SET
                  patient_id=EXCLUDED.patient_id, visit_date=EXCLUDED.visit_date, height_cm=EXCLUDED.height_cm,
                  weight_kg=EXCLUDED.weight_kg, blood_pressure=EXCLUDED.blood_pressure,
                  fetal_heart_rate=EXCLUDED.fetal_heart_rate, temperature_c=EXCLUDED.temperature_c,
                  chief_complaint=EXCLUDED.chief_complaint, diagnosis=EXCLUDED.diagnosis, notes=EXCLUDED.notes,
                  doctor_name=EXCLUDED.doctor_name, sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP,
                  updated_at=CURRENT_TIMESTAMP;
                """;
            await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
            command.Parameters.AddWithValue("@uid", Text(reader, "client_uid"));
            command.Parameters.AddWithValue("@patientUid", Text(reader, "patient_uid"));
            command.Parameters.AddWithValue("@visitDate", DateTimeValue(reader, "visit_date") ?? DateTime.Now);
            AddPg(command, "@height", NullableDecimal(reader, "height_cm"));
            AddPg(command, "@weight", NullableDecimal(reader, "weight_kg"));
            command.Parameters.AddWithValue("@bp", Text(reader, "blood_pressure"));
            command.Parameters.AddWithValue("@fhr", Text(reader, "fetal_heart_rate"));
            AddPg(command, "@temperature", NullableDecimal(reader, "temperature_c"));
            command.Parameters.AddWithValue("@complaint", Text(reader, "chief_complaint"));
            command.Parameters.AddWithValue("@diagnosis", Text(reader, "diagnosis"));
            command.Parameters.AddWithValue("@notes", Text(reader, "notes"));
            command.Parameters.AddWithValue("@doctorName", Text(reader, "doctor_name"));
            count += await command.ExecuteNonQueryAsync() > 0 ? 1 : 0;
        }

        await ExecuteLocalAsync(local, localTransaction, "UPDATE clinical_records SET sync_status = 'Synced', last_synced_at = CURRENT_TIMESTAMP WHERE sync_status = 'Pending';");
        return count;
    }

    private static async Task<int> PushLabResultsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string readSql = """
            SELECT l.client_uid, p.client_uid AS patient_uid, r.client_uid AS record_uid, l.test_name, l.requested_date,
                   l.result_date, l.status, l.file_url, l.notes
            FROM lab_results l
            INNER JOIN patients p ON p.id = l.patient_id
            LEFT JOIN clinical_records r ON r.id = l.clinical_record_id
            WHERE l.sync_status = 'Pending';
            """;
        var count = 0;
        await using var read = new SqliteCommand(readSql, local, localTransaction);
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            const string sql = """
                INSERT INTO lab_results
                  (client_uid, patient_id, clinical_record_id, test_name, requested_date, result_date, status, file_url, notes, sync_status)
                SELECT @uid, p.id, cr.id, @testName, @requestedDate, @resultDate, @status, @fileUrl, @notes, 'Synced'
                FROM patients p
                LEFT JOIN clinical_records cr ON cr.client_uid = @recordUid
                WHERE p.client_uid = @patientUid
                ON CONFLICT (client_uid) DO UPDATE SET
                  patient_id=EXCLUDED.patient_id, clinical_record_id=EXCLUDED.clinical_record_id, test_name=EXCLUDED.test_name,
                  requested_date=EXCLUDED.requested_date, result_date=EXCLUDED.result_date, status=EXCLUDED.status,
                  file_url=EXCLUDED.file_url, notes=EXCLUDED.notes, sync_status='Synced', updated_at=CURRENT_TIMESTAMP;
                """;
            await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
            command.Parameters.AddWithValue("@uid", Text(reader, "client_uid"));
            command.Parameters.AddWithValue("@patientUid", Text(reader, "patient_uid"));
            AddPg(command, "@recordUid", TextOrNull(reader, "record_uid"));
            command.Parameters.AddWithValue("@testName", Text(reader, "test_name"));
            command.Parameters.AddWithValue("@requestedDate", DateTimeValue(reader, "requested_date") ?? DateTime.Now);
            command.Parameters.AddWithValue("@resultDate", DateTimeValue(reader, "result_date") ?? DateTime.Now);
            command.Parameters.AddWithValue("@status", Text(reader, "status"));
            command.Parameters.AddWithValue("@fileUrl", Text(reader, "file_url"));
            AddPg(command, "@notes", TextOrNull(reader, "notes"));
            count += await command.ExecuteNonQueryAsync() > 0 ? 1 : 0;
        }

        await ExecuteLocalAsync(local, localTransaction, "UPDATE lab_results SET sync_status = 'Synced' WHERE sync_status = 'Pending';");
        return count;
    }

    private static async Task<int> PushPrescriptionsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string readSql = """
            SELECT pr.id, pr.client_uid, p.client_uid AS patient_uid, r.client_uid AS record_uid, pr.issued_at, pr.medication,
                   pr.dosage, pr.frequency, pr.duration, pr.instructions, pr.prescriber, pr.print_count
            FROM prescriptions pr
            INNER JOIN patients p ON p.id = pr.patient_id
            LEFT JOIN clinical_records r ON r.id = pr.clinical_record_id
            WHERE pr.sync_status = 'Pending';
            """;
        var count = 0;
        await using var read = new SqliteCommand(readSql, local, localTransaction);
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            const string sql = """
                INSERT INTO prescriptions
                  (client_uid, patient_id, clinical_record_id, issued_at, medication, dosage, frequency, duration,
                   instructions, prescriber, print_count, sync_status)
                SELECT @uid, p.id, cr.id, @issuedAt, @medication, @dosage, @frequency, @duration, @instructions,
                       @prescriber, @printCount, 'Synced'
                FROM patients p
                LEFT JOIN clinical_records cr ON cr.client_uid = @recordUid
                WHERE p.client_uid = @patientUid
                ON CONFLICT (client_uid) DO UPDATE SET
                  patient_id=EXCLUDED.patient_id, clinical_record_id=EXCLUDED.clinical_record_id, issued_at=EXCLUDED.issued_at,
                  medication=EXCLUDED.medication, dosage=EXCLUDED.dosage, frequency=EXCLUDED.frequency,
                  duration=EXCLUDED.duration, instructions=EXCLUDED.instructions, prescriber=EXCLUDED.prescriber,
                  print_count=EXCLUDED.print_count, sync_status='Synced', updated_at=CURRENT_TIMESTAMP
                RETURNING id;
                """;
            await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
            command.Parameters.AddWithValue("@uid", Text(reader, "client_uid"));
            command.Parameters.AddWithValue("@patientUid", Text(reader, "patient_uid"));
            AddPg(command, "@recordUid", TextOrNull(reader, "record_uid"));
            command.Parameters.AddWithValue("@issuedAt", DateTimeValue(reader, "issued_at") ?? DateTime.Now);
            command.Parameters.AddWithValue("@medication", Text(reader, "medication"));
            command.Parameters.AddWithValue("@dosage", Text(reader, "dosage"));
            command.Parameters.AddWithValue("@frequency", Text(reader, "frequency"));
            command.Parameters.AddWithValue("@duration", Text(reader, "duration"));
            AddPg(command, "@instructions", TextOrNull(reader, "instructions"));
            command.Parameters.AddWithValue("@prescriber", Text(reader, "prescriber"));
            command.Parameters.AddWithValue("@printCount", Int(reader, "print_count"));
            var cloudPrescriptionId = Convert.ToInt32(await command.ExecuteScalarAsync());
            await PushPrescriptionItemsAsync(local, localTransaction, cloud, cloudTransaction, Int(reader, "id"), cloudPrescriptionId);
            count++;
        }

        await ExecuteLocalAsync(local, localTransaction, "UPDATE prescriptions SET sync_status = 'Synced' WHERE sync_status = 'Pending';");
        return count;
    }

    private static async Task PushPrescriptionItemsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction, int localPrescriptionId, int cloudPrescriptionId)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM prescription_items WHERE prescription_id = @id;", cloud, cloudTransaction))
        {
            delete.Parameters.AddWithValue("@id", cloudPrescriptionId);
            await delete.ExecuteNonQueryAsync();
        }

        await using var read = new SqliteCommand("SELECT medication, dosage, frequency, duration, sort_order FROM prescription_items WHERE prescription_id = @id ORDER BY sort_order, id;", local, localTransaction);
        read.Parameters.AddWithValue("@id", localPrescriptionId);
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            await using var insert = new NpgsqlCommand("INSERT INTO prescription_items (prescription_id, medication, dosage, frequency, duration, sort_order) VALUES (@id, @med, @dose, @freq, @duration, @sort);", cloud, cloudTransaction);
            insert.Parameters.AddWithValue("@id", cloudPrescriptionId);
            insert.Parameters.AddWithValue("@med", Text(reader, "medication"));
            insert.Parameters.AddWithValue("@dose", Text(reader, "dosage"));
            insert.Parameters.AddWithValue("@freq", Text(reader, "frequency"));
            insert.Parameters.AddWithValue("@duration", Text(reader, "duration"));
            insert.Parameters.AddWithValue("@sort", Int(reader, "sort_order"));
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static async Task<int> PushPrintLayoutsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        await using var read = new SqliteCommand("SELECT id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule, clinic_address, logo_url, logo_position, details_alignment, signatory_name, signatory_title, layout_json FROM print_layouts WHERE sync_status = 'Pending';", local, localTransaction);
        await using var reader = await read.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            const string sql = """
                INSERT INTO print_layouts
                  (id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule, clinic_address,
                   logo_url, logo_position, details_alignment, signatory_name, signatory_title, layout_json)
                VALUES
                  (@id, @documentType, @documentTitle, @clinicName, @doctorName, @licenseNumber, @clinicSchedule, @clinicAddress,
                   @logoUrl, @logoPosition, @detailsAlignment, @signatoryName, @signatoryTitle, @layoutJson::jsonb)
                ON CONFLICT (document_type) DO UPDATE SET
                  document_title=EXCLUDED.document_title, clinic_name=EXCLUDED.clinic_name, doctor_name=EXCLUDED.doctor_name,
                  license_number=EXCLUDED.license_number, clinic_schedule=EXCLUDED.clinic_schedule,
                  clinic_address=EXCLUDED.clinic_address, logo_url=EXCLUDED.logo_url, logo_position=EXCLUDED.logo_position,
                  details_alignment=EXCLUDED.details_alignment, signatory_name=EXCLUDED.signatory_name,
                  signatory_title=EXCLUDED.signatory_title, layout_json=EXCLUDED.layout_json, updated_at=CURRENT_TIMESTAMP;
                """;
            await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
            command.Parameters.AddWithValue("@id", Int(reader, "id"));
            command.Parameters.AddWithValue("@documentType", Text(reader, "document_type"));
            command.Parameters.AddWithValue("@documentTitle", Text(reader, "document_title"));
            command.Parameters.AddWithValue("@clinicName", Text(reader, "clinic_name"));
            command.Parameters.AddWithValue("@doctorName", Text(reader, "doctor_name"));
            command.Parameters.AddWithValue("@licenseNumber", Text(reader, "license_number"));
            command.Parameters.AddWithValue("@clinicSchedule", Text(reader, "clinic_schedule"));
            command.Parameters.AddWithValue("@clinicAddress", Text(reader, "clinic_address"));
            AddPg(command, "@logoUrl", TextOrNull(reader, "logo_url"));
            command.Parameters.AddWithValue("@logoPosition", Text(reader, "logo_position"));
            command.Parameters.AddWithValue("@detailsAlignment", Text(reader, "details_alignment"));
            command.Parameters.AddWithValue("@signatoryName", Text(reader, "signatory_name"));
            command.Parameters.AddWithValue("@signatoryTitle", Text(reader, "signatory_title"));
            AddPg(command, "@layoutJson", TextOrNull(reader, "layout_json"));
            await command.ExecuteNonQueryAsync();
            count++;
        }

        await ExecuteLocalAsync(local, localTransaction, "UPDATE print_layouts SET sync_status = 'Synced', last_synced_at = CURRENT_TIMESTAMP WHERE sync_status = 'Pending';");
        return count;
    }

    private static async Task<int> PullPatientsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string sql = "SELECT client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company, email, partner_name, partner_contact_number, referred_by, age_of_menarche, menopause_age, previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount, menstrual_pattern, sexually_active, contraception_method, height_cm, weight_kg, blood_pressure, fetal_heart_tone, last_menstrual_period, photo_url, archived_at, updated_at FROM patients;";
        var count = 0;
        await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            await ExecuteLocalAsync(
                local,
                localTransaction,
                """
                INSERT INTO patients
                  (client_uid, patient_number, full_name, age, address, sex, civil_status, contact_number, occupation, company,
                   email, partner_name, partner_contact_number, referred_by, age_of_menarche, menopause_age,
                   previous_menstrual_period, period_cycle_days, period_duration_days, menstrual_amount, menstrual_pattern,
                   sexually_active, contraception_method, height_cm, weight_kg, blood_pressure, fetal_heart_tone,
                   last_menstrual_period, photo_url, archived_at, sync_status, last_synced_at, updated_at)
                VALUES
                  (@uid, @number, @name, @age, @address, @sex, @civil, @contact, @occupation, @company,
                   @email, @partner, @partnerContact, @referred, @menarche, @menopause,
                   @pmp, @cycle, @duration, @amount, @pattern, @active, @contraception, @height, @weight, @bp, @fht,
                   @lmp, @photo, @archived, 'Synced', CURRENT_TIMESTAMP, @updated)
                ON CONFLICT(client_uid) DO UPDATE SET
                  patient_number=excluded.patient_number, full_name=excluded.full_name, age=excluded.age, address=excluded.address,
                  sex=excluded.sex, civil_status=excluded.civil_status, contact_number=excluded.contact_number,
                  occupation=excluded.occupation, company=excluded.company, email=excluded.email, partner_name=excluded.partner_name,
                  partner_contact_number=excluded.partner_contact_number, referred_by=excluded.referred_by,
                  age_of_menarche=excluded.age_of_menarche, menopause_age=excluded.menopause_age,
                  previous_menstrual_period=excluded.previous_menstrual_period, period_cycle_days=excluded.period_cycle_days,
                  period_duration_days=excluded.period_duration_days, menstrual_amount=excluded.menstrual_amount,
                  menstrual_pattern=excluded.menstrual_pattern, sexually_active=excluded.sexually_active,
                  contraception_method=excluded.contraception_method, height_cm=excluded.height_cm, weight_kg=excluded.weight_kg,
                  blood_pressure=excluded.blood_pressure, fetal_heart_tone=excluded.fetal_heart_tone,
                  last_menstrual_period=excluded.last_menstrual_period, photo_url=excluded.photo_url,
                  archived_at=excluded.archived_at, sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP,
                  updated_at=excluded.updated_at;
                """,
                ("@uid", PgText(reader, "client_uid")),
                ("@number", PgText(reader, "patient_number")),
                ("@name", PgText(reader, "full_name")),
                ("@age", PgInt(reader, "age")),
                ("@address", PgText(reader, "address")),
                ("@sex", PgText(reader, "sex")),
                ("@civil", PgText(reader, "civil_status")),
                ("@contact", PgText(reader, "contact_number")),
                ("@occupation", PgText(reader, "occupation")),
                ("@company", PgText(reader, "company")),
                ("@email", PgText(reader, "email")),
                ("@partner", PgText(reader, "partner_name")),
                ("@partnerContact", PgText(reader, "partner_contact_number")),
                ("@referred", PgText(reader, "referred_by")),
                ("@menarche", PgNullableInt(reader, "age_of_menarche")),
                ("@menopause", PgNullableInt(reader, "menopause_age")),
                ("@pmp", PgDateText(reader, "previous_menstrual_period")),
                ("@cycle", PgNullableInt(reader, "period_cycle_days")),
                ("@duration", PgNullableInt(reader, "period_duration_days")),
                ("@amount", PgText(reader, "menstrual_amount")),
                ("@pattern", PgText(reader, "menstrual_pattern")),
                ("@active", PgNullableBoolAsInt(reader, "sexually_active")),
                ("@contraception", PgText(reader, "contraception_method")),
                ("@height", PgNullableDecimal(reader, "height_cm")),
                ("@weight", PgNullableDecimal(reader, "weight_kg")),
                ("@bp", PgText(reader, "blood_pressure")),
                ("@fht", PgText(reader, "fetal_heart_tone")),
                ("@lmp", PgDateText(reader, "last_menstrual_period")),
                ("@photo", PgTextOrNull(reader, "photo_url")),
                ("@archived", PgDateTimeText(reader, "archived_at")),
                ("@updated", PgDateTimeText(reader, "updated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            count++;
        }

        return count;
    }

    private static async Task<int> PullClinicalRecordsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string sql = "SELECT r.client_uid, p.client_uid AS patient_uid, r.visit_date, r.height_cm, r.weight_kg, r.blood_pressure, r.fetal_heart_rate, r.temperature_c, r.chief_complaint, r.diagnosis, r.notes, r.doctor_name, r.updated_at FROM clinical_records r INNER JOIN patients p ON p.id = r.patient_id;";
        var count = 0;
        await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var localPatientId = await LocalIdAsync(local, localTransaction, "patients", PgText(reader, "patient_uid"));
            if (!localPatientId.HasValue) continue;
            await ExecuteLocalAsync(local, localTransaction, """
                INSERT INTO clinical_records
                  (client_uid, patient_id, visit_date, height_cm, weight_kg, blood_pressure, fetal_heart_rate, temperature_c,
                   chief_complaint, diagnosis, notes, doctor_name, sync_status, last_synced_at, updated_at)
                VALUES (@uid, @patientId, @visitDate, @height, @weight, @bp, @fhr, @temperature, @complaint, @diagnosis, @notes, @doctorName, 'Synced', CURRENT_TIMESTAMP, @updated)
                ON CONFLICT(client_uid) DO UPDATE SET
                  patient_id=excluded.patient_id, visit_date=excluded.visit_date, height_cm=excluded.height_cm,
                  weight_kg=excluded.weight_kg, blood_pressure=excluded.blood_pressure, fetal_heart_rate=excluded.fetal_heart_rate,
                  temperature_c=excluded.temperature_c, chief_complaint=excluded.chief_complaint, diagnosis=excluded.diagnosis,
                  notes=excluded.notes, doctor_name=excluded.doctor_name, sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP,
                  updated_at=excluded.updated_at;
                """,
                ("@uid", PgText(reader, "client_uid")),
                ("@patientId", localPatientId.Value),
                ("@visitDate", PgDateTimeText(reader, "visit_date") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                ("@height", PgNullableDecimal(reader, "height_cm")),
                ("@weight", PgNullableDecimal(reader, "weight_kg")),
                ("@bp", PgText(reader, "blood_pressure")),
                ("@fhr", PgText(reader, "fetal_heart_rate")),
                ("@temperature", PgNullableDecimal(reader, "temperature_c")),
                ("@complaint", PgText(reader, "chief_complaint")),
                ("@diagnosis", PgText(reader, "diagnosis")),
                ("@notes", PgText(reader, "notes")),
                ("@doctorName", PgText(reader, "doctor_name")),
                ("@updated", PgDateTimeText(reader, "updated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            count++;
        }
        return count;
    }

    private static async Task<int> PullLabResultsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string sql = "SELECT l.client_uid, p.client_uid AS patient_uid, r.client_uid AS record_uid, l.test_name, l.requested_date, l.result_date, l.status, l.file_url, l.notes, l.updated_at FROM lab_results l INNER JOIN patients p ON p.id = l.patient_id LEFT JOIN clinical_records r ON r.id = l.clinical_record_id;";
        var count = 0;
        await using var command = new NpgsqlCommand(sql, cloud, cloudTransaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var localPatientId = await LocalIdAsync(local, localTransaction, "patients", PgText(reader, "patient_uid"));
            if (!localPatientId.HasValue) continue;
            var localRecordId = string.IsNullOrWhiteSpace(PgTextOrNull(reader, "record_uid")) ? null : await LocalIdAsync(local, localTransaction, "clinical_records", PgText(reader, "record_uid"));
            await ExecuteLocalAsync(local, localTransaction, """
                INSERT INTO lab_results
                  (client_uid, patient_id, clinical_record_id, test_name, requested_date, result_date, status, file_url, notes, sync_status, updated_at)
                VALUES (@uid, @patientId, @recordId, @testName, @requestedDate, @resultDate, @status, @fileUrl, @notes, 'Synced', @updated)
                ON CONFLICT(client_uid) DO UPDATE SET
                  patient_id=excluded.patient_id, clinical_record_id=excluded.clinical_record_id, test_name=excluded.test_name,
                  requested_date=excluded.requested_date, result_date=excluded.result_date, status=excluded.status,
                  file_url=excluded.file_url, notes=excluded.notes, sync_status='Synced', updated_at=excluded.updated_at;
                """,
                ("@uid", PgText(reader, "client_uid")),
                ("@patientId", localPatientId.Value),
                ("@recordId", localRecordId),
                ("@testName", PgText(reader, "test_name")),
                ("@requestedDate", PgDateTimeText(reader, "requested_date") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                ("@resultDate", PgDateTimeText(reader, "result_date") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                ("@status", PgText(reader, "status")),
                ("@fileUrl", PgText(reader, "file_url")),
                ("@notes", PgTextOrNull(reader, "notes")),
                ("@updated", PgDateTimeText(reader, "updated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            count++;
        }
        return count;
    }

    private static async Task<int> PullPrescriptionsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        const string sql = "SELECT pr.id, pr.client_uid, p.client_uid AS patient_uid, r.client_uid AS record_uid, pr.issued_at, pr.medication, pr.dosage, pr.frequency, pr.duration, pr.instructions, pr.prescriber, pr.print_count, pr.updated_at FROM prescriptions pr INNER JOIN patients p ON p.id = pr.patient_id LEFT JOIN clinical_records r ON r.id = pr.clinical_record_id;";
        var prescriptions = new List<CloudPrescriptionRow>();
        var count = 0;

        await using (var command = new NpgsqlCommand(sql, cloud, cloudTransaction))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                prescriptions.Add(new CloudPrescriptionRow(
                    PgInt(reader, "id"),
                    PgText(reader, "client_uid"),
                    PgText(reader, "patient_uid"),
                    PgTextOrNull(reader, "record_uid"),
                    PgDateTimeText(reader, "issued_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    PgText(reader, "medication"),
                    PgText(reader, "dosage"),
                    PgText(reader, "frequency"),
                    PgText(reader, "duration"),
                    PgTextOrNull(reader, "instructions"),
                    PgText(reader, "prescriber"),
                    PgInt(reader, "print_count"),
                    PgDateTimeText(reader, "updated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            }
        }

        foreach (var prescription in prescriptions)
        {
            var localPatientId = await LocalIdAsync(local, localTransaction, "patients", prescription.PatientUid);
            if (!localPatientId.HasValue) continue;
            var localRecordId = string.IsNullOrWhiteSpace(prescription.RecordUid) ? null : await LocalIdAsync(local, localTransaction, "clinical_records", prescription.RecordUid);
            await ExecuteLocalAsync(local, localTransaction, """
                INSERT INTO prescriptions
                  (client_uid, patient_id, clinical_record_id, issued_at, medication, dosage, frequency, duration, instructions, prescriber, print_count, sync_status, updated_at)
                VALUES (@uid, @patientId, @recordId, @issuedAt, @medication, @dosage, @frequency, @duration, @instructions, @prescriber, @printCount, 'Synced', @updated)
                ON CONFLICT(client_uid) DO UPDATE SET
                  patient_id=excluded.patient_id, clinical_record_id=excluded.clinical_record_id, issued_at=excluded.issued_at,
                  medication=excluded.medication, dosage=excluded.dosage, frequency=excluded.frequency, duration=excluded.duration,
                  instructions=excluded.instructions, prescriber=excluded.prescriber, print_count=excluded.print_count,
                  sync_status='Synced', updated_at=excluded.updated_at;
                """,
                ("@uid", prescription.ClientUid),
                ("@patientId", localPatientId.Value),
                ("@recordId", localRecordId),
                ("@issuedAt", prescription.IssuedAt),
                ("@medication", prescription.Medication),
                ("@dosage", prescription.Dosage),
                ("@frequency", prescription.Frequency),
                ("@duration", prescription.Duration),
                ("@instructions", prescription.Instructions),
                ("@prescriber", prescription.Prescriber),
                ("@printCount", prescription.PrintCount),
                ("@updated", prescription.UpdatedAt));
            var localPrescriptionId = await LocalIdAsync(local, localTransaction, "prescriptions", prescription.ClientUid);
            if (localPrescriptionId.HasValue)
            {
                await PullPrescriptionItemsAsync(local, localTransaction, cloud, cloudTransaction, prescription.Id, localPrescriptionId.Value);
            }
            count++;
        }
        return count;
    }

    private sealed record CloudPrescriptionRow(
        int Id,
        string ClientUid,
        string PatientUid,
        string? RecordUid,
        string IssuedAt,
        string Medication,
        string Dosage,
        string Frequency,
        string Duration,
        string? Instructions,
        string Prescriber,
        int PrintCount,
        string UpdatedAt);

    private static async Task PullPrescriptionItemsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction, int cloudPrescriptionId, int localPrescriptionId)
    {
        await ExecuteLocalAsync(local, localTransaction, "DELETE FROM prescription_items WHERE prescription_id = @id;", ("@id", localPrescriptionId));
        await using var command = new NpgsqlCommand("SELECT medication, dosage, frequency, duration, sort_order FROM prescription_items WHERE prescription_id = @id ORDER BY sort_order, id;", cloud, cloudTransaction);
        command.Parameters.AddWithValue("@id", cloudPrescriptionId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            await ExecuteLocalAsync(
                local,
                localTransaction,
                "INSERT INTO prescription_items (prescription_id, medication, dosage, frequency, duration, sort_order) VALUES (@id, @med, @dose, @freq, @duration, @sort);",
                ("@id", localPrescriptionId),
                ("@med", PgText(reader, "medication")),
                ("@dose", PgText(reader, "dosage")),
                ("@freq", PgText(reader, "frequency")),
                ("@duration", PgText(reader, "duration")),
                ("@sort", PgInt(reader, "sort_order")));
        }
    }

    private static async Task<int> PullPrintLayoutsAsync(SqliteConnection local, SqliteTransaction localTransaction, NpgsqlConnection cloud, NpgsqlTransaction cloudTransaction)
    {
        var count = 0;
        await using var command = new NpgsqlCommand("SELECT id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule, clinic_address, logo_url, logo_position, details_alignment, signatory_name, signatory_title, layout_json, updated_at FROM print_layouts;", cloud, cloudTransaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            await ExecuteLocalAsync(local, localTransaction, """
                INSERT INTO print_layouts
                  (id, document_type, document_title, clinic_name, doctor_name, license_number, clinic_schedule, clinic_address,
                   logo_url, logo_position, details_alignment, signatory_name, signatory_title, layout_json, sync_status, last_synced_at, updated_at)
                VALUES (@id, @documentType, @documentTitle, @clinicName, @doctorName, @licenseNumber, @clinicSchedule, @clinicAddress,
                        @logoUrl, @logoPosition, @detailsAlignment, @signatoryName, @signatoryTitle, @layoutJson, 'Synced', CURRENT_TIMESTAMP, @updated)
                ON CONFLICT(document_type) DO UPDATE SET
                  document_title=excluded.document_title, clinic_name=excluded.clinic_name, doctor_name=excluded.doctor_name,
                  license_number=excluded.license_number, clinic_schedule=excluded.clinic_schedule, clinic_address=excluded.clinic_address,
                  logo_url=excluded.logo_url, logo_position=excluded.logo_position, details_alignment=excluded.details_alignment,
                  signatory_name=excluded.signatory_name, signatory_title=excluded.signatory_title, layout_json=excluded.layout_json,
                  sync_status='Synced', last_synced_at=CURRENT_TIMESTAMP, updated_at=excluded.updated_at;
                """,
                ("@id", PgInt(reader, "id")),
                ("@documentType", PgText(reader, "document_type")),
                ("@documentTitle", PgText(reader, "document_title")),
                ("@clinicName", PgText(reader, "clinic_name")),
                ("@doctorName", PgText(reader, "doctor_name")),
                ("@licenseNumber", PgText(reader, "license_number")),
                ("@clinicSchedule", PgText(reader, "clinic_schedule")),
                ("@clinicAddress", PgText(reader, "clinic_address")),
                ("@logoUrl", PgTextOrNull(reader, "logo_url")),
                ("@logoPosition", PgText(reader, "logo_position")),
                ("@detailsAlignment", PgText(reader, "details_alignment")),
                ("@signatoryName", PgText(reader, "signatory_name")),
                ("@signatoryTitle", PgText(reader, "signatory_title")),
                ("@layoutJson", PgTextOrNull(reader, "layout_json")),
                ("@updated", PgDateTimeText(reader, "updated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            count++;
        }
        return count;
    }

    private static async Task<int?> LocalIdAsync(SqliteConnection local, SqliteTransaction transaction, string table, string clientUid)
    {
        await using var command = new SqliteCommand($"SELECT id FROM {table} WHERE client_uid = @uid LIMIT 1;", local, transaction);
        command.Parameters.AddWithValue("@uid", clientUid);
        var value = await command.ExecuteScalarAsync();
        return value is null || value is DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<int> ExecuteLocalAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new SqliteCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
        return await command.ExecuteNonQueryAsync();
    }

    private static void AddPg(NpgsqlCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Text(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static string? TextOrNull(SqliteDataReader reader, string name)
    {
        var value = Text(reader, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int Int(SqliteDataReader reader, string name) => Convert.ToInt32(reader.GetValue(reader.GetOrdinal(name)));

    private static int? NullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal? NullableDecimal(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static bool? NullableBool(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal)) != 0;
    }

    private static DateOnly? SqliteDateOnly(SqliteDataReader reader, string name)
    {
        var value = Text(reader, name);
        return DateTime.TryParse(value, out var parsed) ? DateOnly.FromDateTime(parsed) : null;
    }

    private static DateTime? DateTimeValue(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) return null;
        return DateTime.TryParse(Convert.ToString(reader.GetValue(ordinal)), out var parsed) ? parsed : null;
    }

    private static string PgText(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static string? PgTextOrNull(NpgsqlDataReader reader, string name)
    {
        var value = PgText(reader, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int PgInt(NpgsqlDataReader reader, string name) => Convert.ToInt32(reader.GetValue(reader.GetOrdinal(name)));

    private static int? PgNullableInt(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal? PgNullableDecimal(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static int? PgNullableBoolAsInt(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal) ? 1 : 0;
    }

    private static string? PgDateText(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value is DateOnly dateOnly
            ? dateOnly.ToString("yyyy-MM-dd")
            : Convert.ToDateTime(value).ToString("yyyy-MM-dd");
    }

    private static string? PgDateTimeText(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal)).ToString("yyyy-MM-dd HH:mm:ss");
    }
}
