namespace QueryForge.Domain;

public enum OrderValidationStatus
{
    Approved,
    MissingOrderId,
    InvalidAmount,
    UnsupportedCurrency,
    PaymentPending,
    DuplicateOrderId
}

public sealed record OrderValidationResult(
    string OrderId,
    OrderValidationStatus Status,
    string Message);
