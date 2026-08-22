namespace CNYBookRescue.Core;

public static class PickupStatuses
{
    public const string Requested = "REQUESTED";
    public const string Contacted = "CONTACTED";
    public const string Scheduled = "SCHEDULED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string Declined = "DECLINED";

    public static readonly string[] All =
    [
        Requested,
        Contacted,
        Scheduled,
        Completed,
        Cancelled,
        Declined
    ];
}

public static class OwnershipTransferStatuses
{
    public const string Confirmed = "CONFIRMED";
    public const string NotConfirmed = "NOT_CONFIRMED";
    public const string NotCollectedLegacy = "NOT_COLLECTED_LEGACY";

    public static readonly string[] All =
    [
        Confirmed,
        NotConfirmed,
        NotCollectedLegacy
    ];
}

public static class SourceTypes
{
    public const string ResidentialPickup = "Residential Pickup";

    public static readonly string[] All =
    [
        ResidentialPickup,
        "Estate Pickup",
        "Estate Sale Company",
        "Estate Liquidator",
        "Thrift Store",
        "Church / Nonprofit",
        "School / Library",
        "Business",
        "Garage / Estate Sale",
        "Realtor / Property Manager",
        "Other"
    ];
}

public static class InventoryDispositions
{
    public const string Undecided = "UNDECIDED";

    public static readonly string[] All =
    [
        Undecided,
        "AMAZON_FBA",
        "AMAZON_FBM",
        "BUYBACK",
        "EBAY",
        "OTHER_MARKETPLACE",
        "DONATE",
        "RECYCLE",
        "SOLD"
    ];
}

public static class PhotoTypes
{
    public static readonly string[] All =
    [
        "COLLECTION_BEFORE_PICKUP",
        "COLLECTION_AT_PICKUP",
        "BOXES",
        "BOOKS",
        "ITEM_COVER",
        "ITEM_BARCODE",
        "CONDITION_ISSUE",
        "MEDIA",
        "OTHER"
    ];
}

public static class DocumentTypes
{
    public static readonly string[] All =
    [
        "FORM_SUBMISSION",
        "OWNERSHIP_CONFIRMATION",
        "RECEIPT",
        "EMAIL",
        "LETTER",
        "ESTATE_DOCUMENT",
        "SOURCE_DOCUMENT",
        "OTHER"
    ];
}

public sealed class Pickup
{
    public long InternalId { get; set; }
    public string PickupId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? OriginalRequestDate { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string CellNumber { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string SourceContactName { get; set; } = "";
    public string SourcePhone { get; set; } = "";
    public string SourceEmail { get; set; } = "";
    public string StreetAddress { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "NY";
    public string ZipCode { get; set; } = "";
    public string EstimatedItemCount { get; set; } = "";
    public int? ActualItemCount { get; set; }
    public bool LargeCollection { get; set; }
    public bool ClothingPickup { get; set; }
    public string Comments { get; set; } = "";
    public string OwnershipTransferStatus { get; set; } = OwnershipTransferStatuses.NotCollectedLegacy;
    public DateTime? OwnershipTransferConfirmedAt { get; set; }
    public bool ThirdPartyAuthority { get; set; }
    public string AuthorityRelationship { get; set; } = "";
    public string PickupStatus { get; set; } = PickupStatuses.Requested;
    public DateTime? ScheduledPickupAt { get; set; }
    public DateTime? ActualPickupDate { get; set; }
    public DateTime? PickupCompletedAt { get; set; }
    public string SourceType { get; set; } = SourceTypes.ResidentialPickup;
    public string InternalNotes { get; set; } = "";
    public string OriginalSubmissionReference { get; set; } = "";
    public DateTime? ImportedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public string Name => string.Join(" ", new[] { FirstName, LastName }.Where(v => !string.IsNullOrWhiteSpace(v)));
    public string SourceName => !string.IsNullOrWhiteSpace(OrganizationName) ? OrganizationName : Name;
    public string ContactName => !string.IsNullOrWhiteSpace(SourceContactName) ? SourceContactName : Name;
    public string ContactPhone => !string.IsNullOrWhiteSpace(SourcePhone) ? SourcePhone : CellNumber;
    public string ContactEmail => !string.IsNullOrWhiteSpace(SourceEmail) ? SourceEmail : Email;
    public string AddressBlock => string.Join(", ", new[] { StreetAddress, AddressLine2, City, State, ZipCode }.Where(v => !string.IsNullOrWhiteSpace(v)));
}

public sealed class InventoryItem
{
    public long InventoryId { get; set; }
    public string PickupId { get; set; } = "";
    public string ISBN { get; set; } = "";
    public string UPC { get; set; } = "";
    public string EAN { get; set; } = "";
    public string ASIN { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string MediaType { get; set; } = "Book";
    public string Condition { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public DateTime? DateScanned { get; set; }
    public string AmazonCatalogStatus { get; set; } = "";
    public string AmazonEligibilityStatus { get; set; } = "";
    public string AmazonCondition { get; set; } = "";
    public DateTime? AmazonLastCheckedAt { get; set; }
    public string Disposition { get; set; } = InventoryDispositions.Undecided;
    public string Notes { get; set; } = "";
    public string BuybackVendor { get; set; } = "";
    public decimal? BuybackQuotedAmount { get; set; }
    public DateTime? BuybackQuoteDate { get; set; }
    public DateTime? BuybackSubmittedDate { get; set; }
    public decimal? BuybackPayoutAmount { get; set; }
    public DateTime? BuybackPayoutDate { get; set; }
    public decimal? EbayExpectedSalePrice { get; set; }
    public string EbayListingId { get; set; } = "";
    public DateTime? EbayListedDate { get; set; }
    public DateTime? EbaySoldDate { get; set; }
    public decimal? EbayGrossProceeds { get; set; }
    public decimal? EbayFees { get; set; }
    public decimal? EbayNetProceeds { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public sealed class PickupPhoto
{
    public long PhotoId { get; set; }
    public string PickupId { get; set; } = "";
    public long? InventoryId { get; set; }
    public string StoragePath { get; set; } = "";
    public string PhotoType { get; set; } = "OTHER";
    public string Caption { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public sealed class PickupDocument
{
    public long DocumentId { get; set; }
    public string PickupId { get; set; } = "";
    public long? InventoryId { get; set; }
    public string StoragePath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string DocumentType { get; set; } = "OTHER";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public sealed class PickupStatusHistory
{
    public long HistoryId { get; set; }
    public string PickupId { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public string NewStatus { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class AuditEvent
{
    public long AuditId { get; set; }
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string RecordType { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class DashboardSummary
{
    public Dictionary<string, int> PickupCounts { get; } = new();
    public Dictionary<string, int> InventoryCounts { get; } = new();
    public List<Pickup> RecentPickups { get; } = [];
}
