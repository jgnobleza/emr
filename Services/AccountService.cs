using medrec.Data;
using medrec.Models;
using MySqlConnector;

namespace medrec.Services;

public sealed class AccountService
{
    private readonly MySqlConnectionFactory _connections;
    private readonly PasswordService _passwords;
    private readonly IConfiguration _configuration;

    public AccountService(MySqlConnectionFactory connections, PasswordService passwords, IConfiguration configuration)
    {
        _connections = connections;
        _passwords = passwords;
        _configuration = configuration;
    }

    public async Task EnsureAccountsAsync()
    {
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();

        const string tableSql = """
            CREATE TABLE IF NOT EXISTS users (
              id INT AUTO_INCREMENT PRIMARY KEY,
              full_name VARCHAR(160) NOT NULL,
              email VARCHAR(190) NOT NULL UNIQUE,
              password_hash VARCHAR(255) NOT NULL,
              role ENUM('Admin', 'Doctor', 'Nurse', 'Staff') NOT NULL DEFAULT 'Doctor',
              is_active BOOLEAN NOT NULL DEFAULT TRUE,
              created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
              updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            );
            """;
        await using (var tableCommand = new MySqlCommand(tableSql, connection))
        {
            await tableCommand.ExecuteNonQueryAsync();
        }

        await EnsureProfileColumnsAsync(connection);

        await EnsureConfiguredUserAsync(connection, "AdminLogin", "Administrator", "admin", "admin123", "Admin");
        await EnsureConfiguredUserAsync(connection, "DoctorLogin", "Doctor", "doctor", "doctor123", "Doctor");
    }

    public async Task<AppUser?> AuthenticateAsync(string identifier, string password)
    {
        await EnsureAccountsAsync();
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();
        const string sql = """
            SELECT id, full_name, email, password_hash, role, specialty, license_number,
                   contact_number, signature_url, is_active, created_at
            FROM users
            WHERE LOWER(email) = LOWER(@identifier) AND is_active = TRUE
            LIMIT 1;
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@identifier", identifier.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || !_passwords.Verify(password, reader.GetString("password_hash")))
        {
            return null;
        }

        return ReadUser(reader);
    }

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync()
    {
        await EnsureAccountsAsync();
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();
        const string sql = "SELECT id, full_name, email, role, specialty, license_number, contact_number, signature_url, is_active, created_at FROM users ORDER BY role, full_name;";
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var users = new List<AppUser>();
        while (await reader.ReadAsync()) users.Add(ReadUser(reader));
        return users;
    }

    public async Task CreateDoctorAsync(string fullName, string email, string password)
    {
        await EnsureAccountsAsync();
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO users (full_name, email, password_hash, role, is_active)
            VALUES (@fullName, @email, @passwordHash, 'Doctor', TRUE);
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName.Trim());
        command.Parameters.AddWithValue("@email", email.Trim());
        command.Parameters.AddWithValue("@passwordHash", _passwords.Hash(password));
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("An account with that username or email already exists.", ex);
        }
    }

    public async Task<AppUser?> GetUserAsync(int id)
    {
        await EnsureAccountsAsync();
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();
        const string sql = "SELECT id, full_name, email, role, specialty, license_number, contact_number, signature_url, is_active, created_at FROM users WHERE id = @id LIMIT 1;";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadUser(reader) : null;
    }

    public async Task UpdateDoctorProfileAsync(int id, string fullName, string specialty, string licenseNumber, string contactNumber, string? signatureUrl)
    {
        await EnsureAccountsAsync();
        await using var connection = _connections.CreateConnection();
        await connection.OpenAsync();
        const string sql = """
            UPDATE users
            SET full_name = @fullName,
                specialty = @specialty,
                license_number = @licenseNumber,
                contact_number = @contactNumber,
                signature_url = @signatureUrl
            WHERE id = @id AND is_active = TRUE;
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@fullName", fullName.Trim());
        command.Parameters.AddWithValue("@specialty", specialty.Trim());
        command.Parameters.AddWithValue("@licenseNumber", licenseNumber.Trim());
        command.Parameters.AddWithValue("@contactNumber", contactNumber.Trim());
        command.Parameters.AddWithValue("@signatureUrl", string.IsNullOrWhiteSpace(signatureUrl) ? DBNull.Value : signatureUrl.Trim());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureProfileColumnsAsync(MySqlConnection connection)
    {
        var columns = new Dictionary<string, string>
        {
            ["specialty"] = "VARCHAR(160) NOT NULL DEFAULT ''",
            ["license_number"] = "VARCHAR(80) NOT NULL DEFAULT ''",
            ["contact_number"] = "VARCHAR(80) NOT NULL DEFAULT ''",
            ["signature_url"] = "VARCHAR(500) NULL"
        };

        foreach (var column in columns)
        {
            const string existsSql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users' AND column_name = @column;";
            await using var existsCommand = new MySqlCommand(existsSql, connection);
            existsCommand.Parameters.AddWithValue("@column", column.Key);
            if (Convert.ToInt32(await existsCommand.ExecuteScalarAsync()) > 0) continue;

            await using var alterCommand = new MySqlCommand($"ALTER TABLE users ADD COLUMN `{column.Key}` {column.Value};", connection);
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    private async Task EnsureConfiguredUserAsync(MySqlConnection connection, string section, string fallbackName, string fallbackUsername, string fallbackPassword, string role)
    {
        var name = _configuration[$"{section}:Name"] ?? fallbackName;
        var username = _configuration[$"{section}:Username"] ?? _configuration[$"{section}:Email"] ?? fallbackUsername;
        var password = _configuration[$"{section}:Password"] ?? fallbackPassword;
        const string sql = """
            INSERT INTO users (full_name, email, password_hash, role, is_active)
            VALUES (@name, @email, @passwordHash, @role, TRUE)
            ON DUPLICATE KEY UPDATE
              full_name = IF(password_hash = 'replace_with_real_hash', VALUES(full_name), full_name),
              role = IF(password_hash = 'replace_with_real_hash', VALUES(role), role),
              password_hash = IF(password_hash = 'replace_with_real_hash', VALUES(password_hash), password_hash);
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", name.Trim());
        command.Parameters.AddWithValue("@email", username.Trim());
        command.Parameters.AddWithValue("@passwordHash", _passwords.Hash(password));
        command.Parameters.AddWithValue("@role", role);
        await command.ExecuteNonQueryAsync();
    }

    private static AppUser ReadUser(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt32("id"),
        FullName = reader.GetString("full_name"),
        Email = reader.GetString("email"),
        Role = reader.GetString("role"),
        Specialty = reader.GetString("specialty"),
        LicenseNumber = reader.GetString("license_number"),
        ContactNumber = reader.GetString("contact_number"),
        SignatureUrl = reader.IsDBNull(reader.GetOrdinal("signature_url")) ? null : reader.GetString("signature_url"),
        IsActive = reader.GetBoolean("is_active"),
        CreatedAt = reader.GetDateTime("created_at")
    };
}
