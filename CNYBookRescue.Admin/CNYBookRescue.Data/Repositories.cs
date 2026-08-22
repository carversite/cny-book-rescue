using CNYBookRescue.Core;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CNYBookRescue.Data;

public sealed class AuditService(Database database)
{
    public void Log(string eventType, string recordType = "", string recordId = "", string details = "")
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_log(event_type, timestamp, record_type, record_id, details)
            VALUES ($event_type, $timestamp, $record_type, $record_id, $details);
            """;
        command.Parameters.AddWithValue("$event_type", eventType);
        command.Parameters.AddWithValue("$timestamp", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$record_type", recordType);
        command.Parameters.AddWithValue("$record_id", recordId);
        command.Parameters.AddWithValue("$details", details);
        command.ExecuteNonQuery();
    }

    public List<AuditEvent> Search(DateTime? from = null, DateTime? to = null, string eventType = "", string recordType = "", string recordId = "", string text = "")
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string> { "1 = 1" };

        if (from.HasValue)
        {
            where.Add("timestamp >= $from");
            command.Parameters.AddWithValue("$from", Database.ToDb(from.Value.Date));
        }

        if (to.HasValue)
        {
            where.Add("timestamp <= $to");
            command.Parameters.AddWithValue("$to", Database.ToDb(to.Value.Date.AddDays(1).AddTicks(-1)));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            where.Add("event_type LIKE $event_type");
            command.Parameters.AddWithValue("$event_type", $"%{eventType.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(recordType))
        {
            where.Add("record_type LIKE $record_type");
            command.Parameters.AddWithValue("$record_type", $"%{recordType.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(recordId))
        {
            where.Add("record_id LIKE $record_id");
            command.Parameters.AddWithValue("$record_id", $"%{recordId.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            where.Add("(event_type LIKE $text OR record_type LIKE $text OR record_id LIKE $text OR details LIKE $text)");
            command.Parameters.AddWithValue("$text", $"%{text.Trim()}%");
        }

        command.CommandText = $"SELECT * FROM audit_log WHERE {string.Join(" AND ", where)} ORDER BY timestamp DESC LIMIT 5000;";
        using var reader = command.ExecuteReader();
        var rows = new List<AuditEvent>();
        while (reader.Read())
        {
            rows.Add(new AuditEvent
            {
                AuditId = reader.GetInt64(reader.GetOrdinal("audit_id")),
                EventType = Text(reader, "event_type"),
                Timestamp = Database.FromDb(Text(reader, "timestamp")) ?? DateTime.MinValue,
                RecordType = Text(reader, "record_type"),
                RecordId = Text(reader, "record_id"),
                Details = Text(reader, "details")
            });
        }

        return rows;
    }

    private static string Text(SqliteDataReader reader, string name) => reader[name] as string ?? "";
}

public sealed class PickupIdGenerator
{
    public string Generate(SqliteConnection connection, SqliteTransaction transaction, DateTime now)
    {
        var prefix = $"CBR-{now.Year}-";
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pickup_id
            FROM pickups
            WHERE pickup_id LIKE $prefix
            ORDER BY pickup_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$prefix", prefix + "%");

        var last = command.ExecuteScalar() as string;
        var next = 1;
        if (!string.IsNullOrWhiteSpace(last) && last.Length >= prefix.Length + 6)
        {
            next = int.Parse(last[^6..], CultureInfo.InvariantCulture) + 1;
        }

        return prefix + next.ToString("D6", CultureInfo.InvariantCulture);
    }
}

public sealed class PickupRepository(Database database, PickupIdGenerator idGenerator, AuditService audit)
{
    public List<Pickup> Search(
        string search = "",
        string status = "",
        string sourceType = "",
        string city = "",
        string zip = "",
        string state = "",
        DateTime? requestFrom = null,
        DateTime? requestTo = null,
        DateTime? pickupFrom = null,
        DateTime? pickupTo = null,
        bool includeArchived = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        if (!includeArchived)
        {
            where.Add("archived_at IS NULL");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(pickup_id LIKE $search OR first_name LIKE $search OR last_name LIKE $search OR organization_name LIKE $search OR source_contact_name LIKE $search OR email LIKE $search OR cell_number LIKE $search OR source_phone LIKE $search OR source_email LIKE $search OR street_address LIKE $search OR city LIKE $search OR state LIKE $search OR zip_code LIKE $search OR original_submission_reference LIKE $search)");
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            where.Add("pickup_status = $status");
            command.Parameters.AddWithValue("$status", status);
        }

        if (!string.IsNullOrWhiteSpace(sourceType) && sourceType != "All")
        {
            where.Add("source_type = $source_type");
            command.Parameters.AddWithValue("$source_type", sourceType);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            where.Add("city LIKE $city");
            command.Parameters.AddWithValue("$city", $"%{city.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(zip))
        {
            where.Add("zip_code LIKE $zip");
            command.Parameters.AddWithValue("$zip", $"%{zip.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            where.Add("state = $state");
            command.Parameters.AddWithValue("$state", state.Trim().ToUpperInvariant());
        }

        AddDateRange(where, command, "original_request_date", requestFrom, requestTo, "request");
        AddDateRange(where, command, "actual_pickup_date", pickupFrom, pickupTo, "pickup");

        command.CommandText = $"""
            SELECT *
            FROM pickups
            WHERE {(where.Count == 0 ? "1 = 1" : string.Join(" AND ", where))}
            ORDER BY created_at DESC
            LIMIT 2000;
            """;

        using var reader = command.ExecuteReader();
        var pickups = new List<Pickup>();
        while (reader.Read())
        {
            pickups.Add(ReadPickup(reader));
        }

        return pickups;
    }

    public Pickup? GetByPickupId(string pickupId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM pickups WHERE pickup_id = $pickup_id;";
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPickup(reader) : null;
    }

    public bool Exists(string pickupId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pickups WHERE pickup_id = $pickup_id AND archived_at IS NULL;";
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public Pickup Create(Pickup pickup)
    {
        ValidatePickup(pickup, isNew: true);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var now = DateTime.Now;
        pickup.PickupId = idGenerator.Generate(connection, transaction, now);
        pickup.CreatedAt = now;
        pickup.UpdatedAt = now;
        pickup.PickupStatus = string.IsNullOrWhiteSpace(pickup.PickupStatus) ? PickupStatuses.Requested : pickup.PickupStatus;
        pickup.SourceType = string.IsNullOrWhiteSpace(pickup.SourceType) ? SourceTypes.ResidentialPickup : pickup.SourceType;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertPickupSql;
        AddPickupParameters(command, pickup);
        command.ExecuteNonQuery();

        InsertStatusHistory(connection, transaction, pickup.PickupId, null, pickup.PickupStatus, "Pickup created");
        transaction.Commit();
        audit.Log("Pickup Created", "Pickup", pickup.PickupId);
        return pickup;
    }

    public void Update(Pickup pickup)
    {
        ValidatePickup(pickup, isNew: false);
        var existing = GetByPickupId(pickup.PickupId) ?? throw new InvalidOperationException("Pickup not found.");
        pickup.CreatedAt = existing.CreatedAt;
        pickup.UpdatedAt = DateTime.Now;
        pickup.ArchivedAt = existing.ArchivedAt;

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = UpdatePickupSql;
        AddPickupParameters(command, pickup);
        command.ExecuteNonQuery();

        if (!string.Equals(existing.PickupStatus, pickup.PickupStatus, StringComparison.Ordinal))
        {
            InsertStatusHistory(connection, transaction, pickup.PickupId, existing.PickupStatus, pickup.PickupStatus, "Status changed");
            audit.Log("Status Changed", "Pickup", pickup.PickupId, $"{existing.PickupStatus} -> {pickup.PickupStatus}");
        }
        else
        {
            audit.Log("Pickup Edited", "Pickup", pickup.PickupId);
        }

        transaction.Commit();
    }

    public void Archive(string pickupId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE pickups SET archived_at = $archived_at, updated_at = $updated_at WHERE pickup_id = $pickup_id;";
        command.Parameters.AddWithValue("$archived_at", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$updated_at", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        command.ExecuteNonQuery();
        audit.Log("Pickup Archived", "Pickup", pickupId);
    }

    public bool OriginalSubmissionExists(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pickups WHERE original_submission_reference = $reference;";
        command.Parameters.AddWithValue("$reference", reference.Trim());
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public List<PickupStatusHistory> GetStatusHistory(string pickupId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM pickup_status_history WHERE pickup_id = $pickup_id ORDER BY changed_at DESC;";
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        using var reader = command.ExecuteReader();
        var rows = new List<PickupStatusHistory>();
        while (reader.Read())
        {
            rows.Add(new PickupStatusHistory
            {
                HistoryId = reader.GetInt64(reader.GetOrdinal("history_id")),
                PickupId = Text(reader, "pickup_id"),
                PreviousStatus = NullableText(reader, "previous_status") ?? "",
                NewStatus = Text(reader, "new_status"),
                ChangedAt = Database.FromDb(Text(reader, "changed_at")) ?? DateTime.MinValue,
                Notes = Text(reader, "notes")
            });
        }

        return rows;
    }

    public DashboardSummary GetDashboardSummary()
    {
        using var connection = database.OpenConnection();
        var summary = new DashboardSummary();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT pickup_status, COUNT(*) FROM pickups WHERE archived_at IS NULL GROUP BY pickup_status;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                summary.PickupCounts[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT disposition, COUNT(*) FROM inventory_items WHERE archived_at IS NULL GROUP BY disposition;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                summary.InventoryCounts[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM pickups WHERE archived_at IS NULL ORDER BY created_at DESC LIMIT 20;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                summary.RecentPickups.Add(ReadPickup(reader));
            }
        }

        return summary;
    }

    private static void ValidatePickup(Pickup pickup, bool isNew)
    {
        if (!isNew && string.IsNullOrWhiteSpace(pickup.PickupId))
        {
            throw new InvalidOperationException("Pickup ID is required.");
        }

        if (!PickupStatuses.All.Contains(pickup.PickupStatus))
        {
            throw new InvalidOperationException("Pickup status is not valid.");
        }

        if (!OwnershipTransferStatuses.All.Contains(pickup.OwnershipTransferStatus))
        {
            throw new InvalidOperationException("Ownership transfer status is not valid.");
        }

        if (!string.IsNullOrWhiteSpace(pickup.ZipCode) && !System.Text.RegularExpressions.Regex.IsMatch(pickup.ZipCode.Trim(), @"^\d{5}(-\d{4})?$"))
        {
            throw new InvalidOperationException("ZIP code must be 12345 or 12345-6789.");
        }

        if (pickup.ScheduledPickupAt.HasValue && pickup.OriginalRequestDate.HasValue && pickup.ScheduledPickupAt.Value.Date < pickup.OriginalRequestDate.Value.Date)
        {
            throw new InvalidOperationException("Scheduled pickup cannot be before the request date.");
        }

        if (pickup.ActualPickupDate.HasValue && pickup.OriginalRequestDate.HasValue && pickup.ActualPickupDate.Value.Date < pickup.OriginalRequestDate.Value.Date)
        {
            throw new InvalidOperationException("Actual pickup cannot be before the request date.");
        }
    }

    private static void InsertStatusHistory(SqliteConnection connection, SqliteTransaction transaction, string pickupId, string? previousStatus, string newStatus, string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO pickup_status_history(pickup_id, previous_status, new_status, changed_at, notes)
            VALUES ($pickup_id, $previous_status, $new_status, $changed_at, $notes);
            """;
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        command.Parameters.AddWithValue("$previous_status", (object?)previousStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$new_status", newStatus);
        command.Parameters.AddWithValue("$changed_at", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$notes", notes);
        command.ExecuteNonQuery();
    }

    internal static Pickup ReadPickup(SqliteDataReader reader) => new()
    {
        InternalId = reader.GetInt64(reader.GetOrdinal("internal_id")),
        PickupId = Text(reader, "pickup_id"),
        CreatedAt = Database.FromDb(Text(reader, "created_at")) ?? DateTime.MinValue,
        UpdatedAt = Database.FromDb(Text(reader, "updated_at")) ?? DateTime.MinValue,
        OriginalRequestDate = Database.FromDb(NullableText(reader, "original_request_date")),
        FirstName = Text(reader, "first_name"),
        LastName = Text(reader, "last_name"),
        Email = Text(reader, "email"),
        CellNumber = Text(reader, "cell_number"),
        OrganizationName = Text(reader, "organization_name"),
        SourceContactName = Text(reader, "source_contact_name"),
        SourcePhone = Text(reader, "source_phone"),
        SourceEmail = Text(reader, "source_email"),
        StreetAddress = Text(reader, "street_address"),
        AddressLine2 = Text(reader, "address_line2"),
        City = Text(reader, "city"),
        State = Text(reader, "state"),
        ZipCode = Text(reader, "zip_code"),
        EstimatedItemCount = Text(reader, "estimated_item_count"),
        ActualItemCount = NullableInt(reader, "actual_item_count"),
        LargeCollection = Bool(reader, "large_collection"),
        ClothingPickup = Bool(reader, "clothing_pickup"),
        Comments = Text(reader, "comments"),
        OwnershipTransferStatus = Text(reader, "ownership_transfer_status"),
        OwnershipTransferConfirmedAt = Database.FromDb(NullableText(reader, "ownership_transfer_confirmed_at")),
        ThirdPartyAuthority = Bool(reader, "third_party_authority"),
        AuthorityRelationship = Text(reader, "authority_relationship"),
        PickupStatus = Text(reader, "pickup_status"),
        ScheduledPickupAt = Database.FromDb(NullableText(reader, "scheduled_pickup_at")),
        ActualPickupDate = Database.FromDb(NullableText(reader, "actual_pickup_date")),
        PickupCompletedAt = Database.FromDb(NullableText(reader, "pickup_completed_at")),
        SourceType = Text(reader, "source_type"),
        InternalNotes = Text(reader, "internal_notes"),
        OriginalSubmissionReference = Text(reader, "original_submission_reference"),
        ImportedAt = Database.FromDb(NullableText(reader, "imported_at")),
        ArchivedAt = Database.FromDb(NullableText(reader, "archived_at"))
    };

    private static void AddPickupParameters(SqliteCommand command, Pickup pickup)
    {
        command.Parameters.AddWithValue("$pickup_id", pickup.PickupId);
        command.Parameters.AddWithValue("$created_at", Database.ToDb(pickup.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", Database.ToDb(pickup.UpdatedAt));
        command.Parameters.AddWithValue("$original_request_date", (object?)Database.ToDb(pickup.OriginalRequestDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$first_name", pickup.FirstName.Trim());
        command.Parameters.AddWithValue("$last_name", pickup.LastName.Trim());
        command.Parameters.AddWithValue("$email", pickup.Email.Trim());
        command.Parameters.AddWithValue("$cell_number", pickup.CellNumber.Trim());
        command.Parameters.AddWithValue("$organization_name", pickup.OrganizationName.Trim());
        command.Parameters.AddWithValue("$source_contact_name", pickup.SourceContactName.Trim());
        command.Parameters.AddWithValue("$source_phone", pickup.SourcePhone.Trim());
        command.Parameters.AddWithValue("$source_email", pickup.SourceEmail.Trim());
        command.Parameters.AddWithValue("$street_address", pickup.StreetAddress.Trim());
        command.Parameters.AddWithValue("$address_line2", pickup.AddressLine2.Trim());
        command.Parameters.AddWithValue("$city", pickup.City.Trim());
        command.Parameters.AddWithValue("$state", string.IsNullOrWhiteSpace(pickup.State) ? "NY" : pickup.State.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$zip_code", pickup.ZipCode.Trim());
        command.Parameters.AddWithValue("$estimated_item_count", pickup.EstimatedItemCount.Trim());
        command.Parameters.AddWithValue("$actual_item_count", (object?)pickup.ActualItemCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$large_collection", pickup.LargeCollection ? 1 : 0);
        command.Parameters.AddWithValue("$clothing_pickup", pickup.ClothingPickup ? 1 : 0);
        command.Parameters.AddWithValue("$comments", pickup.Comments.Trim());
        command.Parameters.AddWithValue("$ownership_transfer_status", pickup.OwnershipTransferStatus);
        command.Parameters.AddWithValue("$ownership_transfer_confirmed_at", (object?)Database.ToDb(pickup.OwnershipTransferConfirmedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$third_party_authority", pickup.ThirdPartyAuthority ? 1 : 0);
        command.Parameters.AddWithValue("$authority_relationship", pickup.AuthorityRelationship.Trim());
        command.Parameters.AddWithValue("$pickup_status", pickup.PickupStatus);
        command.Parameters.AddWithValue("$scheduled_pickup_at", (object?)Database.ToDb(pickup.ScheduledPickupAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$actual_pickup_date", (object?)Database.ToDb(pickup.ActualPickupDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$pickup_completed_at", (object?)Database.ToDb(pickup.PickupCompletedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_type", pickup.SourceType);
        command.Parameters.AddWithValue("$internal_notes", pickup.InternalNotes.Trim());
        command.Parameters.AddWithValue("$original_submission_reference", pickup.OriginalSubmissionReference.Trim());
        command.Parameters.AddWithValue("$imported_at", (object?)Database.ToDb(pickup.ImportedAt) ?? DBNull.Value);
    }

    private const string InsertPickupSql = """
        INSERT INTO pickups (
            pickup_id, created_at, updated_at, original_request_date, first_name, last_name, email, cell_number,
            organization_name, source_contact_name, source_phone, source_email, street_address, address_line2, city,
            state, zip_code, estimated_item_count, actual_item_count, large_collection, clothing_pickup, comments,
            ownership_transfer_status, ownership_transfer_confirmed_at, third_party_authority, authority_relationship,
            pickup_status, scheduled_pickup_at, actual_pickup_date, pickup_completed_at, source_type, internal_notes,
            original_submission_reference, imported_at
        ) VALUES (
            $pickup_id, $created_at, $updated_at, $original_request_date, $first_name, $last_name, $email, $cell_number,
            $organization_name, $source_contact_name, $source_phone, $source_email, $street_address, $address_line2, $city,
            $state, $zip_code, $estimated_item_count, $actual_item_count, $large_collection, $clothing_pickup, $comments,
            $ownership_transfer_status, $ownership_transfer_confirmed_at, $third_party_authority, $authority_relationship,
            $pickup_status, $scheduled_pickup_at, $actual_pickup_date, $pickup_completed_at, $source_type, $internal_notes,
            $original_submission_reference, $imported_at
        );
        """;

    private const string UpdatePickupSql = """
        UPDATE pickups SET
            updated_at = $updated_at,
            original_request_date = $original_request_date,
            first_name = $first_name,
            last_name = $last_name,
            email = $email,
            cell_number = $cell_number,
            organization_name = $organization_name,
            source_contact_name = $source_contact_name,
            source_phone = $source_phone,
            source_email = $source_email,
            street_address = $street_address,
            address_line2 = $address_line2,
            city = $city,
            state = $state,
            zip_code = $zip_code,
            estimated_item_count = $estimated_item_count,
            actual_item_count = $actual_item_count,
            large_collection = $large_collection,
            clothing_pickup = $clothing_pickup,
            comments = $comments,
            ownership_transfer_status = $ownership_transfer_status,
            ownership_transfer_confirmed_at = $ownership_transfer_confirmed_at,
            third_party_authority = $third_party_authority,
            authority_relationship = $authority_relationship,
            pickup_status = $pickup_status,
            scheduled_pickup_at = $scheduled_pickup_at,
            actual_pickup_date = $actual_pickup_date,
            pickup_completed_at = $pickup_completed_at,
            source_type = $source_type,
            internal_notes = $internal_notes,
            original_submission_reference = $original_submission_reference,
            imported_at = $imported_at
        WHERE pickup_id = $pickup_id;
        """;

    internal static void AddDateRange(List<string> where, SqliteCommand command, string column, DateTime? from, DateTime? to, string prefix)
    {
        if (from.HasValue)
        {
            where.Add($"{column} >= ${prefix}_from");
            command.Parameters.AddWithValue($"${prefix}_from", Database.ToDb(from.Value.Date));
        }

        if (to.HasValue)
        {
            where.Add($"{column} <= ${prefix}_to");
            command.Parameters.AddWithValue($"${prefix}_to", Database.ToDb(to.Value.Date.AddDays(1).AddTicks(-1)));
        }
    }

    internal static string Text(SqliteDataReader reader, string name) => reader[name] as string ?? "";
    internal static string? NullableText(SqliteDataReader reader, string name) => reader[name] == DBNull.Value ? null : (string)reader[name];
    internal static int? NullableInt(SqliteDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToInt32(reader[name], CultureInfo.InvariantCulture);
    internal static long? NullableLong(SqliteDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToInt64(reader[name], CultureInfo.InvariantCulture);
    internal static decimal? NullableDecimal(SqliteDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToDecimal(reader[name], CultureInfo.InvariantCulture);
    internal static bool Bool(SqliteDataReader reader, string name) => Convert.ToInt32(reader[name], CultureInfo.InvariantCulture) == 1;
}

public sealed class InventoryRepository(Database database, PickupRepository pickups, AuditService audit)
{
    public List<InventoryItem> Search(string search = "", string disposition = "", string pickupId = "", DateTime? scannedFrom = null, DateTime? scannedTo = null, DateTime? pickupFrom = null, DateTime? pickupTo = null, bool includeArchived = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        if (!includeArchived)
        {
            where.Add("i.archived_at IS NULL");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(CAST(i.inventory_id AS TEXT) LIKE $search OR i.isbn LIKE $search OR i.upc LIKE $search OR i.ean LIKE $search OR i.asin LIKE $search OR i.title LIKE $search OR i.author LIKE $search OR i.pickup_id LIKE $search OR i.ebay_listing_id LIKE $search)");
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(disposition) && disposition != "All")
        {
            where.Add("i.disposition = $disposition");
            command.Parameters.AddWithValue("$disposition", disposition);
        }

        if (!string.IsNullOrWhiteSpace(pickupId))
        {
            where.Add("i.pickup_id = $pickup_id");
            command.Parameters.AddWithValue("$pickup_id", pickupId);
        }

        PickupRepository.AddDateRange(where, command, "i.date_scanned", scannedFrom, scannedTo, "scanned");
        PickupRepository.AddDateRange(where, command, "p.actual_pickup_date", pickupFrom, pickupTo, "pickup");

        command.CommandText = $"""
            SELECT i.*
            FROM inventory_items i
            JOIN pickups p ON p.pickup_id = i.pickup_id
            WHERE {(where.Count == 0 ? "1 = 1" : string.Join(" AND ", where))}
            ORDER BY i.created_at DESC
            LIMIT 5000;
            """;
        using var reader = command.ExecuteReader();
        var items = new List<InventoryItem>();
        while (reader.Read())
        {
            items.Add(ReadInventory(reader));
        }

        return items;
    }

    public InventoryItem? GetById(long inventoryId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM inventory_items WHERE inventory_id = $inventory_id;";
        command.Parameters.AddWithValue("$inventory_id", inventoryId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadInventory(reader) : null;
    }

    public InventoryItem Add(InventoryItem item)
    {
        ValidateInventory(item);
        var now = DateTime.Now;
        item.CreatedAt = now;
        item.UpdatedAt = now;
        item.DateScanned ??= now;
        item.Disposition = string.IsNullOrWhiteSpace(item.Disposition) ? InventoryDispositions.Undecided : item.Disposition;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = InventoryInsertSql + " SELECT last_insert_rowid();";
        AddInventoryParameters(command, item);
        item.InventoryId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        audit.Log("Inventory Added", "Inventory", item.InventoryId.ToString(CultureInfo.InvariantCulture), $"{item.PickupId}: {item.Title}");
        return item;
    }

    public void Update(InventoryItem item)
    {
        ValidateInventory(item);
        var existing = GetById(item.InventoryId) ?? throw new InvalidOperationException("Inventory item not found.");
        item.CreatedAt = existing.CreatedAt;
        item.UpdatedAt = DateTime.Now;
        item.ArchivedAt = existing.ArchivedAt;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = InventoryUpdateSql;
        AddInventoryParameters(command, item);
        command.Parameters.AddWithValue("$inventory_id", item.InventoryId);
        command.ExecuteNonQuery();
        audit.Log("Inventory Edited", "Inventory", item.InventoryId.ToString(CultureInfo.InvariantCulture), item.Title);
    }

    public void Archive(long inventoryId)
    {
        SetArchived(inventoryId, DateTime.Now, "Inventory Archived");
    }

    public void Restore(long inventoryId)
    {
        SetArchived(inventoryId, null, "Inventory Restored");
    }

    private void SetArchived(long inventoryId, DateTime? archivedAt, string eventType)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE inventory_items SET archived_at = $archived_at, updated_at = $updated_at WHERE inventory_id = $inventory_id;";
        command.Parameters.AddWithValue("$archived_at", (object?)Database.ToDb(archivedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$inventory_id", inventoryId);
        command.ExecuteNonQuery();
        audit.Log(eventType, "Inventory", inventoryId.ToString(CultureInfo.InvariantCulture));
    }

    private void ValidateInventory(InventoryItem item)
    {
        if (!pickups.Exists(item.PickupId))
        {
            throw new InvalidOperationException("Inventory must link to an existing, active Pickup ID.");
        }

        item.ISBN = NormalizeIdentifier(item.ISBN);
        item.UPC = NormalizeIdentifier(item.UPC);
        item.EAN = NormalizeIdentifier(item.EAN);
        item.ASIN = item.ASIN.Trim();

        if (item.Quantity <= 0)
        {
            throw new InvalidOperationException("Quantity must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(item.Title) && string.IsNullOrWhiteSpace(item.ISBN) && string.IsNullOrWhiteSpace(item.UPC) && string.IsNullOrWhiteSpace(item.EAN) && string.IsNullOrWhiteSpace(item.ASIN))
        {
            throw new InvalidOperationException("Enter at least a title or identifier.");
        }

        if (!string.IsNullOrWhiteSpace(item.ISBN) && item.ISBN.Length is not (10 or 13))
        {
            throw new InvalidOperationException("ISBN should contain 10 or 13 characters after spaces and hyphens are removed.");
        }

        if (!string.IsNullOrWhiteSpace(item.UPC) && (item.UPC.Length != 12 || !item.UPC.All(char.IsDigit)))
        {
            throw new InvalidOperationException("UPC should contain 12 digits.");
        }

        if (!string.IsNullOrWhiteSpace(item.EAN) && (item.EAN.Length != 13 || !item.EAN.All(char.IsDigit)))
        {
            throw new InvalidOperationException("EAN should contain 13 digits.");
        }
    }

    private static string NormalizeIdentifier(string value) => new(value.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray());

    internal static InventoryItem ReadInventory(SqliteDataReader reader) => new()
    {
        InventoryId = reader.GetInt64(reader.GetOrdinal("inventory_id")),
        PickupId = PickupRepository.Text(reader, "pickup_id"),
        ISBN = PickupRepository.Text(reader, "isbn"),
        UPC = PickupRepository.Text(reader, "upc"),
        EAN = PickupRepository.Text(reader, "ean"),
        ASIN = PickupRepository.Text(reader, "asin"),
        Title = PickupRepository.Text(reader, "title"),
        Author = PickupRepository.Text(reader, "author"),
        MediaType = PickupRepository.Text(reader, "media_type"),
        Condition = PickupRepository.Text(reader, "condition"),
        Quantity = Convert.ToInt32(reader["quantity"], CultureInfo.InvariantCulture),
        DateScanned = Database.FromDb(PickupRepository.NullableText(reader, "date_scanned")),
        AmazonCatalogStatus = PickupRepository.Text(reader, "amazon_catalog_status"),
        AmazonEligibilityStatus = PickupRepository.Text(reader, "amazon_eligibility_status"),
        AmazonCondition = PickupRepository.Text(reader, "amazon_condition"),
        AmazonLastCheckedAt = Database.FromDb(PickupRepository.NullableText(reader, "amazon_last_checked_at")),
        Disposition = PickupRepository.Text(reader, "disposition"),
        Notes = PickupRepository.Text(reader, "notes"),
        BuybackVendor = PickupRepository.Text(reader, "buyback_vendor"),
        BuybackQuotedAmount = PickupRepository.NullableDecimal(reader, "buyback_quoted_amount"),
        BuybackQuoteDate = Database.FromDb(PickupRepository.NullableText(reader, "buyback_quote_date")),
        BuybackSubmittedDate = Database.FromDb(PickupRepository.NullableText(reader, "buyback_submitted_date")),
        BuybackPayoutAmount = PickupRepository.NullableDecimal(reader, "buyback_payout_amount"),
        BuybackPayoutDate = Database.FromDb(PickupRepository.NullableText(reader, "buyback_payout_date")),
        EbayExpectedSalePrice = PickupRepository.NullableDecimal(reader, "ebay_expected_sale_price"),
        EbayListingId = PickupRepository.Text(reader, "ebay_listing_id"),
        EbayListedDate = Database.FromDb(PickupRepository.NullableText(reader, "ebay_listed_date")),
        EbaySoldDate = Database.FromDb(PickupRepository.NullableText(reader, "ebay_sold_date")),
        EbayGrossProceeds = PickupRepository.NullableDecimal(reader, "ebay_gross_proceeds"),
        EbayFees = PickupRepository.NullableDecimal(reader, "ebay_fees"),
        EbayNetProceeds = PickupRepository.NullableDecimal(reader, "ebay_net_proceeds"),
        CreatedAt = Database.FromDb(PickupRepository.Text(reader, "created_at")) ?? DateTime.MinValue,
        UpdatedAt = Database.FromDb(PickupRepository.Text(reader, "updated_at")) ?? DateTime.MinValue,
        ArchivedAt = Database.FromDb(PickupRepository.NullableText(reader, "archived_at"))
    };

    private static void AddInventoryParameters(SqliteCommand command, InventoryItem item)
    {
        command.Parameters.AddWithValue("$pickup_id", item.PickupId.Trim());
        command.Parameters.AddWithValue("$isbn", item.ISBN.Trim());
        command.Parameters.AddWithValue("$upc", item.UPC.Trim());
        command.Parameters.AddWithValue("$ean", item.EAN.Trim());
        command.Parameters.AddWithValue("$asin", item.ASIN.Trim());
        command.Parameters.AddWithValue("$title", item.Title.Trim());
        command.Parameters.AddWithValue("$author", item.Author.Trim());
        command.Parameters.AddWithValue("$media_type", string.IsNullOrWhiteSpace(item.MediaType) ? "Book" : item.MediaType.Trim());
        command.Parameters.AddWithValue("$condition", item.Condition.Trim());
        command.Parameters.AddWithValue("$quantity", item.Quantity);
        command.Parameters.AddWithValue("$date_scanned", (object?)Database.ToDb(item.DateScanned) ?? DBNull.Value);
        command.Parameters.AddWithValue("$amazon_catalog_status", item.AmazonCatalogStatus.Trim());
        command.Parameters.AddWithValue("$amazon_eligibility_status", item.AmazonEligibilityStatus.Trim());
        command.Parameters.AddWithValue("$amazon_condition", item.AmazonCondition.Trim());
        command.Parameters.AddWithValue("$amazon_last_checked_at", (object?)Database.ToDb(item.AmazonLastCheckedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$disposition", item.Disposition);
        command.Parameters.AddWithValue("$notes", item.Notes.Trim());
        command.Parameters.AddWithValue("$buyback_vendor", item.BuybackVendor.Trim());
        command.Parameters.AddWithValue("$buyback_quoted_amount", (object?)item.BuybackQuotedAmount ?? DBNull.Value);
        command.Parameters.AddWithValue("$buyback_quote_date", (object?)Database.ToDb(item.BuybackQuoteDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$buyback_submitted_date", (object?)Database.ToDb(item.BuybackSubmittedDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$buyback_payout_amount", (object?)item.BuybackPayoutAmount ?? DBNull.Value);
        command.Parameters.AddWithValue("$buyback_payout_date", (object?)Database.ToDb(item.BuybackPayoutDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$ebay_expected_sale_price", (object?)item.EbayExpectedSalePrice ?? DBNull.Value);
        command.Parameters.AddWithValue("$ebay_listing_id", item.EbayListingId.Trim());
        command.Parameters.AddWithValue("$ebay_listed_date", (object?)Database.ToDb(item.EbayListedDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$ebay_sold_date", (object?)Database.ToDb(item.EbaySoldDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$ebay_gross_proceeds", (object?)item.EbayGrossProceeds ?? DBNull.Value);
        command.Parameters.AddWithValue("$ebay_fees", (object?)item.EbayFees ?? DBNull.Value);
        command.Parameters.AddWithValue("$ebay_net_proceeds", (object?)item.EbayNetProceeds ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", Database.ToDb(item.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", Database.ToDb(item.UpdatedAt));
    }

    private const string InventoryInsertSql = """
        INSERT INTO inventory_items (
            pickup_id, isbn, upc, ean, asin, title, author, media_type, condition, quantity, date_scanned,
            amazon_catalog_status, amazon_eligibility_status, amazon_condition, amazon_last_checked_at,
            disposition, notes, buyback_vendor, buyback_quoted_amount, buyback_quote_date, buyback_submitted_date,
            buyback_payout_amount, buyback_payout_date, ebay_expected_sale_price, ebay_listing_id, ebay_listed_date,
            ebay_sold_date, ebay_gross_proceeds, ebay_fees, ebay_net_proceeds, created_at, updated_at
        ) VALUES (
            $pickup_id, $isbn, $upc, $ean, $asin, $title, $author, $media_type, $condition, $quantity, $date_scanned,
            $amazon_catalog_status, $amazon_eligibility_status, $amazon_condition, $amazon_last_checked_at,
            $disposition, $notes, $buyback_vendor, $buyback_quoted_amount, $buyback_quote_date, $buyback_submitted_date,
            $buyback_payout_amount, $buyback_payout_date, $ebay_expected_sale_price, $ebay_listing_id, $ebay_listed_date,
            $ebay_sold_date, $ebay_gross_proceeds, $ebay_fees, $ebay_net_proceeds, $created_at, $updated_at
        );
        """;

    private const string InventoryUpdateSql = """
        UPDATE inventory_items SET
            pickup_id = $pickup_id,
            isbn = $isbn,
            upc = $upc,
            ean = $ean,
            asin = $asin,
            title = $title,
            author = $author,
            media_type = $media_type,
            condition = $condition,
            quantity = $quantity,
            date_scanned = $date_scanned,
            amazon_catalog_status = $amazon_catalog_status,
            amazon_eligibility_status = $amazon_eligibility_status,
            amazon_condition = $amazon_condition,
            amazon_last_checked_at = $amazon_last_checked_at,
            disposition = $disposition,
            notes = $notes,
            buyback_vendor = $buyback_vendor,
            buyback_quoted_amount = $buyback_quoted_amount,
            buyback_quote_date = $buyback_quote_date,
            buyback_submitted_date = $buyback_submitted_date,
            buyback_payout_amount = $buyback_payout_amount,
            buyback_payout_date = $buyback_payout_date,
            ebay_expected_sale_price = $ebay_expected_sale_price,
            ebay_listing_id = $ebay_listing_id,
            ebay_listed_date = $ebay_listed_date,
            ebay_sold_date = $ebay_sold_date,
            ebay_gross_proceeds = $ebay_gross_proceeds,
            ebay_fees = $ebay_fees,
            ebay_net_proceeds = $ebay_net_proceeds,
            updated_at = $updated_at
        WHERE inventory_id = $inventory_id;
        """;
}

public sealed class PhotoRepository(Database database, AppPaths paths, AuditService audit)
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    public List<PickupPhoto> List(string pickupId = "", long? inventoryId = null, bool includeArchived = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        if (!includeArchived)
        {
            where.Add("archived_at IS NULL");
        }
        if (!string.IsNullOrWhiteSpace(pickupId))
        {
            where.Add("pickup_id = $pickup_id");
            command.Parameters.AddWithValue("$pickup_id", pickupId);
        }
        if (inventoryId.HasValue)
        {
            where.Add("inventory_id = $inventory_id");
            command.Parameters.AddWithValue("$inventory_id", inventoryId.Value);
        }

        command.CommandText = $"SELECT * FROM pickup_photos WHERE {(where.Count == 0 ? "1 = 1" : string.Join(" AND ", where))} ORDER BY created_at DESC;";
        using var reader = command.ExecuteReader();
        var rows = new List<PickupPhoto>();
        while (reader.Read())
        {
            rows.Add(ReadPhoto(reader));
        }
        return rows;
    }

    public void AddPhoto(string pickupId, string sourcePath, string photoType, string caption, long? inventoryId = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Photo file not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Unsupported image type.");
        }

        var destinationDirectory = paths.GetPickupPhotoDirectory(pickupId);
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}{extension}";
        var destinationPath = Path.Combine(destinationDirectory, fileName);
        File.Copy(sourcePath, destinationPath, overwrite: false);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pickup_photos(pickup_id, inventory_id, storage_path, photo_type, caption, created_at)
            VALUES ($pickup_id, $inventory_id, $storage_path, $photo_type, $caption, $created_at);
            """;
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        command.Parameters.AddWithValue("$inventory_id", (object?)inventoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$storage_path", destinationPath);
        command.Parameters.AddWithValue("$photo_type", photoType);
        command.Parameters.AddWithValue("$caption", caption.Trim());
        command.Parameters.AddWithValue("$created_at", Database.ToDb(DateTime.Now));
        command.ExecuteNonQuery();
        audit.Log("Photo Added", inventoryId.HasValue ? "Inventory" : "Pickup", inventoryId?.ToString(CultureInfo.InvariantCulture) ?? pickupId, Path.GetFileName(destinationPath));
    }

    public void Archive(long photoId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE pickup_photos SET archived_at = $archived_at WHERE photo_id = $photo_id;";
        command.Parameters.AddWithValue("$archived_at", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$photo_id", photoId);
        command.ExecuteNonQuery();
        audit.Log("Photo Archived", "Photo", photoId.ToString(CultureInfo.InvariantCulture));
    }

    private static PickupPhoto ReadPhoto(SqliteDataReader reader) => new()
    {
        PhotoId = reader.GetInt64(reader.GetOrdinal("photo_id")),
        PickupId = PickupRepository.Text(reader, "pickup_id"),
        InventoryId = PickupRepository.NullableLong(reader, "inventory_id"),
        StoragePath = PickupRepository.Text(reader, "storage_path"),
        PhotoType = PickupRepository.Text(reader, "photo_type"),
        Caption = PickupRepository.Text(reader, "caption"),
        CreatedAt = Database.FromDb(PickupRepository.Text(reader, "created_at")) ?? DateTime.MinValue,
        ArchivedAt = Database.FromDb(PickupRepository.NullableText(reader, "archived_at"))
    };
}

public sealed class DocumentRepository(Database database, AppPaths paths, AuditService audit)
{
    public List<PickupDocument> List(string pickupId = "", long? inventoryId = null, bool includeArchived = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        if (!includeArchived)
        {
            where.Add("archived_at IS NULL");
        }
        if (!string.IsNullOrWhiteSpace(pickupId))
        {
            where.Add("pickup_id = $pickup_id");
            command.Parameters.AddWithValue("$pickup_id", pickupId);
        }
        if (inventoryId.HasValue)
        {
            where.Add("inventory_id = $inventory_id");
            command.Parameters.AddWithValue("$inventory_id", inventoryId.Value);
        }

        command.CommandText = $"SELECT * FROM pickup_documents WHERE {(where.Count == 0 ? "1 = 1" : string.Join(" AND ", where))} ORDER BY created_at DESC;";
        using var reader = command.ExecuteReader();
        var rows = new List<PickupDocument>();
        while (reader.Read())
        {
            rows.Add(ReadDocument(reader));
        }
        return rows;
    }

    public void AddDocument(string pickupId, string sourcePath, string documentType, string description, long? inventoryId = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Document file not found.", sourcePath);
        }

        var destinationDirectory = paths.GetPickupDocumentDirectory(pickupId);
        var originalName = Path.GetFileName(sourcePath);
        var safeName = string.Join("_", originalName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var destinationPath = Path.Combine(destinationDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safeName}");
        File.Copy(sourcePath, destinationPath, overwrite: false);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pickup_documents(pickup_id, inventory_id, storage_path, original_file_name, document_type, description, created_at)
            VALUES ($pickup_id, $inventory_id, $storage_path, $original_file_name, $document_type, $description, $created_at);
            """;
        command.Parameters.AddWithValue("$pickup_id", pickupId);
        command.Parameters.AddWithValue("$inventory_id", (object?)inventoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$storage_path", destinationPath);
        command.Parameters.AddWithValue("$original_file_name", originalName);
        command.Parameters.AddWithValue("$document_type", documentType);
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$created_at", Database.ToDb(DateTime.Now));
        command.ExecuteNonQuery();
        audit.Log("Document Added", inventoryId.HasValue ? "Inventory" : "Pickup", inventoryId?.ToString(CultureInfo.InvariantCulture) ?? pickupId, originalName);
    }

    public void Archive(long documentId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE pickup_documents SET archived_at = $archived_at WHERE document_id = $document_id;";
        command.Parameters.AddWithValue("$archived_at", Database.ToDb(DateTime.Now));
        command.Parameters.AddWithValue("$document_id", documentId);
        command.ExecuteNonQuery();
        audit.Log("Document Archived", "Document", documentId.ToString(CultureInfo.InvariantCulture));
    }

    private static PickupDocument ReadDocument(SqliteDataReader reader) => new()
    {
        DocumentId = reader.GetInt64(reader.GetOrdinal("document_id")),
        PickupId = PickupRepository.Text(reader, "pickup_id"),
        InventoryId = PickupRepository.NullableLong(reader, "inventory_id"),
        StoragePath = PickupRepository.Text(reader, "storage_path"),
        OriginalFileName = PickupRepository.Text(reader, "original_file_name"),
        DocumentType = PickupRepository.Text(reader, "document_type"),
        Description = PickupRepository.Text(reader, "description"),
        CreatedAt = Database.FromDb(PickupRepository.Text(reader, "created_at")) ?? DateTime.MinValue,
        ArchivedAt = Database.FromDb(PickupRepository.NullableText(reader, "archived_at"))
    };
}

public sealed class CsvImportService(PickupRepository pickups, AuditService audit)
{
    public string TemplateText => string.Join(",", new[]
    {
        "External Submission ID",
        "Request Date",
        "First Name",
        "Last Name",
        "Email",
        "Cell Number",
        "Organization Name",
        "Street Address",
        "Address Line 2",
        "City",
        "State",
        "ZIP",
        "Estimated Item Count",
        "Large Collection",
        "Clothing Pickup",
        "Comments",
        "Ownership Confirmation",
        "Ownership Confirmation Timestamp",
        "Third-Party Authority",
        "Authority Relationship"
    }) + Environment.NewLine;

    public ImportResult Import(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            return new ImportResult(0, 0, ["CSV has no data rows."]);
        }

        var headers = SplitCsv(lines[0]).Select(NormalizeHeader).ToList();
        var imported = 0;
        var skipped = 0;
        var messages = new List<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var values = SplitCsv(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count && c < values.Count; c++)
            {
                row[headers[c]] = values[c].Trim();
            }

            var reference = Get(row, "externalsubmissionid", "submissionid", "originalsubmissionreference", "id");
            if (!string.IsNullOrWhiteSpace(reference) && pickups.OriginalSubmissionExists(reference))
            {
                skipped++;
                messages.Add($"Row {i + 1}: skipped duplicate external reference {reference}.");
                continue;
            }

            var ownershipConfirmed = ParseBool(Get(row, "ownershipconfirmation", "ownershiptransferconfirmed", "ownershipandtransferconfirmationchecked"));
            var pickup = new Pickup
            {
                OriginalRequestDate = ParseDate(Get(row, "requestdate", "submittedat", "date")),
                FirstName = Get(row, "firstname", "first"),
                LastName = Get(row, "lastname", "last"),
                Email = Get(row, "email", "emailaddress"),
                CellNumber = Get(row, "phone", "cellnumber", "cell"),
                OrganizationName = Get(row, "organizationname", "organization"),
                StreetAddress = Get(row, "streetaddress", "address"),
                AddressLine2 = Get(row, "addressline2"),
                City = Get(row, "city", "citytown", "town"),
                State = Get(row, "state"),
                ZipCode = Get(row, "zip", "zipcode", "postalcode"),
                EstimatedItemCount = Get(row, "estimateditems", "estimateditemcount", "estimatednumberofbooks", "estimatednumberofitems"),
                LargeCollection = ParseBool(Get(row, "largecollection")),
                ClothingPickup = ParseBool(Get(row, "clothingpickup", "unwantedclothingpickup")),
                Comments = Get(row, "comments", "message"),
                OwnershipTransferStatus = ownershipConfirmed ? OwnershipTransferStatuses.Confirmed : OwnershipTransferStatuses.NotCollectedLegacy,
                OwnershipTransferConfirmedAt = ParseDate(Get(row, "ownershipconfirmationtimestamp", "ownershiptransferconfirmedat")) ?? (ownershipConfirmed ? DateTime.Now : null),
                ThirdPartyAuthority = ParseBool(Get(row, "thirdpartyauthority", "thirdpartyauthoritycheckboxchecked")),
                AuthorityRelationship = Get(row, "authorityrelationship", "relationship"),
                OriginalSubmissionReference = reference,
                ImportedAt = DateTime.Now
            };

            try
            {
                pickups.Create(pickup);
                imported++;
            }
            catch (Exception ex)
            {
                skipped++;
                messages.Add($"Row {i + 1}: {ex.Message}");
            }
        }

        audit.Log("CSV Imported", "File", Path.GetFileName(path), $"{imported} imported, {skipped} skipped");
        return new ImportResult(imported, skipped, messages);
    }

    private static string NormalizeHeader(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Get(Dictionary<string, string> row, params string[] keys) => keys.Select(key => row.TryGetValue(key, out var value) ? value : "").FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    private static bool ParseBool(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("checked", StringComparison.OrdinalIgnoreCase);
    private static DateTime? ParseDate(string value) => DateTime.TryParse(value, out var date) ? date : null;

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}

public sealed record ImportResult(int Imported, int Skipped, List<string> Messages);

public sealed class ExportService(PickupRepository pickupRepository, InventoryRepository inventoryRepository, AuditService audit)
{
    public void ExportPickups(string path)
    {
        var rows = pickupRepository.Search();
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("Pickup ID,Request Date,Pickup Date,Source Type,Organization Name,City,State,ZIP,Status,Estimated Items,Actual Items,Ownership Transfer Status,Third-Party Authority");
        foreach (var pickup in rows)
        {
            writer.WriteLine(string.Join(",", Csv(pickup.PickupId), Csv(pickup.OriginalRequestDate), Csv(pickup.ActualPickupDate), Csv(pickup.SourceType), Csv(pickup.OrganizationName), Csv(pickup.City), Csv(pickup.State), Csv(pickup.ZipCode), Csv(pickup.PickupStatus), Csv(pickup.EstimatedItemCount), Csv(pickup.ActualItemCount), Csv(pickup.OwnershipTransferStatus), Csv(pickup.ThirdPartyAuthority)));
        }
        audit.Log("CSV Exported", "Pickups", "", path);
    }

    public void ExportInventory(string path)
    {
        var rows = inventoryRepository.Search();
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("Inventory ID,Pickup ID,ISBN,UPC,EAN,ASIN,Title,Author,Condition,Quantity,Date Scanned,Disposition,Amazon Catalog Status,eBay Listing ID");
        foreach (var item in rows)
        {
            writer.WriteLine(string.Join(",", Csv(item.InventoryId), Csv(item.PickupId), Csv(item.ISBN), Csv(item.UPC), Csv(item.EAN), Csv(item.ASIN), Csv(item.Title), Csv(item.Author), Csv(item.Condition), Csv(item.Quantity), Csv(item.DateScanned), Csv(item.Disposition), Csv(item.AmazonCatalogStatus), Csv(item.EbayListingId)));
        }
        audit.Log("CSV Exported", "Inventory", "", path);
    }

    public void ExportAudit(string path, IEnumerable<AuditEvent> rows)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("Audit ID,Timestamp,Event Type,Record Type,Record ID,Details");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", Csv(row.AuditId), Csv(row.Timestamp), Csv(row.EventType), Csv(row.RecordType), Csv(row.RecordId), Csv(row.Details)));
        }
        audit.Log("CSV Exported", "Audit", "", path);
    }

    internal static string Csv(object? value)
    {
        var text = value switch
        {
            DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed class ReportService(PickupRepository pickups, InventoryRepository inventory, PhotoRepository photos, DocumentRepository documents, AuditService audit)
{
    public const string Disclaimer = "Internal provenance record. This document is not a supplier invoice, purchase invoice, or proof of authorized distribution.";

    public string BuildPickupProvenanceReport(string pickupId)
    {
        var pickup = pickups.GetByPickupId(pickupId) ?? throw new InvalidOperationException("Pickup not found.");
        var items = inventory.Search(pickupId: pickupId);
        var pickupPhotos = photos.List(pickupId);
        var pickupDocuments = documents.List(pickupId);
        var history = pickups.GetStatusHistory(pickupId);

        var sb = Header($"Pickup Provenance Report - {pickup.PickupId}");
        AppendPickup(sb, pickup);
        sb.AppendLine("Ownership / Authority");
        sb.AppendLine($"Ownership Transfer Status: {pickup.OwnershipTransferStatus}");
        sb.AppendLine($"Ownership Confirmation Timestamp: {Fmt(pickup.OwnershipTransferConfirmedAt)}");
        sb.AppendLine($"Third-Party Authority: {YesNo(pickup.ThirdPartyAuthority)}");
        sb.AppendLine($"Authority Relationship: {pickup.AuthorityRelationship}");
        sb.AppendLine();
        AppendEvidence(sb, pickupPhotos, pickupDocuments);
        sb.AppendLine("Associated Inventory");
        foreach (var item in items)
        {
            sb.AppendLine($"- #{item.InventoryId}: {item.Title} | ISBN {item.ISBN} | UPC {item.UPC} | EAN {item.EAN} | ASIN {item.ASIN} | Qty {item.Quantity} | {item.Disposition}");
        }
        sb.AppendLine();
        sb.AppendLine("Pickup Status History");
        foreach (var row in history)
        {
            sb.AppendLine($"- {Fmt(row.ChangedAt)}: {row.PreviousStatus} -> {row.NewStatus} ({row.Notes})");
        }
        Footer(sb);
        audit.Log("Pickup Provenance Report Generated", "Pickup", pickupId);
        return sb.ToString();
    }

    public string BuildItemProvenanceReport(long inventoryId)
    {
        var item = inventory.GetById(inventoryId) ?? throw new InvalidOperationException("Inventory item not found.");
        var pickup = pickups.GetByPickupId(item.PickupId) ?? throw new InvalidOperationException("Source pickup not found.");
        var pickupPhotos = photos.List(item.PickupId);
        var itemPhotos = photos.List(item.PickupId, item.InventoryId);
        var pickupDocuments = documents.List(item.PickupId);
        var itemDocuments = documents.List(item.PickupId, item.InventoryId);

        var sb = Header($"Item Provenance Report - Inventory #{item.InventoryId}");
        sb.AppendLine("Inventory Item");
        sb.AppendLine($"Inventory ID: {item.InventoryId}");
        sb.AppendLine($"ISBN: {item.ISBN}");
        sb.AppendLine($"UPC: {item.UPC}");
        sb.AppendLine($"EAN: {item.EAN}");
        sb.AppendLine($"ASIN: {item.ASIN}");
        sb.AppendLine($"Title: {item.Title}");
        sb.AppendLine($"Author: {item.Author}");
        sb.AppendLine($"Media Type: {item.MediaType}");
        sb.AppendLine($"Condition: {item.Condition}");
        sb.AppendLine($"Quantity: {item.Quantity}");
        sb.AppendLine($"Date Scanned: {Fmt(item.DateScanned)}");
        sb.AppendLine($"Disposition: {item.Disposition}");
        sb.AppendLine($"eBay Listing ID: {item.EbayListingId}");
        sb.AppendLine();
        AppendPickup(sb, pickup);
        sb.AppendLine("Ownership / Authority");
        sb.AppendLine($"Ownership Transfer Status: {pickup.OwnershipTransferStatus}");
        sb.AppendLine($"Ownership Confirmation Timestamp: {Fmt(pickup.OwnershipTransferConfirmedAt)}");
        sb.AppendLine($"Third-Party Authority: {YesNo(pickup.ThirdPartyAuthority)}");
        sb.AppendLine($"Authority Relationship: {pickup.AuthorityRelationship}");
        sb.AppendLine();
        sb.AppendLine("Provenance Chain");
        sb.AppendLine($"Inventory Item #{item.InventoryId}");
        sb.AppendLine("  -> Pickup ID " + pickup.PickupId);
        sb.AppendLine("  -> Source " + pickup.SourceName);
        sb.AppendLine("  -> Acquisition/Pickup Date " + Fmt(pickup.ActualPickupDate ?? pickup.OriginalRequestDate));
        sb.AppendLine("  -> Ownership/Authority " + pickup.OwnershipTransferStatus);
        sb.AppendLine("  -> Supporting Photos/Documents listed below");
        sb.AppendLine();
        AppendEvidence(sb, pickupPhotos.Concat(itemPhotos).DistinctBy(p => p.PhotoId), pickupDocuments.Concat(itemDocuments).DistinctBy(d => d.DocumentId));
        Footer(sb);
        audit.Log("Item Provenance Report Generated", "Inventory", inventoryId.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public string SaveReport(string reportText, string directory, string prefix)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, reportText, Encoding.UTF8);
        audit.Log("Text Report Saved", "Report", "", path);
        return path;
    }

    private static StringBuilder Header(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CNY Book Rescue");
        sb.AppendLine(title);
        sb.AppendLine(new string('=', Math.Max(30, title.Length)));
        sb.AppendLine();
        return sb;
    }

    private static void AppendPickup(StringBuilder sb, Pickup pickup)
    {
        sb.AppendLine("Source Pickup");
        sb.AppendLine($"Pickup ID: {pickup.PickupId}");
        sb.AppendLine($"Request Date: {Fmt(pickup.OriginalRequestDate)}");
        sb.AppendLine($"Actual Pickup Date: {Fmt(pickup.ActualPickupDate)}");
        sb.AppendLine($"Source Type: {pickup.SourceType}");
        sb.AppendLine($"Organization Name: {pickup.OrganizationName}");
        sb.AppendLine($"Source Name: {pickup.SourceName}");
        sb.AppendLine($"Source Contact: {pickup.ContactName}");
        sb.AppendLine($"Email: {pickup.ContactEmail}");
        sb.AppendLine($"Phone: {pickup.ContactPhone}");
        sb.AppendLine($"Address: {pickup.StreetAddress} {pickup.AddressLine2}".Trim());
        sb.AppendLine($"City/State/ZIP: {pickup.City}, {pickup.State} {pickup.ZipCode}".Trim());
        sb.AppendLine($"Estimated Item Count: {pickup.EstimatedItemCount}");
        sb.AppendLine($"Actual Item Count: {pickup.ActualItemCount}");
        sb.AppendLine("Internal Notes:");
        sb.AppendLine(pickup.InternalNotes);
        sb.AppendLine();
    }

    private static void AppendEvidence(StringBuilder sb, IEnumerable<PickupPhoto> photoRows, IEnumerable<PickupDocument> documentRows)
    {
        sb.AppendLine("Supporting Evidence");
        sb.AppendLine("Photos:");
        foreach (var photo in photoRows)
        {
            sb.AppendLine($"- [{photo.PhotoType}] {photo.StoragePath} {photo.Caption}");
        }
        sb.AppendLine("Documents:");
        foreach (var doc in documentRows)
        {
            sb.AppendLine($"- [{doc.DocumentType}] {doc.OriginalFileName} | {doc.StoragePath} | {doc.Description}");
        }
        sb.AppendLine();
    }

    private static void Footer(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine(Disclaimer);
    }

    private static string Fmt(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";
    private static string YesNo(bool value) => value ? "Yes" : "No";
}

public sealed class BackupService(AppPaths paths, Database database, AuditService audit)
{
    public string CreateBackup()
    {
        paths.EnsureDirectories();
        var destination = Path.Combine(paths.DefaultBackupDirectory, $"CNYBookRescue_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip");
        CreateBackupTo(destination);
        audit.Log("Backup Created", "Backup", "", destination);
        return destination;
    }

    public string CreateSafetyBackup()
    {
        paths.EnsureDirectories();
        var destination = Path.Combine(paths.DefaultBackupDirectory, $"CNYBookRescue_SafetyBackup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip");
        CreateBackupTo(destination);
        audit.Log("Safety Backup Created", "Backup", "", destination);
        return destination;
    }

    public void RestoreBackup(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Backup zip not found.", zipPath);
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            if (!archive.Entries.Any(e => string.Equals(e.FullName, "Database/cnybookrescue.db", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Backup is missing Database/cnybookrescue.db.");
            }
        }

        var safety = CreateSafetyBackup();
        var extractRoot = Path.Combine(paths.TempDirectory, $"Restore_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(extractRoot);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractRoot);
            var restoredDb = Path.Combine(extractRoot, "Database", "cnybookrescue.db");
            if (!File.Exists(restoredDb))
            {
                throw new InvalidOperationException("Extracted backup did not contain the database.");
            }

            SqliteConnection.ClearAllPools();
            File.Copy(restoredDb, paths.DatabasePath, overwrite: true);
            ReplaceDirectory(Path.Combine(extractRoot, "Photos"), paths.PhotosDirectory);
            ReplaceDirectory(Path.Combine(extractRoot, "Documents"), paths.DocumentsDirectory);
            database.Initialize();
            audit.Log("Backup Restored", "Backup", "", $"{zipPath}; safety backup: {safety}");
        }
        catch
        {
            audit.Log("Backup Restore Failed", "Backup", "", $"Restore failed. Safety backup: {safety}");
            throw;
        }
        finally
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }
        }
    }

    private void CreateBackupTo(string destination)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        var tempDb = Path.Combine(paths.TempDirectory, $"backup_{Guid.NewGuid():N}.db");
        try
        {
            using (var source = database.OpenConnection())
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempDb }.ToString()))
            {
                backup.Open();
                source.BackupDatabase(backup);
            }

            SqliteConnection.ClearAllPools();

            using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(tempDb, "Database/cnybookrescue.db");
            AddDirectory(archive, paths.PhotosDirectory);
            AddDirectory(archive, paths.DocumentsDirectory);
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                File.Delete(tempDb);
            }
        }
    }

    private void AddDirectory(ZipArchive archive, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(paths.RootDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, relative);
        }
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            return;
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}

public static class FileLauncher
{
    public static void Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File not found.", path);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
