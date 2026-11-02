namespace QueryForge.Domain;

public static class OrderValidator
{
    // Lesson 1: ordered conditions and early return.
    // A record has one primary validation outcome in this introductory version.
    public static OrderValidationResult Validate(OrderRecord order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrWhiteSpace(order.OrderId))
        {
            return new(order.OrderId, OrderValidationStatus.MissingOrderId, "Missing order ID");
        }
        else if (order.Amount <= 0)
        {
            return new(order.OrderId, OrderValidationStatus.InvalidAmount, "Invalid order amount");
        }
        else if (order.Currency != "EUR" && order.Currency != "USD")
        {
            return new(order.OrderId, OrderValidationStatus.UnsupportedCurrency, "Unsupported currency");
        }
        else if (!order.IsPaid)
        {
            return new(order.OrderId, OrderValidationStatus.PaymentPending, "Payment pending");
        }
        else
        {
            return new(order.OrderId, OrderValidationStatus.Approved, "Order approved");
        }
    }

    // Lesson 2: loops, collections, HashSet and duplicate detection.
    public static IReadOnlyList<OrderValidationResult> ValidateAll(IEnumerable<OrderRecord> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var seenOrderIds = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<OrderValidationResult>();

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.OrderId) && !seenOrderIds.Add(order.OrderId))
            {
                results.Add(new(order.OrderId, OrderValidationStatus.DuplicateOrderId, "Duplicate order ID"));
                continue;
            }

            results.Add(Validate(order));
        }

        return results;
    }
}
