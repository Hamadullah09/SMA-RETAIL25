using FluentAssertions;
using Retail25.Application.Rfid.Import;
using Retail25.Domain.Catalog;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// The tag export, read the way it is actually written.
/// <para>
/// Every fixture here is a verbatim excerpt of the file this importer was built for, oddities
/// included: the annotation row, the hand-edited name column, EPCs transcribed as space-separated
/// hex pairs, and a handful that are not hex at all.
/// </para>
/// </summary>
public sealed class EpcCatalogCsvTests
{
    /// <summary>The real header — both halves of the join, duplicate column names and all.</summary>
    private const string Header =
        "id,product_id   PRODUCT NAME,variant_id,serial_number,epc,state FLAG,location_id,received_on," +
        "last_seen_at,created_at,created_by,modified_at,modified_by,row_version," +
        "id,location_id,stock_code,name,description,type,upc,tax1applies,tax2applies,regular_price," +
        "last_cost,avg_cost,gross_margin_pct,base_stock,reorder_point,reorder_qty,on_hand,on_order," +
        "case_qty,ship_weight,bin_location,pos_message,invoice_message,notes,department_id,category_id," +
        "substitute_product_id,tag_along_product_id,parent_product_id,is_deleted,deleted_at,deleted_by," +
        "created_at,created_by,modified_at,modified_by,,has_image";

    /// <summary>
    /// The second line of the file: somebody's notes on the export, written into the export. It
    /// quotes and doubles its own quotes, which is why the reader cannot simply split on commas.
    /// </summary>
    private const string Annotation =
        ",CHANGE TO PRODUCT NAME,,,,0 or 1,,JUST DATE NO TIME,JUST DATE NO TIME,JUST DATE NO TIME," +
        "USER'S NAME OR ID,JUST DATE NO TIME,USER'S NAME OR ID,????,????," +
        "\"ALREADY APPEARS IN COLUMN \"\"G\"\"\",,\"COLUMN \"\"B\"\" IS OK\"";

    private static string File(params string[] rows) => string.Join("\n", [Header, Annotation, .. rows]);

    /// <summary>
    /// A row from the top of the file: renamed by hand, sold in some earlier session, and carrying
    /// a full product record beside it.
    /// </summary>
    private const string RenamedRow =
        "5,SOYA SUPREME BANASPATI GHEE 1 LIT,,,E28069150000600B40A75995,Sold," +
        "a81b38df-eb18-4a21-be76-d97c83e254d2,2026-08-01 11:16:47.780 +0500,2026-08-03 15:51:35.431 +0500," +
        "2026-08-01 11:16:47.780 +0500,edc26916-8c63-43df-a496-07cfe9a2669f,2026-08-03 15:54:44.015 +0500," +
        "93e6a17c-9169-47bf-9b0b-1a4ef599e0b4,0,f92d747d-488e-4568-bbf6-125fc34024d8," +
        "a81b38df-eb18-4a21-be76-d97c83e254d2,RF-KEYB,Keyboard,,2,,FALSE,FALSE,34.99,0,0,0,0,0,0,-1,0,0,0";

    /// <summary>The same shape, but column B was never edited and still holds the old GUID.</summary>
    private const string GuidRow =
        "13,e5cb806a-82b7-410c-b054-38ce89847dc1,,,E28069150000600B40A78D95,Sold," +
        "a81b38df-eb18-4a21-be76-d97c83e254d2,2026-08-03 14:59:17.499 +0500,2026-08-03 15:52:27.230 +0500," +
        "2026-08-03 14:59:17.531 +0500,93e6a17c-9169-47bf-9b0b-1a4ef599e0b4,2026-08-03 15:54:44.015 +0500," +
        "93e6a17c-9169-47bf-9b0b-1a4ef599e0b4,0,e5cb806a-82b7-410c-b054-38ce89847dc1," +
        "a81b38df-eb18-4a21-be76-d97c83e254d2,RF-MOUS,Wireless mouse,,2,,FALSE,FALSE,19.99,0,0,0,0,0,0,-1,0,0,0";

    /// <summary>The demo block: no product record at all beyond a stock code, and no state.</summary>
    private const string DemoRow =
        "14,DEMO 201-00 1,,,41303839343030303033185E,,,,,,,,,,,,11111";

