using QueryForge.Domain;

// Sample data is intentionally mixed: approved, invalid, unsupported,
// unpaid and duplicate. No external dataset or secrets are required.
OrderRecord[] orders =
[
    new("ORD-001", "Ahmed", 150.50m, "EUR", true),
    new("ORD-002", "Sara", 0m, "EUR", true),
    new("ORD-003", "Mona", 80m, "GBP", true),
    new("ORD-004", "Omar", 30m, "USD", false),
    new("ORD-001", "Ahmed", 99m, "EUR", true)
];

Console.WriteLine("QueryForge — Order Validation Demo");
Console.WriteLine("----------------------------------");

foreach (var result in OrderValidator.ValidateAll(orders))
{
    Console.WriteLine($"{result.OrderId}: {result.Message} ({result.Status})");
}
