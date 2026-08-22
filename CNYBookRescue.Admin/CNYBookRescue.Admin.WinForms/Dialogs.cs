using CNYBookRescue.Core;
using CNYBookRescue.Data;
using System.Drawing.Printing;
using System.Globalization;

namespace CNYBookRescue.Admin.WinForms;

public sealed class PickupDialog : Form
{
    private readonly TextBox _firstName = Box();
    private readonly TextBox _lastName = Box();
    private readonly TextBox _email = Box();
    private readonly TextBox _cell = Box();
    private readonly TextBox _organization = Box();
    private readonly TextBox _sourceContact = Box();
    private readonly TextBox _sourcePhone = Box();
    private readonly TextBox _sourceEmail = Box();
    private readonly TextBox _street = Box();
    private readonly TextBox _address2 = Box();
    private readonly TextBox _city = Box();
    private readonly TextBox _state = new() { Width = 80, Text = "NY" };
    private readonly TextBox _zip = Box();
    private readonly TextBox _estimate = Box();
    private readonly TextBox _actualCount = Box();
    private readonly CheckBox _large = new() { Text = "Large collection" };
    private readonly CheckBox _clothing = new() { Text = "Includes unwanted clothing" };
    private readonly TextBox _comments = Multi();
    private readonly ComboBox _ownership = Combo(OwnershipTransferStatuses.All);
    private readonly CheckBox _thirdParty = new() { Text = "Third-party authority" };
    private readonly TextBox _relationship = Box();
    private readonly ComboBox _status = Combo(PickupStatuses.All);
    private readonly ComboBox _source = Combo(SourceTypes.All);
    private readonly DateTimePicker _requestDate = DatePicker();
    private readonly DateTimePicker _scheduled = DatePicker();
    private readonly DateTimePicker _actualPickup = DatePicker();
    private readonly TextBox _notes = Multi();

    public Pickup Pickup { get; }

