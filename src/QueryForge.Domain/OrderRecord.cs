namespace QueryForge.Domain;

// A record represents the data of one imported order.
// We keep it immutable to avoid accidental changes during validation.
public sealed record OrderRecord(
    string OrderId,
    string CustomerName,
    decimal Amount,
    string Currency,
    bool IsPaid);
