# CNY Book Rescue Admin

Local Windows admin app for pickup provenance, inventory tracking, evidence storage, reporting, CSV import/export, audit history, and backup/restore.

This app is intentionally local-only. It does not publish a web admin, and private pickup/customer data should not be committed to GitHub.

## Build And Run

From the repository root:

```powershell
dotnet restore CNYBookRescue.Admin\CNYBookRescue.Admin.WinForms\CNYBookRescue.Admin.WinForms.csproj
dotnet build CNYBookRescue.Admin\CNYBookRescue.Admin.WinForms\CNYBookRescue.Admin.WinForms.csproj
dotnet run --project CNYBookRescue.Admin\CNYBookRescue.Admin.WinForms\CNYBookRescue.Admin.WinForms.csproj
```

## Local Data Locations

Runtime data is stored outside the repository:

- Database: `%LOCALAPPDATA%\CNYBookRescue\Database\cnybookrescue.db`
- Photos: `%LOCALAPPDATA%\CNYBookRescue\Photos`
- Documents: `%LOCALAPPDATA%\CNYBookRescue\Documents`
- Logs/temp: `%LOCALAPPDATA%\CNYBookRescue`
- Default exports: `Documents\CNY Book Rescue\Exports`
- Default backups: `Documents\CNY Book Rescue\Backups`

The root `.gitignore` excludes database files, photos, documents, logs, exports, and backups so operational records stay private.

## Core Workflows

### Pickups

Use the Pickups tab to create or update source records. A pickup can represent a residential pickup, estate pickup, estate sale company, thrift store, church/nonprofit, school/library, business, realtor/property manager, or other source.

Each pickup includes:

- Generated internal pickup ID
- Contact/source information
- Address/service area fields
- Ownership transfer confirmation
- Optional third-party authority relationship
- Status history
- Internal notes
- Photos and documents

### Inventory

Use the Inventory tab to track items from a pickup. Inventory records support ISBN, UPC, EAN, ASIN, title, author, media type, condition, quantity, disposition, Amazon status fields, buyback fields, and eBay listing/proceeds fields.

Items can be archived and restored without deleting the source record.

### Evidence

Photos and documents can be attached to either the full pickup or a specific inventory item. Evidence is copied into managed local storage so it stays with the operational record.

### Provenance Reports

Pickup and item provenance reports can be created from the Reports tab or from the Pickups/Inventory tabs.

Each report includes the required disclaimer:

`Internal provenance record. This document is not a supplier invoice, purchase invoice, or proof of authorized distribution.`

### CSV Import

Use the Import tab to import website/FormSubmit exports or compatible CSV files. The import supports source/contact details, pickup details, ownership confirmation, third-party authority, and clothing pickup flags.

Use "Save CSV Template" from the Import tab to generate the current expected header.

Duplicate protection uses `Original Submission Reference` when provided.

### Export

Use the Reports tab to export pickups, inventory, and audit history to CSV.

### Backup And Restore

Use the Backup/Restore tab to create ZIP backups containing:

- SQLite database
- Photos
- Documents

Restore validates that the ZIP contains a CNY Book Rescue database and creates a safety backup before replacing local data.

## Verification

Useful checks:

```powershell
dotnet build CNYBookRescue.Admin\CNYBookRescue.Admin.WinForms\CNYBookRescue.Admin.WinForms.csproj
```

The app initializes and migrates the SQLite database automatically on startup.