    public PickupDialog(Pickup pickup)
    {
        Pickup = pickup;
        Text = string.IsNullOrWhiteSpace(pickup.PickupId) ? "New Pickup" : $"Pickup {pickup.PickupId}";
        Width = 860;
        Height = 780;
        MinimumSize = new Size(720, 620);
        StartPosition = FormStartPosition.CenterParent;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        Add(panel, "First Name", _firstName, "Last Name", _lastName);
        Add(panel, "Email", _email, "Cell", _cell);
        Add(panel, "Organization", _organization, "Source Contact", _sourceContact);
        Add(panel, "Source Phone", _sourcePhone, "Source Email", _sourceEmail);
        Add(panel, "Street Address", _street, "Address Line 2", _address2);
        Add(panel, "City", _city, "State", _state);
        Add(panel, "ZIP", _zip, "Estimated Items", _estimate);
        Add(panel, "Actual Count", _actualCount, "Source Type", _source);
        Add(panel, "Request Date", _requestDate, "Scheduled", _scheduled);
        Add(panel, "Actual Pickup", _actualPickup, "Status", _status);
        Add(panel, "Ownership", _ownership, "Authority Relationship", _relationship);
        Add(panel, "", _large, "", _clothing);
        Add(panel, "", _thirdParty, "", new Label());
        AddWide(panel, "Comments", _comments);
        AddWide(panel, "Internal Notes", _notes);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48 };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 100 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 100 };
        save.Click += (_, _) => Save();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(panel);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
        LoadPickup();
    }

    private void LoadPickup()
    {
        _firstName.Text = Pickup.FirstName;
        _lastName.Text = Pickup.LastName;
        _email.Text = Pickup.Email;
        _cell.Text = Pickup.CellNumber;
        _organization.Text = Pickup.OrganizationName;
        _sourceContact.Text = Pickup.SourceContactName;
        _sourcePhone.Text = Pickup.SourcePhone;
        _sourceEmail.Text = Pickup.SourceEmail;
        _street.Text = Pickup.StreetAddress;
        _address2.Text = Pickup.AddressLine2;
        _city.Text = Pickup.City;
        _state.Text = string.IsNullOrWhiteSpace(Pickup.State) ? "NY" : Pickup.State;
        _zip.Text = Pickup.ZipCode;
        _estimate.Text = Pickup.EstimatedItemCount;
        _actualCount.Text = Pickup.ActualItemCount?.ToString(CultureInfo.InvariantCulture);
        _large.Checked = Pickup.LargeCollection;
        _clothing.Checked = Pickup.ClothingPickup;
        _comments.Text = Pickup.Comments;
        _ownership.SelectedItem = Pickup.OwnershipTransferStatus;
        _thirdParty.Checked = Pickup.ThirdPartyAuthority;
        _relationship.Text = Pickup.AuthorityRelationship;
        _status.SelectedItem = Pickup.PickupStatus;
        _source.SelectedItem = Pickup.SourceType;
        SetDate(_requestDate, Pickup.OriginalRequestDate);
        SetDate(_scheduled, Pickup.ScheduledPickupAt);
        SetDate(_actualPickup, Pickup.ActualPickupDate);
        _notes.Text = Pickup.InternalNotes;
    }

    private void Save()
    {
        Pickup.FirstName = _firstName.Text;
        Pickup.LastName = _lastName.Text;
        Pickup.Email = _email.Text;
        Pickup.CellNumber = _cell.Text;
        Pickup.OrganizationName = _organization.Text;
        Pickup.SourceContactName = _sourceContact.Text;
        Pickup.SourcePhone = _sourcePhone.Text;
        Pickup.SourceEmail = _sourceEmail.Text;
        Pickup.StreetAddress = _street.Text;
        Pickup.AddressLine2 = _address2.Text;
        Pickup.City = _city.Text;
        Pickup.State = _state.Text;
        Pickup.ZipCode = _zip.Text;
        Pickup.EstimatedItemCount = _estimate.Text;
        Pickup.ActualItemCount = int.TryParse(_actualCount.Text, out var count) ? count : null;
        Pickup.LargeCollection = _large.Checked;
        Pickup.ClothingPickup = _clothing.Checked;
        Pickup.Comments = _comments.Text;
        Pickup.OwnershipTransferStatus = _ownership.Text;
        Pickup.OwnershipTransferConfirmedAt = _ownership.Text == OwnershipTransferStatuses.Confirmed && Pickup.OwnershipTransferConfirmedAt is null ? DateTime.Now : Pickup.OwnershipTransferConfirmedAt;
        Pickup.ThirdPartyAuthority = _thirdParty.Checked;
        Pickup.AuthorityRelationship = _relationship.Text;
        Pickup.PickupStatus = _status.Text;
        Pickup.SourceType = _source.Text;
        Pickup.OriginalRequestDate = GetDate(_requestDate);
        Pickup.ScheduledPickupAt = GetDate(_scheduled);
        Pickup.ActualPickupDate = GetDate(_actualPickup);
        Pickup.PickupCompletedAt = _status.Text == PickupStatuses.Completed && Pickup.PickupCompletedAt is null ? DateTime.Now : Pickup.PickupCompletedAt;
        Pickup.InternalNotes = _notes.Text;
    }

    internal static void Add(TableLayoutPanel panel, string leftLabel, Control left, string rightLabel, Control right)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = leftLabel, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) });
        panel.Controls.Add(left);
        panel.Controls.Add(new Label { Text = rightLabel, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(8, 6, 0, 0) });
        panel.Controls.Add(right);
    }

    internal static void AddWide(TableLayoutPanel panel, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) });
        panel.SetColumnSpan(control, 3);
        panel.Controls.Add(control);
    }

    internal static TextBox Box() => new() { Width = 230, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    internal static TextBox Multi() => new() { Multiline = true, Height = 74, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    internal static ComboBox Combo(IEnumerable<string> values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        foreach (var value in values) combo.Items.Add(value);
        combo.SelectedIndex = 0;
        return combo;
    }

    internal static DateTimePicker DatePicker() => new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Width = 210 };
    internal static DateTime? GetDate(DateTimePicker picker) => picker.Checked ? picker.Value : null;
    internal static void SetDate(DateTimePicker picker, DateTime? value)
    {
        if (value.HasValue)
        {
            picker.Value = value.Value;
            picker.Checked = true;
        }
        else
        {
            picker.Checked = false;
        }
    }
}