    /// <summary>The reader's own console transcription — same tag, spaces between the byte pairs.</summary>
    private const string SpacedRow =
        "114,DEMO 205-00 1,,,E2 80 11 70 00 00 02 0A 7A 6A 33 AE,,,,,,,,,,,,11120";

    /// <summary>Not an EPC. A handful of rows open with a letter that is not a hex digit.</summary>
    private const string NonHexRow =
        "34,DEMO 201-0 21,,,G2802E6020006589A0FE09F9,,,,,,,,,,,,11112";

    [Fact]
    public void The_annotation_row_is_not_imported_as_a_tag()
    {
        var parsed = EpcCatalogCsv.Parse(File(DemoRow));

        parsed.DataRows.Should().Be(1);
        parsed.Rows.Should().ContainSingle();
        parsed.Problems.Should().BeEmpty("the annotation row is how the file is built, not a fault in it");
    }

    [Fact]
    public void A_hand_edited_name_column_wins_over_the_products_own_name()
    {
        var parsed = EpcCatalogCsv.Parse(File(RenamedRow));

        var row = parsed.Rows.Should().ContainSingle().Subject;
        row.ProductName.Should().Be("SOYA SUPREME BANASPATI GHEE 1 LIT");
        row.StockCode.Should().Be("RF-KEYB");
        row.RegularPrice.Should().Be(34.99m);
    }

    [Fact]
    public void An_unedited_name_column_still_holds_a_guid_so_the_products_name_is_used()
    {
        var parsed = EpcCatalogCsv.Parse(File(GuidRow));

        parsed.Rows.Should().ContainSingle()
            .Which.ProductName.Should().Be("Wireless mouse");
    }

    [Fact]
    public void Space_separated_hex_is_the_same_tag_as_the_run_together_form()
    {
        var parsed = EpcCatalogCsv.Parse(File(SpacedRow));

        parsed.Rows.Should().ContainSingle()
            .Which.Epc.Should().Be("E28011700000020A7A6A33AE");
    }

    [Fact]
    public void A_tag_that_is_not_hexadecimal_is_reported_rather_than_imported()
    {
        var parsed = EpcCatalogCsv.Parse(File(NonHexRow, DemoRow));

        parsed.Rows.Should().ContainSingle().Which.Epc.Should().Be("41303839343030303033185E");

        var problem = parsed.Problems.Should().ContainSingle().Subject;
        problem.Value.Should().Be("G2802E6020006589A0FE09F9");
        problem.RowDropped.Should().BeTrue();
        problem.Reason.Should().Be("epc.invalid_characters");
    }

    [Fact]
    public void The_same_tag_twice_in_one_file_is_imported_once_and_the_repeat_is_reported()
    {
        var parsed = EpcCatalogCsv.Parse(File(DemoRow, DemoRow));

        parsed.DataRows.Should().Be(2);
        parsed.Rows.Should().ContainSingle();
        parsed.Problems.Should().ContainSingle()
            .Which.Reason.Should().Be("epc.duplicate_in_file");
    }

    /// <summary>
    /// The demo block has no state column filled in and the top of the file says <c>Sold</c>. Both
    /// have to land somewhere sensible without a lookup table per file.
    /// </summary>
    [Fact]
    public void An_empty_state_is_in_stock_and_a_named_state_is_read_as_written()
    {
        var parsed = EpcCatalogCsv.Parse(File(DemoRow, RenamedRow));

        parsed.Rows.Single(r => r.StockCode == "11111").State.Should().Be(SerializedUnitState.InStock);
        parsed.Rows.Single(r => r.StockCode == "RF-KEYB").State.Should().Be(SerializedUnitState.Sold);
    }

    /// <summary>The annotation asks for the state as a 0/1 flag, so a file edited that way must read.</summary>
    [Theory]
    [InlineData("0", SerializedUnitState.InStock)]
    [InlineData("1", SerializedUnitState.Sold)]
    public void The_flag_form_the_annotation_asks_for_is_read_too(string flag, SerializedUnitState expected)
    {
        var row = DemoRow.Replace("41303839343030303033185E,,", "41303839343030303033185E," + flag + ",");

        EpcCatalogCsv.Parse(File(row)).Rows.Should().ContainSingle()
            .Which.State.Should().Be(expected);
    }

