namespace Retail25.Domain.Catalog;

/// <summary>
/// The ten product types from the legacy system (user guide p.30–31). Each type drives different
/// behaviour in pricing, stock tracking and POS handling — resolved via the
/// <c>product_type_behaviour</c> configuration table, not a switch statement.
/// </summary>
public enum ProductType
{
    Standard = 0,
    Matrix = 1,
    Serialized = 2,
    Kit = 3,
    NonStock = 4,
    Rental = 5,
    Service = 6,
    Shipping = 7,
    Admission = 8,
    GiftCard = 9,
}