public sealed class InventoryDialog : Form
{
    private readonly TextBox _barcode = PickupDialog.Box();
    private readonly TextBox _pickupId = PickupDialog.Box();
    private readonly TextBox _isbn = PickupDialog.Box();
    private readonly TextBox _upc = PickupDialog.Box();
    private readonly TextBox _ean = PickupDialog.Box();
    private readonly TextBox _asin = PickupDialog.Box();
    private readonly TextBox _title = PickupDialog.Box();
    private readonly TextBox _author = PickupDialog.Box();
    private readonly TextBox _mediaType = PickupDialog.Box();
    private readonly TextBox _condition = PickupDialog.Box();
    private readonly NumericUpDown _quantity = new() { Minimum = 1, Maximum = 100000, Value = 1, Width = 120 };
    private readonly ComboBox _disposition = PickupDialog.Combo(InventoryDispositions.All);
    private readonly TextBox _amazonCatalog = PickupDialog.Box();
    private readonly TextBox _amazonEligibility = PickupDialog.Box();
    private readonly TextBox _amazonCondition = PickupDialog.Box();
    private readonly DateTimePicker _amazonChecked = PickupDialog.DatePicker();
    private readonly TextBox _buybackVendor = PickupDialog.Box();
    private readonly TextBox _buybackQuote = PickupDialog.Box();
    private readonly DateTimePicker _buybackQuoteDate = PickupDialog.DatePicker();
    private readonly DateTimePicker _buybackSubmittedDate = PickupDialog.DatePicker();
    private readonly TextBox _buybackPayout = PickupDialog.Box();
    private readonly DateTimePicker _buybackPayoutDate = PickupDialog.DatePicker();
    private readonly TextBox _ebayExpected = PickupDialog.Box();
    private readonly TextBox _ebayListingId = PickupDialog.Box();
    private readonly DateTimePicker _ebayListed = PickupDialog.DatePicker();
    private readonly DateTimePicker _ebaySold = PickupDialog.DatePicker();
    private readonly TextBox _ebayGross = PickupDialog.Box();
    private readonly TextBox _ebayFees = PickupDialog.Box();
    private readonly TextBox _ebayNet = PickupDialog.Box();
    private readonly TextBox _notes = PickupDialog.Multi();

    public InventoryItem Item { get; }

