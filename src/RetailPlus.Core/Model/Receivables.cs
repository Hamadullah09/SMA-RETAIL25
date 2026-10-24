namespace RetailPlus.Core.Model;

public enum InvoiceKind
{
    Invoice = 1,
    Quote = 2,
    Layaway = 3,
}

/// <summary>An on-account invoice, price quote or layaway (INVOICES.DBF).</summary>
public class Invoice : IFlaggable, IVersioned
{
    public int Id { get; set; }
    public long InvoiceNumber { get; set; }
    public InvoiceKind Kind { get; set; } = InvoiceKind.Invoice;
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    public string? AccountNumber { get; set; }
    public string? CustomerNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public decimal Total { get; set; }
    public decimal Balance { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? SalesPerson { get; set; }
    public DateOnly? LastPaidDate { get; set; }
    public decimal LastPaidAmount { get; set; }
    /// <summary>Late charges added so far (included in Balance).</summary>
    public decimal Penalty { get; set; }
    /// <summary>Tax rates in effect at the time of the sale; reprints use these.</summary>
    public decimal Tax1Rate { get; set; }
    public decimal Tax2Rate { get; set; }
    public string? ShipTo { get; set; }
    public string? BillTo { get; set; }
    /// <summary>For imported invoices: the original NOTES memo, which holds the line detail.</summary>
    public string? Notes { get; set; }
    public string? Email { get; set; }
    public int? SaleId { get; set; }
    public bool IsVoided { get; set; }
    public bool IsFlagged { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<InvoiceLine> Lines { get; set; } = [];
    public List<InvoicePayment> Payments { get; set; } = [];

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Company) ? Company! : $"{FirstName} {LastName}".Trim();
}

public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string? Plu { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class InvoicePayment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public DateOnly PaidOn { get; set; }
    public decimal Amount { get; set; }
    public string TenderType { get; set; } = "";
    /// <summary>Part of the payment that went to the late charge first, as the manual describes.</summary>
    public decimal AppliedToPenalty { get; set; }
    public string? Note { get; set; }
}
