using Microsoft.Data.Sqlite;
using System.Globalization;

namespace CNYBookRescue.Data;

public sealed class Database
{
    public const int CurrentSchemaVersion = 2;

    private readonly AppPaths _paths;
    private readonly string _connectionString;

    public Database(AppPaths paths)
    {
        _paths = paths;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            ForeignKeys = true
        }.ToString();
    }

    public string DatabasePath => _paths.DatabasePath;

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        _paths.EnsureDirectories();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL
            );
            """);

        var version = GetSchemaVersion(connection, transaction);
        if (version < 1)
        {
            ApplyVersion1(connection, transaction);
            SetSchemaVersion(connection, transaction, 1);
            version = 1;
        }

        if (version < 2)
        {
            ApplyVersion2(connection, transaction);
            SetSchemaVersion(connection, transaction, 2);
        }

        transaction.Commit();
    }

    public int GetSchemaVersion()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        return GetSchemaVersion(connection, transaction);
    }

    private static int GetSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void SetSchemaVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        Execute(connection, transaction, "DELETE FROM schema_version;");
        Execute(connection, transaction, $"INSERT INTO schema_version(version) VALUES ({version});");
    }

    private static void ApplyVersion1(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS pickups (
                internal_id INTEGER PRIMARY KEY AUTOINCREMENT,
                pickup_id TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                original_request_date TEXT NULL,
                first_name TEXT NOT NULL DEFAULT '',
                last_name TEXT NOT NULL DEFAULT '',
                email TEXT NOT NULL DEFAULT '',
                cell_number TEXT NOT NULL DEFAULT '',
                city TEXT NOT NULL DEFAULT '',
                zip_code TEXT NOT NULL DEFAULT '',
                estimated_item_count TEXT NOT NULL DEFAULT '',
                actual_item_count INTEGER NULL,
                large_collection INTEGER NOT NULL DEFAULT 0,
                clothing_pickup INTEGER NOT NULL DEFAULT 0,
                comments TEXT NOT NULL DEFAULT '',
                ownership_transfer_status TEXT NOT NULL,
                ownership_transfer_confirmed_at TEXT NULL,
                third_party_authority INTEGER NOT NULL DEFAULT 0,
                authority_relationship TEXT NOT NULL DEFAULT '',
                pickup_status TEXT NOT NULL,
                scheduled_pickup_at TEXT NULL,
                actual_pickup_date TEXT NULL,
                pickup_completed_at TEXT NULL,
                source_type TEXT NOT NULL,
                internal_notes TEXT NOT NULL DEFAULT '',
                original_submission_reference TEXT NOT NULL DEFAULT '',
                imported_at TEXT NULL,
                archived_at TEXT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_pickups_original_submission_reference
                ON pickups(original_submission_reference)
                WHERE original_submission_reference <> '';

            CREATE INDEX IF NOT EXISTS ix_pickups_pickup_status ON pickups(pickup_status);
            CREATE INDEX IF NOT EXISTS ix_pickups_pickup_id ON pickups(pickup_id);
            CREATE INDEX IF NOT EXISTS ix_pickups_city ON pickups(city);
            CREATE INDEX IF NOT EXISTS ix_pickups_zip_code ON pickups(zip_code);
            CREATE INDEX IF NOT EXISTS ix_pickups_source_type ON pickups(source_type);

            CREATE TABLE IF NOT EXISTS pickup_status_history (
                history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                pickup_id TEXT NOT NULL,
                previous_status TEXT NULL,
                new_status TEXT NOT NULL,
                changed_at TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (pickup_id) REFERENCES pickups(pickup_id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS pickup_photos (
                photo_id INTEGER PRIMARY KEY AUTOINCREMENT,
                pickup_id TEXT NOT NULL,
                storage_path TEXT NOT NULL,
                photo_type TEXT NOT NULL,
                caption TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                FOREIGN KEY (pickup_id) REFERENCES pickups(pickup_id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS inventory_items (
                inventory_id INTEGER PRIMARY KEY AUTOINCREMENT,
                pickup_id TEXT NOT NULL,
                isbn TEXT NOT NULL DEFAULT '',
                upc TEXT NOT NULL DEFAULT '',
                ean TEXT NOT NULL DEFAULT '',
                asin TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL DEFAULT '',
                author TEXT NOT NULL DEFAULT '',
                media_type TEXT NOT NULL DEFAULT 'Book',
                condition TEXT NOT NULL DEFAULT '',
                quantity INTEGER NOT NULL DEFAULT 1,
                date_scanned TEXT NULL,
                amazon_catalog_status TEXT NOT NULL DEFAULT '',
                amazon_eligibility_status TEXT NOT NULL DEFAULT '',
                amazon_condition TEXT NOT NULL DEFAULT '',
                amazon_last_checked_at TEXT NULL,
                disposition TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                buyback_vendor TEXT NOT NULL DEFAULT '',
                buyback_quoted_amount REAL NULL,
                buyback_quote_date TEXT NULL,
                buyback_submitted_date TEXT NULL,
                buyback_payout_amount REAL NULL,
                buyback_payout_date TEXT NULL,
                ebay_expected_sale_price REAL NULL,
                ebay_listing_id TEXT NOT NULL DEFAULT '',
                ebay_listed_date TEXT NULL,
                ebay_sold_date TEXT NULL,
                ebay_gross_proceeds REAL NULL,
                ebay_fees REAL NULL,
                ebay_net_proceeds REAL NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                archived_at TEXT NULL,
                FOREIGN KEY (pickup_id) REFERENCES pickups(pickup_id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_inventory_pickup_id ON inventory_items(pickup_id);
            CREATE INDEX IF NOT EXISTS ix_inventory_isbn ON inventory_items(isbn);
            CREATE INDEX IF NOT EXISTS ix_inventory_upc ON inventory_items(upc);
            CREATE INDEX IF NOT EXISTS ix_inventory_ean ON inventory_items(ean);
            CREATE INDEX IF NOT EXISTS ix_inventory_asin ON inventory_items(asin);
            CREATE INDEX IF NOT EXISTS ix_inventory_disposition ON inventory_items(disposition);

            CREATE TABLE IF NOT EXISTS audit_log (
                audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                record_type TEXT NOT NULL DEFAULT '',
                record_id TEXT NOT NULL DEFAULT '',
                details TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL
            );
            """);
    }

    private static void ApplyVersion2(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfMissing(connection, transaction, "pickups", "organization_name", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, transaction, "pickups", "source_contact_name", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, transaction, "pickups", "source_phone", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, transaction, "pickups", "source_email", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, transaction, "pickups", "street_address", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, transaction, "pickups", "address_line2", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, transaction, "pickups", "state", "TEXT NOT NULL DEFAULT 'NY'");

        AddColumnIfMissing(connection, transaction, "pickup_photos", "inventory_id", "INTEGER NULL");
        AddColumnIfMissing(connection, transaction, "pickup_photos", "archived_at", "TEXT NULL");

        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS pickup_documents (
                document_id INTEGER PRIMARY KEY AUTOINCREMENT,
                pickup_id TEXT NOT NULL,
                inventory_id INTEGER NULL,
                storage_path TEXT NOT NULL,
                original_file_name TEXT NOT NULL DEFAULT '',
                document_type TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                archived_at TEXT NULL,
                FOREIGN KEY (pickup_id) REFERENCES pickups(pickup_id) ON DELETE RESTRICT,
                FOREIGN KEY (inventory_id) REFERENCES inventory_items(inventory_id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_pickups_organization_name ON pickups(organization_name);
            CREATE INDEX IF NOT EXISTS ix_pickups_state ON pickups(state);
            CREATE INDEX IF NOT EXISTS ix_pickups_street_address ON pickups(street_address);
            CREATE INDEX IF NOT EXISTS ix_pickups_actual_pickup_date ON pickups(actual_pickup_date);
            CREATE INDEX IF NOT EXISTS ix_pickups_original_request_date ON pickups(original_request_date);
            CREATE INDEX IF NOT EXISTS ix_inventory_ebay_listing_id ON inventory_items(ebay_listing_id);
            CREATE INDEX IF NOT EXISTS ix_inventory_date_scanned ON inventory_items(date_scanned);
            CREATE INDEX IF NOT EXISTS ix_documents_pickup_id ON pickup_documents(pickup_id);
            CREATE INDEX IF NOT EXISTS ix_documents_inventory_id ON pickup_documents(inventory_id);
            CREATE INDEX IF NOT EXISTS ix_photos_inventory_id ON pickup_photos(inventory_id);
            """);
    }

    private static void AddColumnIfMissing(SqliteConnection connection, SqliteTransaction transaction, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        Execute(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    internal static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static string ToDb(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    internal static string? ToDb(DateTime? value) => value.HasValue ? ToDb(value.Value) : null;
    internal static DateTime? FromDb(string? value) => string.IsNullOrWhiteSpace(value) ? null : DateTime.Parse(value, null, DateTimeStyles.RoundtripKind).ToLocalTime();
}

public sealed class AppPaths
{
    public string RootDirectory { get; }
    public string DatabaseDirectory { get; }
    public string DatabasePath { get; }
    public string PhotosDirectory { get; }
    public string DocumentsDirectory { get; }
    public string TempDirectory { get; }
    public string LogsDirectory { get; }
    public string DefaultExportDirectory { get; }
    public string DefaultBackupDirectory { get; }

    private AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        DatabaseDirectory = Path.Combine(rootDirectory, "Database");
        DatabasePath = Path.Combine(DatabaseDirectory, "cnybookrescue.db");
        PhotosDirectory = Path.Combine(rootDirectory, "Photos");
        DocumentsDirectory = Path.Combine(rootDirectory, "Documents");
        TempDirectory = Path.Combine(rootDirectory, "Temp");
        LogsDirectory = Path.Combine(rootDirectory, "Logs");
        DefaultExportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CNY Book Rescue", "Exports");
        DefaultBackupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CNY Book Rescue", "Backups");
    }

    public static AppPaths CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = new AppPaths(Path.Combine(local, "CNYBookRescue"));
        paths.EnsureDirectories();
        return paths;
    }

    public static AppPaths CreateForRoot(string rootDirectory)
    {
        var paths = new AppPaths(rootDirectory);
        paths.EnsureDirectories();
        return paths;
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(PhotosDirectory);
        Directory.CreateDirectory(DocumentsDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DefaultExportDirectory);
        Directory.CreateDirectory(DefaultBackupDirectory);
    }

    public string GetPickupPhotoDirectory(string pickupId) => EnsureRecordDirectory(PhotosDirectory, pickupId);
    public string GetPickupDocumentDirectory(string pickupId) => EnsureRecordDirectory(DocumentsDirectory, pickupId);

    private static string EnsureRecordDirectory(string root, string pickupId)
    {
        var safePickupId = string.Join("_", pickupId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var directory = Path.Combine(root, safePickupId);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