    public InventoryDialog(InventoryItem item)
    {
        Item = item;
        Text = item.InventoryId == 0 ? "Add Inventory Item" : $"Inventory #{item.InventoryId}";
        Width = 900;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        PickupDialog.AddWide(panel, "Scan ISBN / UPC / EAN", _barcode);
        PickupDialog.Add(panel, "Pickup ID", _pickupId, "Disposition", _disposition);
        PickupDialog.Add(panel, "ISBN", _isbn, "UPC", _upc);
        PickupDialog.Add(panel, "EAN", _ean, "ASIN", _asin);
        PickupDialog.Add(panel, "Title", _title, "Author", _author);
        PickupDialog.Add(panel, "Media Type", _mediaType, "Condition", _condition);
        PickupDialog.Add(panel, "Quantity", _quantity, "Amazon Catalog", _amazonCatalog);
        PickupDialog.Add(panel, "Amazon Eligibility", _amazonEligibility, "Amazon Condition", _amazonCondition);
        PickupDialog.Add(panel, "Amazon Last Checked", _amazonChecked, "Buyback Vendor", _buybackVendor);
        PickupDialog.Add(panel, "Buyback Quote", _buybackQuote, "Buyback Quote Date", _buybackQuoteDate);
        PickupDialog.Add(panel, "Buyback Submitted", _buybackSubmittedDate, "Buyback Payout", _buybackPayout);
        PickupDialog.Add(panel, "Buyback Payout Date", _buybackPayoutDate, "eBay Expected", _ebayExpected);
        PickupDialog.Add(panel, "eBay Listing ID", _ebayListingId, "eBay Listed", _ebayListed);
        PickupDialog.Add(panel, "eBay Sold", _ebaySold, "eBay Gross", _ebayGross);
        PickupDialog.Add(panel, "eBay Fees", _ebayFees, "eBay Net", _ebayNet);
        PickupDialog.AddWide(panel, "Notes", _notes);

        _barcode.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyBarcode(_barcode.Text.Trim());
                _barcode.SelectAll();
                e.SuppressKeyPress = true;
            }
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48 };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 100 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 100 };
        save.Click += (_, _) => Save();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(panel);
        Controls.Add(buttons);
        LoadItem();
        Shown += (_, _) => _barcode.Focus();
    }

    private void ApplyBarcode(string barcode)
    {
        barcode = new string(barcode.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray());
        if (barcode.Length is 10 or 13)
        {
            _isbn.Text = barcode;
        }
        else if (barcode.Length == 12)
        {
            _upc.Text = barcode;
        }
        else if (!string.IsNullOrWhiteSpace(barcode))
        {
            _ean.Text = barcode;
        }
    }

    private void LoadItem()
    {
        _pickupId.Text = Item.PickupId;
        _isbn.Text = Item.ISBN;
        _upc.Text = Item.UPC;
        _ean.Text = Item.EAN;
        _asin.Text = Item.ASIN;
        _title.Text = Item.Title;
        _author.Text = Item.Author;
        _mediaType.Text = Item.MediaType;
        _condition.Text = Item.Condition;
        _quantity.Value = Item.Quantity <= 0 ? 1 : Item.Quantity;
        _disposition.SelectedItem = Item.Disposition;
        _amazonCatalog.Text = Item.AmazonCatalogStatus;
        _amazonEligibility.Text = Item.AmazonEligibilityStatus;
        _amazonCondition.Text = Item.AmazonCondition;
        PickupDialog.SetDate(_amazonChecked, Item.AmazonLastCheckedAt);
        _buybackVendor.Text = Item.BuybackVendor;
        _buybackQuote.Text = Item.BuybackQuotedAmount?.ToString(CultureInfo.InvariantCulture);
        PickupDialog.SetDate(_buybackQuoteDate, Item.BuybackQuoteDate);
        PickupDialog.SetDate(_buybackSubmittedDate, Item.BuybackSubmittedDate);
        _buybackPayout.Text = Item.BuybackPayoutAmount?.ToString(CultureInfo.InvariantCulture);
        PickupDialog.SetDate(_buybackPayoutDate, Item.BuybackPayoutDate);
        _ebayExpected.Text = Item.EbayExpectedSalePrice?.ToString(CultureInfo.InvariantCulture);
        _ebayListingId.Text = Item.EbayListingId;
        PickupDialog.SetDate(_ebayListed, Item.EbayListedDate);
        PickupDialog.SetDate(_ebaySold, Item.EbaySoldDate);
        _ebayGross.Text = Item.EbayGrossProceeds?.ToString(CultureInfo.InvariantCulture);
        _ebayFees.Text = Item.EbayFees?.ToString(CultureInfo.InvariantCulture);
        _ebayNet.Text = Item.EbayNetProceeds?.ToString(CultureInfo.InvariantCulture);
        _notes.Text = Item.Notes;
    }

    private void Save()
    {
        Item.PickupId = _pickupId.Text.Trim();
        Item.ISBN = _isbn.Text;
        Item.UPC = _upc.Text;
        Item.EAN = _ean.Text;
        Item.ASIN = _asin.Text;
        Item.Title = _title.Text;
        Item.Author = _author.Text;
        Item.MediaType = string.IsNullOrWhiteSpace(_mediaType.Text) ? "Book" : _mediaType.Text;
        Item.Condition = _condition.Text;
        Item.Quantity = (int)_quantity.Value;
        Item.Disposition = _disposition.Text;
        Item.AmazonCatalogStatus = _amazonCatalog.Text;
        Item.AmazonEligibilityStatus = _amazonEligibility.Text;
        Item.AmazonCondition = _amazonCondition.Text;
        Item.AmazonLastCheckedAt = PickupDialog.GetDate(_amazonChecked);
        Item.BuybackVendor = _buybackVendor.Text;
        Item.BuybackQuotedAmount = DecimalOrNull(_buybackQuote.Text);
        Item.BuybackQuoteDate = PickupDialog.GetDate(_buybackQuoteDate);
        Item.BuybackSubmittedDate = PickupDialog.GetDate(_buybackSubmittedDate);
        Item.BuybackPayoutAmount = DecimalOrNull(_buybackPayout.Text);
        Item.BuybackPayoutDate = PickupDialog.GetDate(_buybackPayoutDate);
        Item.EbayExpectedSalePrice = DecimalOrNull(_ebayExpected.Text);
        Item.EbayListingId = _ebayListingId.Text;
        Item.EbayListedDate = PickupDialog.GetDate(_ebayListed);
        Item.EbaySoldDate = PickupDialog.GetDate(_ebaySold);
        Item.EbayGrossProceeds = DecimalOrNull(_ebayGross.Text);
        Item.EbayFees = DecimalOrNull(_ebayFees.Text);
        Item.EbayNetProceeds = DecimalOrNull(_ebayNet.Text);
        Item.Notes = _notes.Text;
        Item.DateScanned ??= DateTime.Now;
    }

    private static decimal? DecimalOrNull(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;
}

