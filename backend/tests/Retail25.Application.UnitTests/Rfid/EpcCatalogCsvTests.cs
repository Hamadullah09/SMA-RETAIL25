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

    [Fact]
    public void A_file_with_no_epc_column_says_so_instead_of_importing_nothing_quietly()
    {
        var parsed = EpcCatalogCsv.Parse("id,name\n1,Keyboard");

        parsed.Rows.Should().BeEmpty();
        parsed.Problems.Should().ContainSingle()
            .Which.Reason.Should().Be("header.no_epc_column");
    }

    [Fact]
    public void A_row_with_no_stock_code_has_no_item_to_hang_the_tag_on()
    {
        var parsed = EpcCatalogCsv.Parse(File("14,DEMO 201-00 1,,,41303839343030303033185E,,,,,,,,,,,,"));

        parsed.Rows.Should().BeEmpty();
        parsed.Problems.Should().ContainSingle()
            .Which.Reason.Should().Be("row.no_stock_code");
    }
}
