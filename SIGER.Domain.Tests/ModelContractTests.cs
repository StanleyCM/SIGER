using System.Collections;
using System.Reflection;
using SIGER.Domain.Base;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Domain.Exceptions;

namespace SIGER.Domain.Tests;

public class ModelContractTests
{
    public static TheoryData<Type> Entities => new(
        typeof(Role), typeof(User), typeof(Category), typeof(Product), typeof(Table), typeof(Order),
        typeof(OrderDetail), typeof(Payment), typeof(Reservation), typeof(Promotion), typeof(PromotionProduct), typeof(Audit));

    [Theory, MemberData(nameof(Entities))]
    public void Collections_are_initialized_and_not_shared(Type type)
    {
        var first = Activator.CreateInstance(type)!;
        var second = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties().Where(p => p.PropertyType.IsGenericType &&
                     p.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>)))
        {
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable>(property.GetValue(first)));
            Assert.NotSame(property.GetValue(first), property.GetValue(second));
        }
        if (type != typeof(PromotionProduct))
            Assert.True(typeof(BaseEntity).IsAssignableFrom(type));
    }

    [Theory]
    [InlineData(typeof(TableStatus), "Available,Occupied,Reserved,OutOfService")]
    [InlineData(typeof(OrderStatus), "Pending,InPreparation,Ready,Served,Paid,Cancelled")]
    [InlineData(typeof(PaymentMethod), "Cash,Card,Transfer,Other")]
    [InlineData(typeof(PaymentStatus), "Pending,Completed,Failed,Refunded")]
    [InlineData(typeof(OrderOrigin), "Desktop,Web")]
    [InlineData(typeof(OrderType), "Table,TakeAway")]
    [InlineData(typeof(ReservationStatus), "Pending,Confirmed,Cancelled,Completed")]
    public void Enum_contract_is_stable(Type type, string names)
    {
        Assert.Equal(names.Split(','), Enum.GetNames(type));
        Assert.Equal(Enumerable.Range(0, names.Split(',').Length),
            Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32));
    }

    [Fact]
    public void Composite_join_has_no_surrogate_identity()
    {
        Assert.False(typeof(BaseEntity).IsAssignableFrom(typeof(PromotionProduct)));
        Assert.Null(typeof(PromotionProduct).GetProperty("Id"));
        Assert.Equal(typeof(long), typeof(PromotionProduct).GetProperty("PromotionId")!.PropertyType);
        Assert.Equal(typeof(long), typeof(PromotionProduct).GetProperty("ProductId")!.PropertyType);
        Assert.True(typeof(BaseEntity).IsAbstract);
        Assert.Equal(["Id"], typeof(BaseEntity).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Nullable_relationships_and_audit_payloads_match_contract()
    {
        var order = new Order();
        Assert.Null(order.TableId); Assert.Null(order.Table);
        Assert.Null(order.ClientId); Assert.Null(order.Client);
        Assert.Empty(order.Details); Assert.Empty(order.Payments);
        var audit = new Audit();
        Assert.Null(audit.UserId); Assert.Null(audit.User);
        Assert.Null(audit.PreviousData); Assert.Null(audit.NewData); Assert.Null(audit.IpAddress);
        var nullability = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(Order).GetProperty("Client")!).ReadState);
        Assert.Equal(NullabilityState.NotNull, nullability.Create(typeof(Order).GetProperty("User")!).ReadState);
    }

    [Fact]
    public void Identity_security_and_concurrency_contracts_are_preserved()
    {
        Assert.DoesNotContain(typeof(User).GetProperties(), p => p.Name.Contains("Password"));
        Assert.Equal(typeof(Guid), typeof(User).GetProperty("AuthUserId")!.PropertyType);
        foreach (var type in new[] { typeof(Product), typeof(Table), typeof(Order) })
            Assert.Equal(typeof(long), type.GetProperty("Version")!.PropertyType);
        Assert.DoesNotContain(typeof(User).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("SIGER.") || a.Name.Contains("EntityFramework") || a.Name.Contains("Supabase"));
    }

    [Fact]
    public void Exceptions_preserve_message_and_cause()
    {
        var cause = new InvalidOperationException("cause");
        var exception = new BusinessRuleException("rule", cause);
        Assert.IsAssignableFrom<DomainException>(exception);
        Assert.Equal("rule", exception.Message);
        Assert.Same(cause, exception.InnerException);
        Assert.Equal("domain", new DomainException("domain").Message);
        Assert.Same(cause, new DomainException("domain", cause).InnerException);
        Assert.Equal("rule", new BusinessRuleException("rule").Message);
    }
}

