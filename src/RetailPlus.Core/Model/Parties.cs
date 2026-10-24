namespace RetailPlus.Core.Model;

/// <summary>
/// Contact fields shared by clients and suppliers. In the original, CLIENTS.DBF and SUPPLIER.DBF have exactly the
/// same structure; here the commerce fields only exist on <see cref="Client"/>.
/// </summary>
public abstract class Party : ISoftDeletable, IFlaggable, IVersioned
{
    public int Id { get; set; }
    /// <summary>[FNAME]</summary>
    public string? FirstName { get; set; }
    /// <summary>[LNAME]</summary>
    public string? LastName { get; set; }
    public string? Title { get; set; }
    public string? Company { get; set; }
    /// <summary>[ADDR1]</summary>
    public string? Address1 { get; set; }
    /// <summary>[ADDR2]</summary>
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? WorkPhone { get; set; }
    /// <summary>[EXT]</summary>
    public string? Extension { get; set; }
    public string? Fax { get; set; }
    /// <summary>[HOMEPHONE]; shown as the mobile/home phone.</summary>
    public string? HomePhone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    /// <summary>Customer or supplier number [CUSTNO].</summary>
    public string? Number { get; set; }
    /// <summary>[ACCTNO]</summary>
    public string? AccountNumber { get; set; }
    /// <summary>[LMAIL]</summary>
    public DateOnly? LastMailed { get; set; }
    /// <summary>[LPURCHASE]</summary>
    public DateOnly? LastPurchase { get; set; }
    /// <summary>[CLIENTTYPE]</summary>
    public string? Type { get; set; }
    /// <summary>Ship-to address [SHIPTO memo].</summary>
    public string? ShipTo { get; set; }
    /// <summary>[NOTES memo]. For clients this holds the original's purchase history text.</summary>
    public string? Notes { get; set; }
    /// <summary>[PICPATH]</summary>
    public string? PhotoPath { get; set; }

    /// <summary>[PR]; assumed to be the browse flag, since these tables have no PRTFLAG. To confirm.</summary>
    public bool IsFlagged { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A customer (one record of CLIENTS.DBF).</summary>
public class Client : Party
{
    public DateOnly? Birthday { get; set; }
    /// <summary>Bonus points [POINTS].</summary>
    public decimal Points { get; set; }
    /// <summary>Usual discount in percent [DISCOUNT].</summary>
    public decimal UsualDiscountPercent { get; set; }
    /// <summary>[EXEMPTTAX1]</summary>
    public bool ExemptTax1 { get; set; }
    /// <summary>[EXEMPTTAX2]</summary>
    public bool ExemptTax2 { get; set; }
    /// <summary>Price level the client normally pays, 0 = regular [PRICELEVEL].</summary>
    public int PriceLevel { get; set; }
    /// <summary>Zero means no limit, as in the original [CREDIT].</summary>
    public decimal CreditLimit { get; set; }
    public decimal Balance { get; set; }

    // Recurring charge (one per client)
    /// <summary>Stock code of the item or service billed [BILLITEM].</summary>
    public string? RecurringItemCode { get; set; }
    /// <summary>[BILLEVERY]</summary>
    public int RecurringEvery { get; set; }
    /// <summary>Days, Weeks or Months [DMY].</summary>
    public string? RecurringUnit { get; set; }
    /// <summary>[NEXTBILL]</summary>
    public DateOnly? RecurringNextBilling { get; set; }
    /// <summary>[LBILLED]</summary>
    public DateOnly? RecurringLastBilled { get; set; }
    /// <summary>[BILLQTY]</summary>
    public int RecurringMaxBillings { get; set; }
    /// <summary>[BILLSSENT]</summary>
    public int RecurringTimesBilled { get; set; }
    /// <summary>[NOWACTIVE]</summary>
    public bool RecurringActive { get; set; }
}

/// <summary>A supplier (one record of SUPPLIER.DBF).</summary>
public class Supplier : Party
{
}
