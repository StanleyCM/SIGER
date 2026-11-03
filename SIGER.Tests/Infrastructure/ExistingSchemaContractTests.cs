using Microsoft.EntityFrameworkCore;
using SIGER.Domain.Entities;

namespace SIGER.Tests.Infrastructure;

// Contract captured by read-only information_schema/pg_catalog inspection on 2026-09-24.
// These tests never connect to Supabase or modify its schema.
public class ExistingSchemaContractTests
{

    [Fact]
    public void Payment_order_is_unique_but_user_email_is_not_a_database_unique_constraint()
    {
        using var f = new ModelFixture();
        var payment = f.Context.Model.FindEntityType(typeof(Payment))!;
        Assert.Contains(payment.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(Payment.OrderId));
        var user = f.Context.Model.FindEntityType(typeof(User))!;
        Assert.DoesNotContain(user.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(User.Email));
    }
}
