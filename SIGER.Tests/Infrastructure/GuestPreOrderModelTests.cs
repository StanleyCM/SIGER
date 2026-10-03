using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SIGER.Domain.Entities;

namespace SIGER.Tests.Infrastructure;

public class GuestPreOrderModelTests
{
    [Fact]
    public void Guest_relationships_and_unique_indexes_match_approved_schema()
    {
        using var f = new ModelFixture(); var model = f.Context.GetService<IDesignTimeModel>().Model;
        var r = model.FindEntityType(typeof(Reservation))!; var o = model.FindEntityType(typeof(Order))!;
        Assert.True(r.FindProperty("UserId")!.IsNullable); Assert.False(r.FindProperty("TableId")!.IsNullable);
        Assert.True(o.FindProperty("UserId")!.IsNullable); Assert.True(o.FindProperty("ReservationId")!.IsNullable);
        var fk = Assert.Single(o.GetForeignKeys(), k => k.PrincipalEntityType == r);
        Assert.True(fk.IsUnique); Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        var index = Assert.Single(o.GetIndexes(), i => i.Properties.SingleOrDefault()?.Name == "ReservationId");
        Assert.True(index.IsUnique); Assert.Equal("id_reserva IS NOT NULL", index.GetFilter());
        Assert.Equal("bytea", r.FindProperty("AccessTokenHash")!.GetColumnType());
        Assert.Equal("timestamp with time zone", r.FindProperty("AccessTokenExpiresAt")!.GetColumnType());
        Assert.Equal(150, r.FindProperty("ContactName")!.GetMaxLength()); Assert.Equal(30, r.FindProperty("ContactPhone")!.GetMaxLength());
        Assert.Equal(150, r.FindProperty("ContactEmail")!.GetMaxLength());
        Assert.Equal(2, r.GetCheckConstraints().Count()); Assert.Equal(2, o.GetCheckConstraints().Count());
    }
}
