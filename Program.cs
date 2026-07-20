using medrec.Data;
using medrec.Models;
using medrec.Services;
using medrec.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Npgsql;

Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

var builder = WebApplication.CreateBuilder(args);
var runtimeSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "runtime-settings.json");
Directory.CreateDirectory(Path.GetDirectoryName(runtimeSettingsPath)!);
builder.Configuration.AddJsonFile(runtimeSettingsPath, optional: true, reloadOnChange: false);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(7);
});
var dataProtectionKeys = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "data-protection-keys"));
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MedRec")
    .PersistKeysToFileSystem(dataProtectionKeys);
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}
builder.Services.AddSingleton<PostgresConnectionFactory>();
builder.Services.AddSingleton<LocalAppPaths>();
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddScoped<PostgresEmrRepository>();
builder.Services.AddScoped<SqliteEmrRepository>();
builder.Services.AddScoped<EmrRepository>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<GoogleDriveStorage>();
builder.Services.AddSingleton<UploadStorage>();
builder.Services.AddSingleton<RuntimeSettingsService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<OfflineSyncService>();
builder.Services.AddScoped<CloudSyncService>();
builder.Services.AddHostedService<CloudRuntimeSettingsRefreshService>();
builder.Services.AddHostedService<DailySyncService>();

var app = builder.Build();

await EnsurePostgresSchemaAsync(app);
await LoadCloudRuntimeSettingsAsync(app);
await EnsureSqliteSchemaAsync(app);
await EnsureDefaultPrintLayoutsAsync(app);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/offline.html"))
    {
        context.Response.Redirect("/");
        return;
    }

    if (context.Request.Query.TryGetValue("offline", out var offlineMode)
        && offlineMode.Contains("1"))
    {
        var remainingQuery = context.Request.Query
            .Where(item => !item.Key.Equals("offline", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.Key, item => (string?)item.Value.ToString());
        var redirectUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            context.Request.Path.ToString(),
            remainingQuery);
        context.Response.Redirect(redirectUrl);
        return;
    }

    await next();
});
app.UseStaticFiles();
if (app.Configuration.GetSection("MedRec").Get<MedRecStorageOptions>()?.UseLocalStorage == true)
{
    var localPaths = app.Services.GetRequiredService<LocalAppPaths>();
    localPaths.EnsureCreated();
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(localPaths.FilesRoot),
        RequestPath = "/local-files"
    });
}
app.UseRouting();
app.UseSession();

app.Use(async (context, next) =>
{
    if (IsPublicRequest(context)
        || context.Session.GetInt32("UserId").HasValue)
    {
        await next();
        return;
    }

    var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
    context.Response.Redirect($"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
});

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static bool IsPublicRequest(HttpContext context)
{
    var path = context.Request.Path;
    var value = path.Value ?? string.Empty;

    return path.StartsWithSegments("/Account/Login")
        || path.StartsWithSegments("/Account/Logout")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/lib")
        || path.StartsWithSegments("/uploads")
        || path.StartsWithSegments("/files")
        || value.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase)
        || value.Equals("/service-worker.js", StringComparison.OrdinalIgnoreCase)
        || value.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
        || System.IO.Path.HasExtension(value);
}

static async Task EnsurePostgresSchemaAsync(WebApplication app)
{
    try
    {
        if (app.Configuration.GetSection("MedRec").Get<MedRecStorageOptions>()?.UseLocalStorage == true)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<PostgresConnectionFactory>();
        if (!connections.IsConfigured)
        {
            return;
        }

        var schemaPath = Path.Combine(app.Environment.ContentRootPath, "Database", "schema.sql");
        if (!File.Exists(schemaPath))
        {
            app.Logger.LogWarning("PostgreSQL schema file was not found at {SchemaPath}.", schemaPath);
            return;
        }

        await using var connection = connections.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(schemaPath), connection);
        await command.ExecuteNonQueryAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Unable to initialize the PostgreSQL schema. The app will continue and show demo data if live tables are unavailable.");
    }
}

static async Task LoadCloudRuntimeSettingsAsync(WebApplication app)
{
    try
    {
        if (app.Configuration.GetSection("MedRec").Get<MedRecStorageOptions>()?.UseLocalStorage == true)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<RuntimeSettingsService>();
        await settings.LoadCloudSettingsAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Unable to load shared runtime settings from PostgreSQL.");
    }
}

static async Task EnsureSqliteSchemaAsync(WebApplication app)
{
    try
    {
        if (app.Configuration.GetSection("MedRec").Get<MedRecStorageOptions>()?.UseLocalStorage != true)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<LocalAppPaths>();
        paths.EnsureCreated();

        var schemaPath = Path.Combine(app.Environment.ContentRootPath, "Database", "sqlite-schema.sql");
        if (!File.Exists(schemaPath))
        {
            app.Logger.LogWarning("SQLite schema file was not found at {SchemaPath}.", schemaPath);
            return;
        }

        var connections = scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqliteCommand(await File.ReadAllTextAsync(schemaPath), connection);
        await command.ExecuteNonQueryAsync();
        await EnsureSqliteMigrationsAsync(connection);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Unable to initialize the local SQLite schema.");
    }
}

