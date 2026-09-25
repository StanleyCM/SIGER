using Microsoft.EntityFrameworkCore;
using SIGER.Domain.Entities;

namespace SIGER.Tests.Infrastructure;

// Contract captured by read-only information_schema/pg_catalog inspection on 2026-09-24.
// These tests never connect to Supabase or modify its schema.
public class ExistingSchemaContractTests
{
    [Theory]
    [InlineData(typeof(Payment), nameof(Payment.UserId), typeof(User), "fk_pago_usuario", "id_usuario")]
    [InlineData(typeof(Reservation), nameof(Reservation.TableId), typeof(Table), "fk_reserva_mesa", "id_mesa")]
    public void Mandatory_historical_relationships_restrict_parent_deletion(
        Type dependentType, string foreignKeyProperty, Type principalType, string constraintName, string principalColumn)
    {
        using var f = new ModelFixture();
        var entity = f.Context.Model.FindEntityType(dependentType)!;
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key =>
            key.Properties.Count == 1 && key.Properties[0].Name == foreignKeyProperty);
        Assert.Equal(principalType, foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(principalColumn, Assert.Single(foreignKey.PrincipalKey.Properties).GetColumnName());
        Assert.Equal(constraintName, foreignKey.GetConstraintName());
        Assert.True(foreignKey.IsRequired);
        Assert.False(Assert.Single(foreignKey.Properties).IsNullable);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        var table = f.Context.Model.GetRelationalModel().Tables.Single(table => table.Name == entity.GetTableName());
        var constraint = Assert.Single(table.ForeignKeyConstraints, key => key.Name == constraintName);
        Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, constraint.OnDeleteAction);
        Assert.Equal(System.Data.ConnectionState.Closed, f.Context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(typeof(User), nameof(User.AuthUserId), "usuario", "auth_user_id", "uuid")]
    [InlineData(typeof(Payment), nameof(Payment.UserId), "pago", "id_usuario", "bigint")]
    [InlineData(typeof(Reservation), nameof(Reservation.TableId), "reserva", "id_mesa", "bigint")]
    [InlineData(typeof(Promotion), nameof(Promotion.DiscountPercentage), "promocion", "porcentaje_descuento", "numeric(5,2)")]
    public void Mandatory_domain_fields_match_NOT_NULL_relational_contract(
        Type entityType, string propertyName, string table, string column, string storeType)
    {
        using var f = new ModelFixture();
        var entity = f.Context.Model.FindEntityType(entityType)!;
        var property = entity.FindProperty(propertyName)!;
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, entity.GetSchema());
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal(column, property.GetColumnName(store));
        Assert.Equal(storeType, property.GetColumnType());
        Assert.Null(Nullable.GetUnderlyingType(property.ClrType));
        Assert.False(property.IsNullable);
        Assert.False(property.IsColumnNullable(store));
        foreach (var foreignKey in property.GetContainingForeignKeys())
            Assert.True(foreignKey.IsRequired);
        if (entityType == typeof(User))
            Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.SequenceEqual(new[] { property }));
        if (entityType == typeof(Promotion))
        {
            Assert.Equal(5, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
        }
        Assert.Equal(System.Data.ConnectionState.Closed, f.Context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(typeof(Role), "IsActive", "estado", "boolean")]
    [InlineData(typeof(User), "IsActive", "estado", "boolean")]
    [InlineData(typeof(User), "Email", "email", "character varying(150)")]
    [InlineData(typeof(OrderDetail), "Note", "notas", "character varying(300)")]
    [InlineData(typeof(Reservation), "Notes", "observaciones", "character varying(500)")]
    [InlineData(typeof(Promotion), "IsActive", "estado", "boolean")]
    [InlineData(typeof(Audit), "EntityId", "entidad_id", "bigint")]
    [InlineData(typeof(Audit), "IpAddress", "direccion_ip", "inet")]
    [InlineData(typeof(Role), "Name", "nombre", "character varying(50)")]
    [InlineData(typeof(Role), "Description", "descripcion", "character varying(250)")]
    [InlineData(typeof(Category), "Description", "descripcion", "character varying(250)")]
    [InlineData(typeof(Product), "Description", "descripcion", "character varying(500)")]
    [InlineData(typeof(Product), "ImageUrl", "imagen_url", "text")]
    [InlineData(typeof(Product), "Price", "precio", "numeric(12,2)")]
    [InlineData(typeof(OrderDetail), "UnitPrice", "precio_unitario", "numeric(12,2)")]
    public void Mapped_column_matches_existing_database(Type entityType, string propertyName, string column, string storeType)
    {
        using var f = new ModelFixture();
        var property = f.Context.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;
        Assert.Equal(column, property.GetColumnName());
        Assert.Equal(storeType, property.GetColumnType());
    }

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