public sealed class PhotoDialog : Form
{
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _caption = new() { Width = 360 };

    public string PhotoType => _type.Text;
    public string Caption => _caption.Text;

    public PhotoDialog()
    {
        Text = "Photo Details";
        Width = 480;
        Height = 190;
        StartPosition = FormStartPosition.CenterParent;
        foreach (var value in PhotoTypes.All) _type.Items.Add(value);
        _type.SelectedIndex = 0;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        panel.Controls.Add(new Label { Text = "Category", AutoSize = true });
        panel.Controls.Add(_type);
        panel.Controls.Add(new Label { Text = "Caption", AutoSize = true });
        panel.Controls.Add(_caption);

        var buttons = Buttons();
        Controls.Add(panel);
        Controls.Add(buttons);
    }

    internal static FlowLayoutPanel Buttons()
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        buttons.Controls.Add(new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 });
        buttons.Controls.Add(new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 });
        return buttons;
    }
}

public sealed class DocumentDialog : Form
{
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _description = new() { Width = 360 };

    public string DocumentType => _type.Text;
    public string Description => _description.Text;

    public DocumentDialog()
    {
        Text = "Document Details";
        Width = 520;
        Height = 200;
        StartPosition = FormStartPosition.CenterParent;
        foreach (var value in DocumentTypes.All) _type.Items.Add(value);
        _type.SelectedIndex = 0;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        panel.Controls.Add(new Label { Text = "Document Type", AutoSize = true });
        panel.Controls.Add(_type);
        panel.Controls.Add(new Label { Text = "Description", AutoSize = true });
        panel.Controls.Add(_description);
        Controls.Add(panel);
        Controls.Add(PhotoDialog.Buttons());
    }
}

public sealed class EvidenceDialog : Form
{
    private readonly DataGridView _photos = Grid();
    private readonly DataGridView _documents = Grid();
    private readonly PhotoRepository _photoRepository;
    private readonly DocumentRepository _documentRepository;