static async Task EnsureSqliteMigrationsAsync(SqliteConnection connection)
{
    await AddSqliteColumnIfMissingAsync(connection, "users", "client_uid", "TEXT NULL");
    await AddSqliteColumnIfMissingAsync(connection, "users", "specialty", "TEXT NOT NULL DEFAULT ''");
    await AddSqliteColumnIfMissingAsync(connection, "users", "license_number", "TEXT NOT NULL DEFAULT ''");
    await AddSqliteColumnIfMissingAsync(connection, "users", "contact_number", "TEXT NOT NULL DEFAULT ''");
    await AddSqliteColumnIfMissingAsync(connection, "users", "signature_url", "TEXT NULL");
    await AddSqliteColumnIfMissingAsync(connection, "users", "sync_status", "TEXT NOT NULL DEFAULT 'Pending'");
    await AddSqliteColumnIfMissingAsync(connection, "users", "last_synced_at", "TEXT NULL");
    await AddSqliteColumnIfMissingAsync(connection, "users", "updated_at", "TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP");
    await ExecuteSqliteAsync(connection, "UPDATE users SET client_uid = lower(hex(randomblob(16))) WHERE client_uid IS NULL OR client_uid = '';");
    await ExecuteSqliteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS ux_users_client_uid ON users (client_uid);");

    foreach (var table in new[] { "patients", "clinical_records", "lab_results", "prescriptions" })
    {
        await AddSqliteColumnIfMissingAsync(connection, table, "client_uid", "TEXT NULL");
        await AddSqliteColumnIfMissingAsync(connection, table, "sync_status", "TEXT NOT NULL DEFAULT 'Pending'");
        await AddSqliteColumnIfMissingAsync(connection, table, "last_synced_at", "TEXT NULL");
        await AddSqliteColumnIfMissingAsync(connection, table, "updated_at", "TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP");
        await ExecuteSqliteAsync(connection, $"UPDATE {table} SET client_uid = lower(hex(randomblob(16))) WHERE client_uid IS NULL OR client_uid = '';");
        await ExecuteSqliteAsync(connection, $"CREATE UNIQUE INDEX IF NOT EXISTS ux_{table}_client_uid ON {table} (client_uid);");
    }

    await AddSqliteColumnIfMissingAsync(connection, "clinical_records", "height_cm", "REAL NULL");
    await AddSqliteColumnIfMissingAsync(connection, "clinical_records", "weight_kg", "REAL NULL");
    await AddSqliteColumnIfMissingAsync(connection, "clinical_records", "blood_pressure", "TEXT NOT NULL DEFAULT ''");
    await AddSqliteColumnIfMissingAsync(connection, "clinical_records", "fetal_heart_rate", "TEXT NOT NULL DEFAULT ''");
    await AddSqliteColumnIfMissingAsync(connection, "clinical_records", "temperature_c", "REAL NULL");

    await AddSqliteColumnIfMissingAsync(connection, "sync_queue", "entity_uid", "TEXT NULL");
    if (await SqliteColumnExistsAsync(connection, "sync_queue", "entity_id"))
    {
        await ExecuteSqliteAsync(connection, "UPDATE sync_queue SET entity_uid = CAST(entity_id AS TEXT) WHERE entity_uid IS NULL OR entity_uid = '';");
    }
    await ExecuteSqliteAsync(connection, "UPDATE sync_queue SET entity_uid = CAST(id AS TEXT) WHERE entity_uid IS NULL OR entity_uid = '';");

    await AddSqliteColumnIfMissingAsync(connection, "print_layouts", "document_type", "TEXT NOT NULL DEFAULT 'Prescription'");
    await AddSqliteColumnIfMissingAsync(connection, "print_layouts", "document_title", "TEXT NOT NULL DEFAULT 'Prescription'");
    await AddSqliteColumnIfMissingAsync(connection, "print_layouts", "layout_json", "TEXT NULL");
    await AddSqliteColumnIfMissingAsync(connection, "print_layouts", "sync_status", "TEXT NOT NULL DEFAULT 'Pending'");
    await AddSqliteColumnIfMissingAsync(connection, "print_layouts", "last_synced_at", "TEXT NULL");
    await AddSqliteColumnIfMissingAsync(connection, "print_layouts", "updated_at", "TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP");
}

static async Task AddSqliteColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition)
{
    if (await SqliteColumnExistsAsync(connection, table, column))
    {
        return;
    }

    await ExecuteSqliteAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
}

