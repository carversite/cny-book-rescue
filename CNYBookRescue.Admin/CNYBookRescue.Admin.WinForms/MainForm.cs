using CNYBookRescue.Core;
using CNYBookRescue.Data;
using System.Globalization;

namespace CNYBookRescue.Admin.WinForms;

public sealed class MainForm : Form
{
    private readonly AppPaths _paths;
    private readonly Database _database;
    private readonly AuditService _audit;
    private readonly PickupRepository _pickups;
    private readonly InventoryRepository _inventory;
    private readonly PhotoRepository _photos;
    private readonly DocumentRepository _documents;
    private readonly CsvImportService _importer;
    private readonly ExportService _exports;
    private readonly BackupService _backup;
    private readonly ReportService _reports;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _dashboardRecent = Grid();
    private readonly Label _dashboardCounts = new() { Dock = DockStyle.Top, AutoSize = false, Height = 150, Padding = new Padding(12), Font = new Font("Segoe UI", 10F) };
    private readonly DataGridView _pickupsGrid = Grid();
    private readonly DataGridView _inventoryGrid = Grid();
    private readonly DataGridView _auditGrid = Grid();
    private readonly TextBox _pickupSearch = new() { Width = 190 };
    private readonly ComboBox _pickupStatus = Combo("All", PickupStatuses.All);
    private readonly ComboBox _pickupSource = Combo("All", SourceTypes.All);
    private readonly TextBox _pickupRequestFrom = new() { Width = 90, PlaceholderText = "Req from" };
    private readonly TextBox _pickupRequestTo = new() { Width = 90, PlaceholderText = "Req to" };
    private readonly TextBox _inventorySearch = new() { Width = 190 };
    private readonly ComboBox _inventoryDisposition = Combo("All", InventoryDispositions.All);
    private readonly TextBox _inventoryScannedFrom = new() { Width = 90, PlaceholderText = "Scan from" };
    private readonly TextBox _inventoryScannedTo = new() { Width = 90, PlaceholderText = "Scan to" };
    private readonly CheckBox _includeArchivedInventory = new() { Text = "Archived" };
    private readonly TextBox _auditSearch = new() { Width = 220 };

    public MainForm()
    {
        Text = "CNY Book Rescue Admin";
        Width = 1320;
        Height = 820;
        MinimumSize = new Size(1040, 650);
        StartPosition = FormStartPosition.CenterScreen;

        _paths = AppPaths.CreateDefault();
        _database = new Database(_paths);
        _database.Initialize();
        _audit = new AuditService(_database);
        _pickups = new PickupRepository(_database, new PickupIdGenerator(), _audit);
        _inventory = new InventoryRepository(_database, _pickups, _audit);
        _photos = new PhotoRepository(_database, _paths, _audit);
        _documents = new DocumentRepository(_database, _paths, _audit);
        _importer = new CsvImportService(_pickups, _audit);
        _exports = new ExportService(_pickups, _inventory, _audit);
        _backup = new BackupService(_paths, _database, _audit);
        _reports = new ReportService(_pickups, _inventory, _photos, _documents, _audit);
        _audit.Log("Application Started");

        Controls.Add(_tabs);
        BuildDashboardTab();
        BuildPickupsTab();
        BuildInventoryTab();
        BuildImportTab();
        BuildReportsTab();
        BuildBackupTab();
        BuildAuditTab();
        BuildSettingsTab();

        LoadDashboard();
        LoadPickups();
        LoadInventory();
        LoadAudit();
    }

    private void BuildDashboardTab()
    {
        var tab = Page("Dashboard");
        var refresh = Button("Refresh", (_, _) => LoadDashboard());
        _dashboardRecent.DoubleClick += (_, _) => OpenRecentPickup();
        tab.Controls.Add(_dashboardRecent);
        tab.Controls.Add(refresh);
        tab.Controls.Add(_dashboardCounts);
        _tabs.TabPages.Add(tab);
    }