    public EvidenceDialog(IEnumerable<PickupPhoto> photos, IEnumerable<PickupDocument> documents, PhotoRepository photoRepository, DocumentRepository documentRepository)
    {
        _photoRepository = photoRepository;
        _documentRepository = documentRepository;
        Text = "Supporting Evidence";
        Width = 1000;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        _photos.DataSource = photos.Select(p => new PhotoRow(p.PhotoId, p.PickupId, p.InventoryId, p.PhotoType, p.Caption, p.StoragePath, p.CreatedAt)).ToList();
        _documents.DataSource = documents.Select(d => new DocumentRow(d.DocumentId, d.PickupId, d.InventoryId, d.DocumentType, d.Description, d.OriginalFileName, d.StoragePath, d.CreatedAt)).ToList();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Tab("Photos", _photos));
        tabs.TabPages.Add(Tab("Documents", _documents));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90 };
        var open = new Button { Text = "Open Selected", Width = 120 };
        var archive = new Button { Text = "Archive Selected", Width = 130 };
        open.Click += (_, _) => OpenSelected();
        archive.Click += (_, _) => ArchiveSelected();
        toolbar.Controls.Add(close);
        toolbar.Controls.Add(archive);
        toolbar.Controls.Add(open);

        Controls.Add(tabs);
        Controls.Add(toolbar);
    }

    private void OpenSelected()
    {
        if (_photos.Focused && _photos.CurrentRow?.DataBoundItem is PhotoRow photo)
        {
            FileLauncher.Open(photo.StoragePath);
        }
        else if (_documents.CurrentRow?.DataBoundItem is DocumentRow document)
        {
            FileLauncher.Open(document.StoragePath);
        }
    }

    private void ArchiveSelected()
    {
        if (_photos.Focused && _photos.CurrentRow?.DataBoundItem is PhotoRow photo)
        {
            _photoRepository.Archive(photo.PhotoId);
            MessageBox.Show(this, "Photo archived. Reopen Evidence to refresh the list.", "Archived");
        }
        else if (_documents.CurrentRow?.DataBoundItem is DocumentRow document)
        {
            _documentRepository.Archive(document.DocumentId);
            MessageBox.Show(this, "Document archived. Reopen Evidence to refresh the list.", "Archived");
        }
    }

    private static TabPage Tab(string title, Control control)
    {
        var page = new TabPage(title);
        page.Controls.Add(control);
        return page;
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };

    private sealed record PhotoRow(long PhotoId, string PickupId, long? InventoryId, string PhotoType, string Caption, string StoragePath, DateTime CreatedAt);
    private sealed record DocumentRow(long DocumentId, string PickupId, long? InventoryId, string DocumentType, string Description, string OriginalFileName, string StoragePath, DateTime CreatedAt);
}

public sealed class TextViewer : Form
{
    private readonly TextBox _textBox;

    public TextViewer(string title, string text)
    {
        Text = title;
        Width = 860;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;

        _textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10F),
            Text = text
        };

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90 };
        var print = new Button { Text = "Print", Width = 90 };
        var save = new Button { Text = "Save Text", Width = 100 };
        print.Click += (_, _) => Print();
        save.Click += (_, _) => SaveText();
        toolbar.Controls.Add(close);
        toolbar.Controls.Add(print);
        toolbar.Controls.Add(save);

        Controls.Add(_textBox);
        Controls.Add(toolbar);
    }

    private void SaveText()
    {
        using var file = new SaveFileDialog { Filter = "Text files|*.txt", FileName = $"{Text.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.txt" };
        if (file.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(file.FileName, _textBox.Text);
            MessageBox.Show(this, "Text report saved.", "Saved");
        }
    }

    private void Print()
    {
        var remaining = _textBox.Text;
        using var document = new PrintDocument();
        document.DocumentName = Text;
        document.PrintPage += (_, e) =>
        {
            var font = new Font("Consolas", 9F);
            var chars = 0;
            var lines = 0;
            e.Graphics!.MeasureString(remaining, font, e.MarginBounds.Size, StringFormat.GenericTypographic, out chars, out lines);
            e.Graphics.DrawString(remaining[..chars], font, Brushes.Black, e.MarginBounds, StringFormat.GenericTypographic);
            remaining = remaining[chars..];
            e.HasMorePages = remaining.Length > 0;
        };
        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            document.Print();
        }
    }
}