static async Task<bool> SqliteColumnExistsAsync(SqliteConnection connection, string table, string column)
{
    await using var command = new SqliteCommand($"PRAGMA table_info({table});", connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        if (string.Equals(Convert.ToString(reader["name"]), column, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static async Task ExecuteSqliteAsync(SqliteConnection connection, string sql)
{
    await using var command = new SqliteCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureDefaultPrintLayoutsAsync(WebApplication app)
{
    var options = app.Configuration.GetSection("MedRec").Get<MedRecStorageOptions>() ?? new MedRecStorageOptions();
    var prescriptionJson = PrintLayoutFormModel.SerializeBlocks(PrintLayout.DefaultBlocks("Prescription"));
    var diagnosisJson = PrintLayoutFormModel.SerializeBlocks(PrintLayout.DefaultBlocks("Diagnosis"));

    if (options.UseLocalStorage)
    {
        using var scope = app.Services.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync();
        await SeedSqlitePrintLayoutAsync(connection, 1, "Prescription", "Prescription", prescriptionJson);
        await SeedSqlitePrintLayoutAsync(connection, 2, "Diagnosis", "Medical Certificate", diagnosisJson);
        return;
    }

    using (var scope = app.Services.CreateScope())
    {
        var connections = scope.ServiceProvider.GetRequiredService<PostgresConnectionFactory>();
        if (!connections.IsConfigured)
        {
            return;
        }

        await using var connection = connections.CreateConnection();
        await connection.OpenAsync();
        await SeedPostgresPrintLayoutAsync(connection, 1, "Prescription", "Prescription", prescriptionJson);
        await SeedPostgresPrintLayoutAsync(connection, 2, "Diagnosis", "Medical Certificate", diagnosisJson);
    }
}

static async Task SeedSqlitePrintLayoutAsync(SqliteConnection connection, int id, string documentType, string title, string layoutJson)
{
    const string sql = """
        INSERT INTO print_layouts
          (id, document_type, document_title, clinic_name, doctor_name, signatory_name, signatory_title, logo_position, details_alignment, layout_json, sync_status, updated_at)
        VALUES
          (@id, @documentType, @title, 'MedRec Clinic', 'Dr. Cruz', 'Dr. Cruz', 'OB-Gyne', 'Left', 'Left', @layoutJson, 'Pending', CURRENT_TIMESTAMP)
        ON CONFLICT(document_type) DO UPDATE SET
          layout_json = CASE
            WHEN print_layouts.layout_json IS NULL OR TRIM(print_layouts.layout_json) = '' THEN excluded.layout_json
            ELSE print_layouts.layout_json
          END,
          document_title = CASE
            WHEN print_layouts.layout_json IS NULL OR TRIM(print_layouts.layout_json) = '' THEN excluded.document_title
            ELSE print_layouts.document_title
          END,
          sync_status = CASE
            WHEN print_layouts.layout_json IS NULL OR TRIM(print_layouts.layout_json) = '' THEN 'Pending'
            ELSE print_layouts.sync_status
          END,
          updated_at = CASE
            WHEN print_layouts.layout_json IS NULL OR TRIM(print_layouts.layout_json) = '' THEN CURRENT_TIMESTAMP
            ELSE print_layouts.updated_at
          END;
        """;

    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("@id", id);
    command.Parameters.AddWithValue("@documentType", documentType);
    command.Parameters.AddWithValue("@title", title);
    command.Parameters.AddWithValue("@layoutJson", layoutJson);
    await command.ExecuteNonQueryAsync();
}

static async Task SeedPostgresPrintLayoutAsync(NpgsqlConnection connection, int id, string documentType, string title, string layoutJson)
{
    const string sql = """
        INSERT INTO print_layouts
          (id, document_type, document_title, clinic_name, doctor_name, signatory_name, signatory_title, logo_position, details_alignment, layout_json)
        VALUES
          (@id, @documentType, @title, 'MedRec Clinic', 'Dr. Cruz', 'Dr. Cruz', 'OB-Gyne', 'Left', 'Left', @layoutJson::jsonb)
        ON CONFLICT(document_type) DO UPDATE SET
          layout_json = CASE
            WHEN print_layouts.layout_json IS NULL THEN excluded.layout_json
            ELSE print_layouts.layout_json
          END,
          document_title = CASE
            WHEN print_layouts.layout_json IS NULL THEN excluded.document_title
            ELSE print_layouts.document_title
          END,
          updated_at = CASE
            WHEN print_layouts.layout_json IS NULL THEN CURRENT_TIMESTAMP
            ELSE print_layouts.updated_at
          END;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("@id", id);
    command.Parameters.AddWithValue("@documentType", documentType);
    command.Parameters.AddWithValue("@title", title);
    command.Parameters.AddWithValue("@layoutJson", layoutJson);
    await command.ExecuteNonQueryAsync();
}