    private void BuildPickupsTab()
    {
        var tab = Page("Pickups");
        var toolbar = Bar(70);
        toolbar.Controls.Add(Label("Search"));
        toolbar.Controls.Add(_pickupSearch);
        toolbar.Controls.Add(Label("Status"));
        toolbar.Controls.Add(_pickupStatus);
        toolbar.Controls.Add(Label("Source"));
        toolbar.Controls.Add(_pickupSource);
        toolbar.Controls.Add(_pickupRequestFrom);
        toolbar.Controls.Add(_pickupRequestTo);
        toolbar.Controls.Add(Button("Apply", (_, _) => LoadPickups()));
        toolbar.Controls.Add(Button("New", (_, _) => NewPickup()));
        toolbar.Controls.Add(Button("Edit", (_, _) => EditSelectedPickup()));
        toolbar.Controls.Add(Button("Add Inventory", (_, _) => AddInventoryFromSelectedPickup()));
        toolbar.Controls.Add(Button("Add Photo", (_, _) => AddPhotoToSelectedPickup()));
        toolbar.Controls.Add(Button("Add Document", (_, _) => AddDocumentToSelectedPickup()));
        toolbar.Controls.Add(Button("Evidence", (_, _) => ShowPickupEvidence()));
        toolbar.Controls.Add(Button("History", (_, _) => ShowStatusHistory()));
        toolbar.Controls.Add(Button("Report", (_, _) => ShowPickupReport()));

        _pickupSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) LoadPickups(); };
        _pickupsGrid.DoubleClick += (_, _) => EditSelectedPickup();
        tab.Controls.Add(_pickupsGrid);
        tab.Controls.Add(toolbar);
        _tabs.TabPages.Add(tab);
    }

    private void BuildInventoryTab()
    {
        var tab = Page("Inventory");
        var toolbar = Bar(76);
        toolbar.Controls.Add(Label("Search"));
        toolbar.Controls.Add(_inventorySearch);
        toolbar.Controls.Add(Label("Disposition"));
        toolbar.Controls.Add(_inventoryDisposition);
        toolbar.Controls.Add(_inventoryScannedFrom);
        toolbar.Controls.Add(_inventoryScannedTo);
        toolbar.Controls.Add(_includeArchivedInventory);
        toolbar.Controls.Add(Button("Apply", (_, _) => LoadInventory()));
        toolbar.Controls.Add(Button("Add", (_, _) => AddInventory()));
        toolbar.Controls.Add(Button("Edit", (_, _) => EditSelectedInventory()));
        toolbar.Controls.Add(Button("Archive", (_, _) => ArchiveSelectedInventory()));
        toolbar.Controls.Add(Button("Restore", (_, _) => RestoreSelectedInventory()));
        toolbar.Controls.Add(Button("Source Pickup", (_, _) => ViewSourcePickup()));
        toolbar.Controls.Add(Button("Photo", (_, _) => AddPhotoToSelectedInventory()));
        toolbar.Controls.Add(Button("Document", (_, _) => AddDocumentToSelectedInventory()));
        toolbar.Controls.Add(Button("Evidence", (_, _) => ShowInventoryEvidence()));
        toolbar.Controls.Add(Button("View Provenance", (_, _) => ShowItemProvenance()));
        toolbar.Controls.Add(Button("Report", (_, _) => SaveItemReport()));
        _inventorySearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) LoadInventory(); };
        _inventoryGrid.DoubleClick += (_, _) => EditSelectedInventory();
        tab.Controls.Add(_inventoryGrid);
        tab.Controls.Add(toolbar);
        _tabs.TabPages.Add(tab);
    }

    private void BuildImportTab()
    {
        var tab = Page("Import");
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Text = "Import website requests from a CSV export. The desktop app generates the official CBR Pickup ID during import.\r\n\r\nSupported columns include External Submission ID, Request Date, First Name, Last Name, Email, Cell Number, Organization Name, Street Address, Address Line 2, City, State, ZIP, Estimated Item Count, Large Collection, Clothing Pickup, Comments, Ownership Confirmation, Ownership Confirmation Timestamp, Third-Party Authority, and Authority Relationship.\r\n\r\nDuplicate External Submission ID values are skipped."
        };
        var toolbar = Bar();
        toolbar.Controls.Add(Button("Select CSV and Import", (_, _) => ImportCsv()));
        toolbar.Controls.Add(Button("Save CSV Template", (_, _) => SaveImportTemplate()));
        tab.Controls.Add(box);
        tab.Controls.Add(toolbar);
        _tabs.TabPages.Add(tab);
    }

    private void BuildReportsTab()
    {
        var tab = Page("Reports");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 130, Padding = new Padding(12), AutoScroll = true };
        panel.Controls.Add(Button("Export Pickups CSV", (_, _) => ExportPickups()));
        panel.Controls.Add(Button("Export Inventory CSV", (_, _) => ExportInventory()));
        panel.Controls.Add(Button("Generate Selected Pickup Report", (_, _) => ShowPickupReport()));
        panel.Controls.Add(Button("Generate Selected Item Report", (_, _) => SaveItemReport()));
        tab.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Text = $"CSV reports are stored locally. Provenance reports are internal records only.\r\n\r\n{ReportService.Disclaimer}"
        });
        tab.Controls.Add(panel);
        _tabs.TabPages.Add(tab);
    }

    private void BuildBackupTab()
    {
        var tab = Page("Backup / Restore");
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Text = $"Backups include the SQLite database, managed photos, and managed documents.\r\n\r\nDefault backup folder:\r\n{_paths.DefaultBackupDirectory}\r\n\r\nRestore validates the zip, creates an automatic safety backup, restores database/photos/documents, reopens the database, validates schema, refreshes the UI, and logs the restore."
        };
        var toolbar = Bar();
        toolbar.Controls.Add(Button("Create Backup", async (_, _) => await RunAsync("Creating backup", () => _backup.CreateBackup(), path => MessageBox.Show(this, $"Backup created:\r\n{path}", "Backup"))));
        toolbar.Controls.Add(Button("Restore Backup", (_, _) => RestoreBackup()));
        tab.Controls.Add(text);
        tab.Controls.Add(toolbar);
        _tabs.TabPages.Add(tab);
    }

    private void BuildAuditTab()
    {
        var tab = Page("Audit Log");
        var toolbar = Bar();
        toolbar.Controls.Add(Label("Search"));
        toolbar.Controls.Add(_auditSearch);
        toolbar.Controls.Add(Button("Apply", (_, _) => LoadAudit()));
        toolbar.Controls.Add(Button("Export Audit CSV", (_, _) => ExportAudit()));
        _auditSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) LoadAudit(); };
        tab.Controls.Add(_auditGrid);
        tab.Controls.Add(toolbar);
        _tabs.TabPages.Add(tab);
    }

    private void BuildSettingsTab()
    {
        var tab = Page("Settings");
        tab.Controls.Add(new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Text = $"Application Version: 1.0\r\nSchema Version: {_database.GetSchemaVersion()}\r\n\r\nDatabase:\r\n{_paths.DatabasePath}\r\n\r\nPhotos:\r\n{_paths.PhotosDirectory}\r\n\r\nDocuments:\r\n{_paths.DocumentsDirectory}\r\n\r\nExports:\r\n{_paths.DefaultExportDirectory}\r\n\r\nBackups:\r\n{_paths.DefaultBackupDirectory}\r\n\r\nDefault Source Type: {SourceTypes.ResidentialPickup}\r\nDefault Inventory Disposition: {InventoryDispositions.Undecided}\r\n\r\nPrivate data remains local to this Windows profile."
        });
        _tabs.TabPages.Add(tab);
    }

    private void LoadDashboard()
    {
        var summary = _pickups.GetDashboardSummary();
        _dashboardCounts.Text =
            "Pickups\r\n" +
            string.Join("    ", PickupStatuses.All.Select(s => $"{s}: {summary.PickupCounts.GetValueOrDefault(s)}")) +
            "\r\n\r\nInventory\r\n" +
            $"TOTAL: {summary.InventoryCounts.Values.Sum()}    " +
            string.Join("    ", InventoryDispositions.All.Select(s => $"{s}: {summary.InventoryCounts.GetValueOrDefault(s)}"));

        _dashboardRecent.DataSource = summary.RecentPickups.Select(PickupRow.From).ToList();
    }

    private void LoadPickups()
    {
        _pickupsGrid.DataSource = _pickups.Search(
            _pickupSearch.Text,
            _pickupStatus.Text,
            _pickupSource.Text,
            requestFrom: ParseDate(_pickupRequestFrom.Text),
            requestTo: ParseDate(_pickupRequestTo.Text)).Select(PickupRow.From).ToList();
        LoadDashboard();
    }

    private void LoadInventory()
    {
        _inventoryGrid.DataSource = _inventory.Search(
            _inventorySearch.Text,
            _inventoryDisposition.Text,
            scannedFrom: ParseDate(_inventoryScannedFrom.Text),
            scannedTo: ParseDate(_inventoryScannedTo.Text),
            includeArchived: _includeArchivedInventory.Checked).Select(InventoryRow.From).ToList();
    }

    private void LoadAudit()
    {
        _auditGrid.DataSource = _audit.Search(text: _auditSearch.Text).Select(AuditRow.From).ToList();
    }

    private void NewPickup()
    {
        using var dialog = new PickupDialog(new Pickup { OriginalRequestDate = DateTime.Now, State = "NY" });
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pickups.Create(dialog.Pickup);
            LoadPickups();
        }
    }

    private void EditSelectedPickup()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is null) return;
        var pickup = _pickups.GetByPickupId(pickupId);
        if (pickup is null) return;

        using var dialog = new PickupDialog(pickup);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pickups.Update(dialog.Pickup);
            LoadPickups();
            LoadInventory();
        }
    }

    private void AddInventoryFromSelectedPickup()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is not null) AddInventory(pickupId);
    }

    private void AddInventory(string pickupId = "")
    {
        if (string.IsNullOrWhiteSpace(pickupId))
        {
            pickupId = Microsoft.VisualBasic.Interaction.InputBox("Enter source Pickup ID:", "Add Inventory");
        }

        if (string.IsNullOrWhiteSpace(pickupId)) return;
        using var dialog = new InventoryDialog(new InventoryItem { PickupId = pickupId });
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inventory.Add(dialog.Item);
            LoadInventory();
            LoadDashboard();
        }
    }

    private void EditSelectedInventory()
    {
        var inventoryId = SelectedInventoryId();
        if (inventoryId is null) return;
        var item = _inventory.GetById(inventoryId.Value);
        if (item is null) return;

        using var dialog = new InventoryDialog(item);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inventory.Update(dialog.Item);
            LoadInventory();
        }
    }

    private void ArchiveSelectedInventory()
    {
        var inventoryId = SelectedInventoryId();
        if (inventoryId is null) return;
        if (MessageBox.Show(this, "Archive selected inventory item? This preserves provenance and hides it from normal lists.", "Archive", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _inventory.Archive(inventoryId.Value);
        LoadInventory();
        LoadDashboard();
    }

    private void RestoreSelectedInventory()
    {
        var inventoryId = SelectedInventoryId();
        if (inventoryId is null) return;
        _inventory.Restore(inventoryId.Value);
        LoadInventory();
        LoadDashboard();
    }

    private void AddPhotoToSelectedPickup()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is not null) AddPhoto(pickupId, null);
    }

    private void AddPhotoToSelectedInventory()
    {
        var item = SelectedInventory();
        if (item is not null) AddPhoto(item.PickupId, item.InventoryId);
    }

    private void AddPhoto(string pickupId, long? inventoryId)
    {
        using var file = new OpenFileDialog { Filter = "Image files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp", Multiselect = false };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        using var category = new PhotoDialog();
        if (category.ShowDialog(this) == DialogResult.OK)
        {
            _photos.AddPhoto(pickupId, file.FileName, category.PhotoType, category.Caption, inventoryId);
            MessageBox.Show(this, "Photo copied into managed local storage.", "Photo Added");
        }
    }

    private void AddDocumentToSelectedPickup()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is not null) AddDocument(pickupId, null);
    }

    private void AddDocumentToSelectedInventory()
    {
        var item = SelectedInventory();
        if (item is not null) AddDocument(item.PickupId, item.InventoryId);
    }

    private void AddDocument(string pickupId, long? inventoryId)
    {
        using var file = new OpenFileDialog { Filter = "Documents|*.pdf;*.txt;*.doc;*.docx;*.jpg;*.jpeg;*.png;*.eml;*.msg|All files|*.*", Multiselect = false };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        using var details = new DocumentDialog();
        if (details.ShowDialog(this) == DialogResult.OK)
        {
            _documents.AddDocument(pickupId, file.FileName, details.DocumentType, details.Description, inventoryId);
            MessageBox.Show(this, "Document copied into managed local storage.", "Document Added");
        }
    }

    private void ShowPickupEvidence()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is null) return;
        using var dialog = new EvidenceDialog(_photos.List(pickupId), _documents.List(pickupId), _photos, _documents);
        dialog.ShowDialog(this);
    }

    private void ShowInventoryEvidence()
    {
        var item = SelectedInventory();
        if (item is null) return;
        using var dialog = new EvidenceDialog(_photos.List(item.PickupId, item.InventoryId), _documents.List(item.PickupId, item.InventoryId), _photos, _documents);
        dialog.ShowDialog(this);
    }

    private void ShowStatusHistory()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is null) return;
        var text = string.Join(Environment.NewLine, _pickups.GetStatusHistory(pickupId).Select(h => $"{h.ChangedAt:g}: {h.PreviousStatus} -> {h.NewStatus}  {h.Notes}"));
        using var viewer = new TextViewer("Pickup Status History", string.IsNullOrWhiteSpace(text) ? "No status history." : text);
        viewer.ShowDialog(this);
    }

    private void ShowPickupReport()
    {
        var pickupId = SelectedPickupId();
        if (pickupId is null) return;
        using var viewer = new TextViewer("Pickup Provenance Report", _reports.BuildPickupProvenanceReport(pickupId));
        viewer.ShowDialog(this);
    }

    private void ShowItemProvenance()
    {
        var inventoryId = SelectedInventoryId();
        if (inventoryId is null) return;
        using var viewer = new TextViewer("Item Provenance", _reports.BuildItemProvenanceReport(inventoryId.Value));
        viewer.ShowDialog(this);
    }

    private void SaveItemReport()
    {
        var inventoryId = SelectedInventoryId();
        if (inventoryId is null) return;
        var report = _reports.BuildItemProvenanceReport(inventoryId.Value);
        var path = _reports.SaveReport(report, _paths.DefaultExportDirectory, $"item_provenance_{inventoryId.Value}");
        MessageBox.Show(this, $"Report saved:\r\n{path}", "Report");
    }

    private void ViewSourcePickup()
    {
        var item = SelectedInventory();
        if (item is null) return;
        _pickupSearch.Text = item.PickupId;
        LoadPickups();
        _tabs.SelectedIndex = 1;
    }

    private void OpenRecentPickup()
    {
        if (_dashboardRecent.CurrentRow?.DataBoundItem is not PickupRow row) return;
        _pickupSearch.Text = row.PickupId;
        LoadPickups();
        _tabs.SelectedIndex = 1;
    }

    private async void ImportCsv()
    {
        using var file = new OpenFileDialog { Filter = "CSV files|*.csv|All files|*.*" };
        if (file.ShowDialog(this) != DialogResult.OK) return;

        await RunAsync("Importing CSV", () => _importer.Import(file.FileName), result =>
        {
            MessageBox.Show(this, $"Imported: {result.Imported}\r\nSkipped: {result.Skipped}\r\n\r\n{string.Join("\r\n", result.Messages.Take(12))}", "Import Complete");
            LoadPickups();
            LoadAudit();
        });
    }

    private void SaveImportTemplate()
    {
        using var file = SaveCsv("website_import_template");
        if (file.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(file.FileName, _importer.TemplateText);
            MessageBox.Show(this, "CSV template saved.", "Template");
        }
    }

    private void ExportPickups()
    {
        using var file = SaveCsv("pickups");
        if (file.ShowDialog(this) == DialogResult.OK)
        {
            _exports.ExportPickups(file.FileName);
            MessageBox.Show(this, "Pickup export complete.", "Export");
        }
    }

    private void ExportInventory()
    {
        using var file = SaveCsv("inventory");
        if (file.ShowDialog(this) == DialogResult.OK)
        {
            _exports.ExportInventory(file.FileName);
            MessageBox.Show(this, "Inventory export complete.", "Export");
        }
    }

    private void ExportAudit()
    {
        using var file = SaveCsv("audit");
        if (file.ShowDialog(this) == DialogResult.OK)
        {
            _exports.ExportAudit(file.FileName, _audit.Search(text: _auditSearch.Text));
            MessageBox.Show(this, "Audit export complete.", "Export");
        }
    }

    private void RestoreBackup()
    {
        using var file = new OpenFileDialog { Filter = "Backup zip|*.zip|All files|*.*" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "Restore this backup? A safety backup of current data will be created first.", "Restore Backup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _backup.RestoreBackup(file.FileName);
        LoadDashboard();
        LoadPickups();
        LoadInventory();
        LoadAudit();
        MessageBox.Show(this, "Backup restored successfully.", "Restore");
    }

    private async Task RunAsync<T>(string title, Func<T> work, Action<T> complete)
    {
        UseWaitCursor = true;
        try
        {
            var result = await Task.Run(work);
            complete(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private string? SelectedPickupId() => _pickupsGrid.CurrentRow?.DataBoundItem is PickupRow row ? row.PickupId : null;
    private long? SelectedInventoryId() => _inventoryGrid.CurrentRow?.DataBoundItem is InventoryRow row ? row.InventoryId : null;
    private InventoryItem? SelectedInventory() => SelectedInventoryId() is { } id ? _inventory.GetById(id) : null;
    private static DateTime? ParseDate(string value) => DateTime.TryParse(value, out var date) ? date : null;

    private static SaveFileDialog SaveCsv(string prefix) => new()
    {
        Filter = "CSV files|*.csv",
        FileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
    };

    private static TabPage Page(string title) => new() { Text = title, Padding = new Padding(8) };
    private static FlowLayoutPanel Bar(int height = 52) => new() { Dock = DockStyle.Top, Height = height, Padding = new Padding(4), AutoScroll = true, WrapContents = true };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(8, 9, 2, 0) };
    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(4, 5, 4, 5) };
        button.Click += handler;
        return button;
    }

    private static ComboBox Combo(string first, IEnumerable<string> values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
        combo.Items.Add(first);
        foreach (var value in values)
        {
            combo.Items.Add(value);
        }
        combo.SelectedIndex = 0;
        return combo;
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };

    private sealed record PickupRow(string PickupId, DateTime? RequestDate, string SourceName, string City, string State, string ZIP, string EstimatedItems, string SourceType, string Status, DateTime? ScheduledDate, DateTime? ActualPickupDate)
    {
        public static PickupRow From(Pickup pickup) => new(pickup.PickupId, pickup.OriginalRequestDate, pickup.SourceName, pickup.City, pickup.State, pickup.ZipCode, pickup.EstimatedItemCount, pickup.SourceType, pickup.PickupStatus, pickup.ScheduledPickupAt, pickup.ActualPickupDate);
    }

    private sealed record InventoryRow(long InventoryId, string PickupId, string ISBN, string UpcEan, string ASIN, string Title, string Author, string Condition, int Quantity, DateTime? DateScanned, string MarketplaceId, string Disposition, string Archived)
    {
        public static InventoryRow From(InventoryItem item) => new(item.InventoryId, item.PickupId, item.ISBN, string.Join(" / ", new[] { item.UPC, item.EAN }.Where(v => !string.IsNullOrWhiteSpace(v))), item.ASIN, item.Title, item.Author, item.Condition, item.Quantity, item.DateScanned, item.EbayListingId, item.Disposition, item.ArchivedAt.HasValue ? "Yes" : "");
    }

    private sealed record AuditRow(long AuditId, DateTime Timestamp, string EventType, string RecordType, string RecordId, string Details)
    {
        public static AuditRow From(AuditEvent row) => new(row.AuditId, row.Timestamp, row.EventType, row.RecordType, row.RecordId, row.Details);
    }
}