    /// <summary>
    /// The type column already holds this system's values — <c>2</c> is <c>Serialized</c>. Where the
    /// demo block leaves it blank the answer is the same, because every row in this file is a tag
    /// and a tag is one physical unit.
    /// </summary>
    [Fact]
    public void Every_imported_item_is_serialized_whether_the_file_says_so_or_not()
    {
        var parsed = EpcCatalogCsv.Parse(File(RenamedRow, DemoRow));

        parsed.Rows.Should().OnlyContain(r => r.Type == ProductType.Serialized);
    }

    /// <summary>
    /// The demo block's stock code groups its tags: twenty codes carry two hundred tags between
    /// them, and the importer creates one item per code rather than one per tag.
    /// </summary>
    [Fact]
    public void Tags_sharing_a_stock_code_describe_one_item()
    {
        var second = DemoRow.Replace("41303839343030303033185E", "41303839343030303033EC5B");

        var parsed = EpcCatalogCsv.Parse(File(DemoRow, second));

        parsed.Rows.Should().HaveCount(2);
        parsed.Rows.Select(r => r.StockCode).Distinct().Should().ContainSingle().Which.Should().Be("11111");
    }

    /// <summary>
    /// A file with no EPC column is now a catalogue, not a fault.
    /// <para>
    /// This test previously required an EPC column and expected <c>header.no_epc_column</c>. That
    /// expectation was correct while this only read tag exports, and wrong the moment it became the
    /// importer a shop uses to load its stock: most shops have no RFID, and demanding a column they
    /// cannot fill meant they could not import a catalogue at all. The requirement changed, so the
    /// test pins the new one — the file still has to say which item each row is about, which is
    /// what the case below covers.
    /// </para>
    /// </summary>
    [Fact]
    public void A_file_with_no_epc_column_is_a_catalogue_rather_than_an_error()
    {
        var parsed = EpcCatalogCsv.Parse("stock code,name\nKB-1,Keyboard");

        parsed.Problems.Should().BeEmpty();
        parsed.Rows.Should().ContainSingle();
        parsed.Rows[0].StockCode.Should().Be("KB-1");
        parsed.Rows[0].Epc.Should().BeEmpty();
    }

    [Fact]
    public void A_row_with_no_stock_code_has_no_item_to_hang_the_tag_on()
    {
        var parsed = EpcCatalogCsv.Parse(File("14,DEMO 201-00 1,,,41303839343030303033185E,,,,,,,,,,,,"));

        parsed.Rows.Should().BeEmpty();
        parsed.Problems.Should().ContainSingle()
            .Which.Reason.Should().Be("row.no_stock_code");
    }

    // ---------------------------------------------------------------------------------------------
    // The other file this importer has to read: the one the import screen describes, and the one
    // anybody produces by hand — a tag and the stock code it belongs to, nothing else.

    /// <summary>
    /// Two columns, headed as a person writes them, is a file this importer must read.
    /// <para>
    /// It could not. Three separate things stopped it, each sufficient alone: rows were kept only if
    /// column one parsed as an integer, so every row of a file with no id column was dropped; the
    /// product columns were searched only after the join's seam, which in a file with no seam is an
    /// empty range, so the stock code was never found; and headers normalised spaces to spaces while
    /// the lookup used snake_case, so "Stock Code" matched nothing. The visible result was
    /// "the file held no rows this importer could use" on a file that was entirely correct.
    /// </para>
    /// </summary>
    [Fact]
    public void A_plain_two_column_file_of_tags_and_stock_codes_is_read()
    {
        var parsed = EpcCatalogCsv.Parse(
            "EPC,Stock Code\r\n"
            + "E2 80 11 70 00 00 02 0A 7A 6B 6A E1,FR0207001\r\n"
            + "E2 80 11 70 00 00 02 0A 7A 6A 9A 21,FR0207002\r\n");

        parsed.Problems.Should().BeEmpty();
        parsed.Rows.Should().HaveCount(2);

        parsed.Rows[0].Epc.Should().Be("E28011700000020A7A6B6AE1", "the spaced transcription is the same tag");
        parsed.Rows[0].StockCode.Should().Be("FR0207001");
        parsed.Rows.Select(r => r.StockCode).Should().Equal("FR0207001", "FR0207002");
    }

