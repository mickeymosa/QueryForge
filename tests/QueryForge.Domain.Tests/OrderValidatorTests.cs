using QueryForge.Domain;
using Xunit;

namespace QueryForge.Domain.Tests;

public class OrderValidatorTests
{
    [Fact]
    public void PaidPositiveEuroOrder_IsApproved()
    {
        var order = new OrderRecord("A1", "Ahmed", 150.50m, "EUR", true);

        var result = OrderValidator.Validate(order);

        Assert.Equal(OrderValidationStatus.Approved, result.Status);
    }

    [Fact]
    public void NegativeAmount_IsInvalid()
    {
        var order = new OrderRecord("A2", "Ahmed", -50m, "EUR", true);

        var result = OrderValidator.Validate(order);

        Assert.Equal(OrderValidationStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void ZeroAmount_IsInvalid()
    {
        var order = new OrderRecord("A3", "Ahmed", 0m, "EUR", false);

        var result = OrderValidator.Validate(order);

        Assert.Equal(OrderValidationStatus.InvalidAmount, result.Status);
    }

    [Fact]
    public void Gbp_IsUnsupported()
    {
        var order = new OrderRecord("A4", "Ahmed", 50m, "GBP", true);

        var result = OrderValidator.Validate(order);

        Assert.Equal(OrderValidationStatus.UnsupportedCurrency, result.Status);
    }

    [Fact]
    public void UnpaidPositiveOrder_IsPending()
    {
        var order = new OrderRecord("A5", "Ahmed", 50m, "USD", false);

        var result = OrderValidator.Validate(order);

        Assert.Equal(OrderValidationStatus.PaymentPending, result.Status);
    }

    [Fact]
    public void EmptyOrderId_IsInvalid()
    {
        var order = new OrderRecord(" ", "Ahmed", 50m, "USD", true);

        var result = OrderValidator.Validate(order);

        Assert.Equal(OrderValidationStatus.MissingOrderId, result.Status);
    }

    [Fact]
    public void DuplicateIds_AreDetected()
    {
        OrderRecord[] orders =
        [
            new("A1", "Ahmed", 10m, "EUR", true),
            new("A1", "Sara", 20m, "EUR", true)
        ];

        var results = OrderValidator.ValidateAll(orders);

        Assert.Equal(OrderValidationStatus.Approved, results[0].Status);
        Assert.Equal(OrderValidationStatus.DuplicateOrderId, results[1].Status);
    }

    [Fact]
    public void EmptyList_ProducesNoResults()
    {
        var results = OrderValidator.ValidateAll(Array.Empty<OrderRecord>());

        Assert.Empty(results);
    }
}
