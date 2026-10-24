using static RetailPlus.Migration.Tests.TestField;

namespace RetailPlus.Migration.Tests;

/// <summary>
/// A small, invented Retail Plus data folder (location TST) with the cases the importer must handle: deleted records,
/// duplicate stock codes, customer numbers and invoice numbers, names missing from the lists, an overflowing numeric,
/// packed memos, a kit pointing at a missing part, and passwords that must never be imported.
/// </summary>
internal sealed class SyntheticDataRoot : IDisposable
{
    public const string Password1 = "Pw-Never-Stored-1";
    public const string Password2 = "Pw-Never-Stored-2";

    public SyntheticDataRoot()
    {
        Folder = Path.Combine(Path.GetTempPath(), "RetailPlusImportTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(Folder, "TST", "PO"));
        WriteRootTables();
        WriteLocationTables(Path.Combine(Folder, "TST"));
    }

    public string Folder { get; }

    private static readonly TestField[] PartyFields =
    [
        C("FNAME", 12), C("LNAME", 16), C("COMPANY", 30), C("CITY", 18), C("CUSTNO", 11), C("ACCTNO", 10),
        N("POINTS", 11, 2), N("PRICELEVEL", 1), N("DISCOUNT", 6, 2), N("CREDIT", 7), N("BALANCE", 11, 2), L("PR"), M("NOTES"),
    ];

    private static readonly TestField[] InventoryFields =
    [
        C("PROD", 30), C("PLU", 16), C("DEPT", 15), C("ITEMTYPE", 12), C("CATEGORY", 15), N("ONHAND", 10, 2), N("UNITPRICE1", 10, 2),
        N("LASTCOST", 11, 3), C("SUPPLIER", 30), N("JAN_VOLUME", 10, 2), N("JAN_GROSS", 10, 2), L("TX1"), L("PRTFLAG"),
        C("LASTYR", 150), C("WEEKLY", 600), C("PICPATH", 100), M("KIT"), M("MATRIX"), M("SUPLIST"), M("SERNO"),
    ];

    private void WriteRootTables()
    {
        TestDbfWriter.Write(Path.Combine(Folder, "SETUP.DBF"),
            [C("LOC", 50), C("TAX1NAME", 20), N("TAX1", 6, 3), C("COMPANY", 30), L("UK_DATE"), N("GIFTCARDMAX", 10, 2), N("MINIMUM_COI", 10, 2), N("CCARDCHRG", 3, 2), L("YNSAVESALE")],
            [new TestRecord { ["LOC"] = "TST", ["TAX1NAME"] = "TAX A", ["TAX1"] = "5.000", ["COMPANY"] = "TEST STORE", ["UK_DATE"] = "F", ["GIFTCARDMAX"] = "**********", ["MINIMUM_COI"] = "0.05", ["CCARDCHRG"] = "***", ["YNSAVESALE"] = "T" }]);

        TestDbfWriter.Write(Path.Combine(Folder, "NUMBERS.DBF"),
            [N("LASTINVNO", 8), N("LASTCUSTNO", 6), N("LAST_PO", 10), N("LAST_RMA", 10)],
            [new TestRecord { ["LASTINVNO"] = "500", ["LASTCUSTNO"] = "20", ["LAST_PO"] = "7", ["LAST_RMA"] = "3" }]);

        TestDbfWriter.Write(Path.Combine(Folder, "DEPT.DBF"), [C("DEPT", 15)],
            [new TestRecord { ["DEPT"] = "TOOLS" }, new TestRecord { ["DEPT"] = "PARTS" }, new TestRecord { ["DEPT"] = "TOOLS" }]);

        TestDbfWriter.Write(Path.Combine(Folder, "SUPPLIER.DBF"), PartyFields,
            [new TestRecord { ["COMPANY"] = "ACME SUPPLY", ["CUSTNO"] = "S1" }]);

        TestDbfWriter.Write(Path.Combine(Folder, "CLIENTS.DBF"), PartyFields,
        [
            new TestRecord { ["FNAME"] = "Ann", ["LNAME"] = "Example", ["CUSTNO"] = "21", ["ACCTNO"] = "AC21", ["BALANCE"] = "10.50", ["PRICELEVEL"] = "2", ["NOTES"] = "first client" },
            new TestRecord { ["FNAME"] = "Ben", ["LNAME"] = "Removed", ["CUSTNO"] = "22", ["BALANCE"] = "99.00", Deleted = true },
            new TestRecord { ["FNAME"] = "Cy", ["LNAME"] = "Duplicate", ["CUSTNO"] = "21", ["BALANCE"] = "5.25", ["PRICELEVEL"] = "7" },
        ]);

        TestDbfWriter.Write(Path.Combine(Folder, "INVOICES.DBF"),
            [C("LNAME", 16), N("INV_NO", 8), D("INV_DATE"), N("TOTAL", 12, 2), N("BALANCE", 12, 2), C("ACCTNO", 10), N("DOCTYPE", 1), M("NOTES")],
        [
            new TestRecord { ["LNAME"] = "Example", ["INV_NO"] = "501", ["INV_DATE"] = "20261001", ["TOTAL"] = "10.50", ["BALANCE"] = "10.50", ["ACCTNO"] = "AC21", ["DOCTYPE"] = "1", ["NOTES"] = "line detail" },
            new TestRecord { ["LNAME"] = "Example", ["INV_NO"] = "502", ["INV_DATE"] = "20261002", ["TOTAL"] = "40.00", ["BALANCE"] = "40.00", ["DOCTYPE"] = "2" },
            new TestRecord { ["LNAME"] = "Again", ["INV_NO"] = "501", ["INV_DATE"] = "20261003", ["TOTAL"] = "7.00", ["BALANCE"] = "0.00", ["DOCTYPE"] = "1" },
            new TestRecord { ["LNAME"] = "Gone", ["INV_NO"] = "499", ["INV_DATE"] = "20260901", ["TOTAL"] = "3.00", ["BALANCE"] = "3.00", Deleted = true },
        ]);

        TestDbfWriter.Write(Path.Combine(Folder, "USERS.DBF"),
            [C("USERNAM", 15), C("P_WORD", 10 + 10), N("PRV", 1), L("PWCHANGE")],
        [
            new TestRecord { ["USERNAM"] = "MANAGER", ["P_WORD"] = Password1, ["PRV"] = "4", ["PWCHANGE"] = "F" },
            new TestRecord { ["USERNAM"] = "CLERK", ["P_WORD"] = Password2, ["PRV"] = "1", ["PWCHANGE"] = "F" },
        ]);

        TestDbfWriter.Write(Path.Combine(Folder, "RMA.DBF"),
            [C("PROD", 30), C("PLU", 16), N("QTY", 10, 2), C("CUST_RMANO", 10), L("ISCUST_RMA"), L("IS_ACTIVE"), M("NOTES")],
            [new TestRecord { ["PROD"] = "WIDGET", ["PLU"] = "A1", ["QTY"] = "1.00", ["CUST_RMANO"] = "12", ["ISCUST_RMA"] = "T", ["IS_ACTIVE"] = "T" }]);

        TestDbfWriter.Write(Path.Combine(Folder, "FASTKEYS.DBF"),
            [C("KEYNUM", 3), C("KEYNAME", 30), C("PLU", 16), C("UNITPRICE1", 12), C("CREDIT", 5), C("TAX1", 5), C("TAX2", 5), C("CATKEY", 5), C("COLOR", 15)],
        [
            new TestRecord { ["KEYNUM"] = "1", ["KEYNAME"] = "COFFEE", ["PLU"] = "A1", ["UNITPRICE1"] = "1.50", ["CREDIT"] = "FALSE", ["TAX1"] = "TRUE ", ["TAX2"] = "FALSE", ["CATKEY"] = "FALSE" },
            new TestRecord { ["KEYNUM"] = "2", ["KEYNAME"] = "2", ["UNITPRICE1"] = "0.00", ["CREDIT"] = "FALSE", ["TAX1"] = "FALSE", ["TAX2"] = "FALSE", ["CATKEY"] = "FALSE" },
            new TestRecord { ["KEYNUM"] = "27", ["KEYNAME"] = "SUB KEY", ["UNITPRICE1"] = "2.00", ["CREDIT"] = "FALSE", ["TAX1"] = "FALSE", ["TAX2"] = "FALSE", ["CATKEY"] = "FALSE" },
        ]);

        File.WriteAllText(Path.Combine(Folder, "HEADER.TXT"), "TEST STORE\r\n1 Example Street\r\n\x1a");
    }

    private static void WriteLocationTables(string folder)
    {
        string weekly = string.Join(',', Enumerable.Range(1, 54).Select(w => w == 3 ? "2.00" : "0.00"));
        TestDbfWriter.Write(Path.Combine(folder, "TSTINV.DBF"), InventoryFields,
        [
            new TestRecord
            {
                ["PROD"] = "WIDGET", ["PLU"] = "A1", ["DEPT"] = "TOOLS", ["ITEMTYPE"] = "STANDARD", ["ONHAND"] = "5.00", ["UNITPRICE1"] = "9.99",
                ["SUPPLIER"] = "ACME SUPPLY", ["JAN_VOLUME"] = "3.00", ["JAN_GROSS"] = "29.97", ["TX1"] = "T",
                ["LASTYR"] = "1.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,4.00", ["WEEKLY"] = weekly,
                ["PICPATH"] = @"C:\Retail Plus 30\PICS\widget.jpg", ["SUPLIST"] = "|ACME SUPPLY,AC-1,2.500,12.00|OTHER SOURCE,,2.75,1.00|",
            },
            new TestRecord
            {
                ["PROD"] = "SHIRT", ["PLU"] = "A2", ["DEPT"] = "APPAREL", ["ITEMTYPE"] = "MATRIX", ["CATEGORY"] = "TOPS", ["ONHAND"] = "7.00",
                ["SUPPLIER"] = "NEW SUPPLIER", ["MATRIX"] = "Colour,Size,,|Red,S,-,3.00|Blue,M,-,4.00",
            },
            new TestRecord { ["PROD"] = "WIDGET COPY", ["PLU"] = "A1", ["ITEMTYPE"] = "STANDARD", ["ONHAND"] = "2.00" },
            new TestRecord { ["PROD"] = "WIDGET KIT", ["PLU"] = "K1", ["ITEMTYPE"] = "KIT", ["KIT"] = "|A1,WIDGET,2.00|ZZ9,MISSING PART,1.00|" },
            new TestRecord { ["PROD"] = "OLD ITEM", ["PLU"] = "D1", ["ITEMTYPE"] = "STANDARD", ["ONHAND"] = "100.00", Deleted = true },
            new TestRecord { ["PROD"] = "GADGET", ["PLU"] = "S1", ["ITEMTYPE"] = "SERIALIZED", ["ONHAND"] = "1.00", ["SERNO"] = "|SN-1,IN STOCK,02/28/2012|" },
            new TestRecord { ["PROD"] = "ODD TYPE", ["PLU"] = "X1", ["ITEMTYPE"] = "WIDGETRY" },
        ]);

        TestDbfWriter.Write(Path.Combine(folder, "BACKORD.DBF"),
            [C("NAME", 30), D("ORDATE"), C("CUSTNO", 11), C("PROD", 37), C("PLU", 16), N("UNITPRICE", 8, 2), N("QTY", 8, 2), L("PAID")],
            [new TestRecord { ["NAME"] = "Ann Example", ["ORDATE"] = "20261010", ["CUSTNO"] = "21", ["PROD"] = "WIDGET", ["PLU"] = "A1", ["UNITPRICE"] = "9.99", ["QTY"] = "2.00", ["PAID"] = "F" }]);

        TestDbfWriter.Write(Path.Combine(folder, "SALES001.DBF"),
            [D("SALEDATE"), C("SALESTIME", 5), C("INVNO", 15), C("PLU", 16), C("PROD", 37), N("QTY", 10, 2), N("PRICE", 10, 2), N("TOTAL", 12, 2), C("MOP", 16), N("TAX1", 12, 2)],
        [
            new TestRecord { ["SALEDATE"] = "20261020", ["SALESTIME"] = "10:15", ["INVNO"] = "501", ["PLU"] = "A1", ["PROD"] = "WIDGET", ["QTY"] = "1.00", ["PRICE"] = "9.99", ["TOTAL"] = "9.99", ["MOP"] = "Cash", ["TAX1"] = "0.50" },
            new TestRecord { ["SALEDATE"] = "20261020", ["SALESTIME"] = "10:15", ["INVNO"] = "501", ["PLU"] = "A2", ["PROD"] = "SHIRT", ["QTY"] = "1.00", ["PRICE"] = "20.00", ["TOTAL"] = "20.00", ["MOP"] = "Cash", ["TAX1"] = "1.00" },
            new TestRecord { ["SALEDATE"] = "20261020", ["SALESTIME"] = "11:00", ["INVNO"] = "502", ["PLU"] = "A1", ["PROD"] = "WIDGET", ["QTY"] = "-1.00", ["PRICE"] = "9.99", ["TOTAL"] = "-9.99", ["MOP"] = "Visa" },
        ]);

        TestDbfWriter.Write(Path.Combine(folder, "TOTAL001.DBF"),
            [C("LOC", 3), D("SALES_DATE"), C("SALES_TIME", 5), N("R_CASH", 10, 2), N("R_VISA", 10, 2), N("R_TOTAL", 12, 2), N("TAX1_GROSS", 10, 2), N("CUSTCOUNT", 10)],
            [new TestRecord { ["LOC"] = "TST", ["SALES_DATE"] = "20261020", ["SALES_TIME"] = "18:00", ["R_CASH"] = "30.99", ["R_VISA"] = "-9.99", ["R_TOTAL"] = "21.00", ["TAX1_GROSS"] = "1.50", ["CUSTCOUNT"] = "2" }]);

        TestDbfWriter.Write(Path.Combine(folder, "PO", "PO8.TST"),
            [N("ORDQTY", 10, 2), C("PLU", 16), C("PROD", 30), N("PKG", 4), N("LASTCOST", 11, 3), N("RECVD", 10, 2), C("SUPPLIER", 30), C("ORDERSENT", 60)],
            [new TestRecord { ["ORDQTY"] = "2.00", ["PLU"] = "A1", ["PROD"] = "WIDGET", ["PKG"] = "12", ["LASTCOST"] = "2.500", ["RECVD"] = "0.00", ["SUPPLIER"] = "ACME SUPPLY", ["ORDERSENT"] = "EMAIL" }]);

        File.WriteAllText(Path.Combine(folder, "RETADDR.ADD"), "TEST STORE\r\nReturns Dept\r\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