    /// <summary>
    /// With no name column, the stock code names the item. A tag has to hang on something, and the
    /// code is the only thing this file says about it.
    /// </summary>
    [Fact]
    public void An_item_named_by_nothing_but_its_stock_code_takes_that_as_its_name()
    {
        var parsed = EpcCatalogCsv.Parse("EPC,Stock Code\nE28069150000600B40A75995,FR0207001");

        parsed.Rows.Should().ContainSingle()
            .Which.ProductName.Should().Be("FR0207001");
    }

    /// <summary>A blank trailing line is how a file ends, not something to report.</summary>
    [Fact]
    public void A_trailing_blank_line_is_not_a_problem_worth_reporting()
    {
        var parsed = EpcCatalogCsv.Parse("EPC,Stock Code\nE28069150000600B40A75995,FR0207001\n\n");

        parsed.Rows.Should().ContainSingle();
        parsed.Problems.Should().BeEmpty();
    }

    /// <summary>
    /// snake_case and the spaced spelling are the same column. A spreadsheet writes one or the other
    /// depending on who made the file.
    /// </summary>
    [Theory]
    [InlineData("epc,stock_code")]
    [InlineData("EPC,Stock Code")]
    [InlineData("Epc , STOCK CODE")]
    public void The_stock_code_column_is_found_however_its_header_is_spelled(string header)
    {
        var parsed = EpcCatalogCsv.Parse(header + "\nE28069150000600B40A75995,FR0207001");

        parsed.Rows.Should().ContainSingle()
            .Which.StockCode.Should().Be("FR0207001");
    }

    // --- The one-file catalogue ------------------------------------------------------------
    //
    // The shape a shopkeeper actually has: one sheet, headed however they head it, holding the item
    // and everything about it side by side. These pin that it imports without a second file, a
    // conversion step, or an RFID tag.

    /// <summary>
    /// Headed the way a person writes headings — spaces, capitals, and names of their own choosing.
    /// A file rejected for saying "Qty" instead of "on_hand" is a file the shopkeeper cannot fix
    /// without being told the secret.
    /// </summary>
    [Fact]
    public void A_plain_sheet_with_human_headings_imports()
    {
        const string csv =
            "Stock Code,Item Name,Department,Category,Supplier,Barcode,Cost,Price,Qty,Bin\n" +
            "SHIRT-01,Blue Shirt,Menswear,Shirts,Acme Textiles,5012345678900,900,1500,12,A3\n";

        var parsed = EpcCatalogCsv.Parse(csv);

        parsed.Rows.Should().HaveCount(1);

        var row = parsed.Rows[0];
        row.StockCode.Should().Be("SHIRT-01");
        row.ProductName.Should().Be("Blue Shirt");
        row.Department.Should().Be("Menswear");
        row.Category.Should().Be("Shirts");
        row.Supplier.Should().Be("Acme Textiles");
        row.Barcode.Should().Be("5012345678900");
        row.BinLocation.Should().Be("A3");
        row.Cost.Should().Be(900m);
        row.RegularPrice.Should().Be(1500m);
        row.OnHand.Should().Be(12m);
        row.Epc.Should().BeEmpty("this shop has no tags");
    }

    /// <summary>
    /// An untagged item is ordinary stock counted by quantity. Calling it serialized would demand a
    /// tag per unit from a shop that has none, and every sale would ask which numbered one was going.
    /// </summary>
    [Fact]
    public void An_untagged_item_is_standard_stock_and_a_tagged_one_is_serialized()
    {
        const string csv =
            "stock code,name,epc\n" +
            "PLAIN-1,Socks,\n" +
            "TAGGED-1,Jacket,E28011606000020C1B3E1234\n";

        var parsed = EpcCatalogCsv.Parse(csv);

        parsed.Rows.Should().HaveCount(2);
        parsed.Rows[0].Type.Should().Be(ProductType.Standard);
        parsed.Rows[1].Type.Should().Be(ProductType.Serialized);
    }

    /// <summary>
    /// A blank cost is not a cost of zero. Zero would make the first margin report announce that the
    /// whole catalogue is pure profit, which is a worse answer than declining to say.
    /// </summary>
    [Fact]
    public void An_absent_number_stays_absent_rather_than_becoming_zero()
    {
        const string csv =
            "stock code,name,cost,qty\n" +
            "A-1,Thing,,\n";

        var row = EpcCatalogCsv.Parse(csv).Rows.Single();

        row.Cost.Should().BeNull();
        row.OnHand.Should().BeNull();
    }

    /// <summary>A spreadsheet writes 1,250.00 and means 1250.</summary>
    [Fact]
    public void A_thousands_separator_is_read_as_a_number()
    {
        const string csv =
            "stock code,name,price,cost\n" +
            "A-1,Thing,\"1,250.00\",\"999.50\"\n";

        var row = EpcCatalogCsv.Parse(csv).Rows.Single();

        row.RegularPrice.Should().Be(1250.00m);
        row.Cost.Should().Be(999.50m);
    }

    /// <summary>
    /// Without a stock code there is nothing to attach a row to, and the message has to say which
    /// column is missing — "no rows this importer could use" sends somebody hunting.
    /// </summary>
    [Fact]
    public void A_file_with_no_stock_code_column_says_which_column_it_wanted()
    {
        const string csv = "name,price\nThing,10\n";

        var parsed = EpcCatalogCsv.Parse(csv);

        parsed.Rows.Should().BeEmpty();
        parsed.Problems.Should().ContainSingle()
            .Which.Reason.Should().Be("header.no_stock_code_column");
    }

    /// <summary>
    /// Both spellings of the same field, on one file. UPC is the more specific name, so it wins;
    /// the importer stores one barcode and must not pick at random.
    /// </summary>
    [Fact]
    public void Upc_and_barcode_are_both_read_when_both_are_present()
    {
        const string csv =
            "stock code,name,barcode,upc\n" +
            "A-1,Thing,111,222\n";

        var row = EpcCatalogCsv.Parse(csv).Rows.Single();

        row.Barcode.Should().Be("111");
        row.Upc.Should().Be("222");
    }

    /// <summary>
    /// Weight is the column the till's WEIGHT panel reads. It stays blank at zero on purpose, so a
    /// catalogue that has never been weighed shows blanks rather than a column of noughts — which
    /// means importing it is the only way a shop fills that column without opening every item.
    /// </summary>
    [Fact]
    public void Weight_and_the_ordering_figures_are_read()
    {
        const string csv =
            "stock code,name,weight,case qty,reorder point,reorder qty,base stock\n" +
            "A-1,Thing,0.4,12,3,24,6\n";

        var row = EpcCatalogCsv.Parse(csv).Rows.Single();

        row.Weight.Should().Be(0.4m);
        row.CaseQty.Should().Be(12m);
        row.ReorderPoint.Should().Be(3);
        row.ReorderQty.Should().Be(24);
        row.BaseStock.Should().Be(6);
    }

    /// <summary>
    /// A tax column absent means "keep the default". Reading a missing column as "no" is the one
    /// mistake here that shows up as missing money rather than as a wrong-looking screen.
    /// </summary>
    [Theory]
    [InlineData("Yes", true)]
    [InlineData("y", true)]
    [InlineData("1", true)]
    [InlineData("TRUE", true)]
    [InlineData("No", false)]
    [InlineData("0", false)]
    public void A_tax_column_is_read_in_the_spellings_a_spreadsheet_holds(string cell, bool expected)
    {
        var row = EpcCatalogCsv.Parse($"stock code,name,tax1\nA-1,Thing,{cell}\n").Rows.Single();

        row.Tax1Applies.Should().Be(expected);
    }

    [Fact]
    public void An_absent_tax_column_leaves_the_default_alone()
    {
        var row = EpcCatalogCsv.Parse("stock code,name\nA-1,Thing\n").Rows.Single();

        row.Tax1Applies.Should().BeNull();
        row.Tax2Applies.Should().BeNull();
    }

    [Fact]
    public void The_messages_and_the_image_link_are_read()
    {
        const string csv =
            "stock code,name,pos message,invoice message,notes,image url\n" +
            "A-1,Thing,Check the zip,Dry clean only,Bought in 2026,https://example.test/a.jpg\n";

        var row = EpcCatalogCsv.Parse(csv).Rows.Single();

        row.PosMessage.Should().Be("Check the zip");
        row.InvoiceMessage.Should().Be("Dry clean only");
        row.Notes.Should().Be("Bought in 2026");
        row.ImageUrl.Should().Be("https://example.test/a.jpg");
    }
}
