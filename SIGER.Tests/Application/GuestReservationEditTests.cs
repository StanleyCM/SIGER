using System.Text.Json;
using Moq;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Exceptions;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Application;

public class GuestReservationEditTests
{
    [Fact]
    public async Task Contact_and_notes_patch_preserves_omitted_fields_identity_and_hash()
    {
        var g = new GuestFixture(); var hash = g.Reservation.AccessTokenHash!.ToArray();
        var date = g.Reservation.ReservationDateTime;
        var result = await g.Guests.UpdateAsync(10, g.Credential, new()
            { Name = " Nueva Ana ", Phone = "8095550123", Email = "ana@empresa.com", Notes = " Ventana " });
        Assert.True(result.IsSuccess); Assert.Equal("Nueva Ana", result.Value!.Name);
        Assert.Equal("8095550123", result.Value.Phone); Assert.Equal("ana@empresa.com", result.Value.Email);
        Assert.Equal("Ventana", result.Value.Notes); Assert.Equal(date, result.Value.ReservationDateTime);
        Assert.Equal(hash, g.Reservation.AccessTokenHash); Assert.Equal(4, g.Reservation.TableId);
        Assert.Equal(ReservationStatus.Pending, result.Value.Status);
        var second = await g.Guests.UpdateAsync(10, g.Credential, new() { Email = null, Notes = null });
        Assert.Null(second.Value!.Email); Assert.Null(second.Value.Notes); Assert.Equal("Nueva Ana", second.Value.Name);
        Assert.DoesNotContain("token", JsonSerializer.Serialize(second.Value), StringComparison.OrdinalIgnoreCase);
        g.F.Tables.Verify(x => x.GetReservationCandidateIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("date")] [InlineData("time")] [InlineData("people")]
    public async Task Schedule_changes_recheck_capacity_status_two_hour_overlap_excluding_self(string field)
    {
        var g = new GuestFixture(); var calls = new List<long>();
        var request = new UpdateGuestReservationRequestDto();
        if (field == "date") request.ReservationDateTime = g.Reservation.ReservationDateTime.AddDays(1);
        if (field == "time") request.ReservationDateTime = g.Reservation.ReservationDateTime.AddMinutes(30);
        if (field == "people") request.NumberOfPeople = 4;
        g.F.Reservations.Setup(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, DateTimeOffset start, DateTimeOffset end, long? excluded, CancellationToken _) =>
            { Assert.True(g.F.InTransaction); Assert.Equal(10, excluded); Assert.Equal(TimeSpan.FromHours(2), end - start); calls.Add(id); return false; });
        Assert.True((await g.Guests.UpdateAsync(10, g.Credential, request)).IsSuccess);
        Assert.Single(calls); Assert.Equal(g.Reservation.ReservationDateTime.AddHours(2), g.Reservation.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Reassignment_updates_only_preorder_table_and_keeps_nonoperational_state()
    {
        var g = new GuestFixture(); await g.Preorders.CreateAsync(10, g.Credential, g.Items);
        var hash = g.Reservation.AccessTokenHash!.ToArray(); var order = g.SavedOrder!;
        var details = JsonSerializer.Serialize(order.Details.Select(x => new { x.ProductId, x.Quantity, x.Note, x.UnitPrice, x.Subtotal }));
        g.F.Tables.Setup(x => x.GetReservationCandidateIdsAsync(6, It.IsAny<CancellationToken>())).ReturnsAsync([9L, 4L, 8L]);
        g.F.Tables.Setup(x => x.GetByIdForUpdateAsync(8, It.IsAny<CancellationToken>())).ReturnsAsync(new Table { Id = 8, Capacity = 12, Status = TableStatus.OutOfService });
        g.F.Tables.Setup(x => x.GetByIdForUpdateAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(new Table { Id = 9, Capacity = 6 });
        Assert.True((await g.Guests.UpdateAsync(10, g.Credential, new() { NumberOfPeople = 6 })).IsSuccess);
        Assert.Equal(9, g.Reservation.TableId); Assert.Equal(9, order.TableId); Assert.Equal(10, order.ReservationId);
        Assert.Equal(OrderStatus.PreOrdered, order.Status); Assert.Equal(OrderOrigin.Web, order.Origin); Assert.Equal(25m, order.Total);
        Assert.Equal(details, JsonSerializer.Serialize(order.Details.Select(x => new { x.ProductId, x.Quantity, x.Note, x.UnitPrice, x.Subtotal })));
        Assert.Equal(hash, g.Reservation.AccessTokenHash); Assert.Equal(TableStatus.Available, g.F.Table.Status);
        g.F.Payments.VerifyNoOtherCalls(); g.F.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
    }

    [Theory]
    [InlineData("none")] [InlineData("overlap")] [InlineData("capacity")] [InlineData("out")]
    public async Task Unavailable_edit_does_not_mutate_any_reservation_fields(string reason)
    {
        var g = new GuestFixture(); var before = JsonSerializer.Serialize(g.Reservation);
        if (reason == "none") g.F.Tables.Setup(x => x.GetReservationCandidateIdsAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        if (reason == "capacity") g.F.Table.Capacity = 2;
        if (reason == "out") g.F.Table.Status = TableStatus.OutOfService;
        if (reason == "overlap") g.F.Reservations.Setup(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), 10, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await g.Guests.UpdateAsync(10, g.Credential, new() { Name = "Changed", NumberOfPeople = 3 });
        Assert.Equal("No table is available for this reservation.", result.Error);
        Assert.Equal(before, JsonSerializer.Serialize(g.Reservation)); g.F.NoSave();
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)] [InlineData(ReservationStatus.Cancelled)] [InlineData(ReservationStatus.Completed)]
    public async Task Only_pending_can_be_edited(ReservationStatus status)
    {
        var g = new GuestFixture(); g.Reservation.Status = status;
        Assert.Equal("Only future pending reservations can be edited.", (await g.Guests.UpdateAsync(10, g.Credential, new() { Notes = "test" })).Error);
        g.F.NoSave();
    }

    [Theory]
    [InlineData("old")] [InlineData("new")]
    public async Task Neither_original_nor_requested_date_may_be_in_the_past(string kind)
    {
        var g = new GuestFixture(); var patch = new UpdateGuestReservationRequestDto();
        if (kind == "old") g.Reservation.ReservationDateTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        else patch.ReservationDateTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.True((await g.Guests.UpdateAsync(10, g.Credential, patch)).IsFailure); g.F.NoSave();
    }

    [Theory]
    [InlineData("missing")] [InlineData("invalid")] [InlineData("other")] [InlineData("expired")] [InlineData("expired-under-lock")]
    public async Task Own_valid_credential_is_required_before_and_after_lock(string kind)
    {
        var g = new GuestFixture();
        if (kind == "expired") g.Reservation.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        if (kind == "expired-under-lock") g.F.Reservations.Setup(x => x.GetByIdForUpdateAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { g.Reservation.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); return g.Reservation; });
        if (kind == "other") g.F.Reservations.Setup(x => x.GetByIdAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync(new Reservation
            { AccessTokenHash = g.Tokens.Hash(g.Tokens.Generate()), AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await Assert.ThrowsAsync<UnauthorizedException>(() => g.Guests.UpdateAsync(kind == "other" ? 11 : 10,
            kind == "missing" ? null : kind == "invalid" ? g.Tokens.Generate() : g.Credential, new() { Notes = "test" }));
        g.F.NoSave();
    }

    [Theory]
    [InlineData("name")] [InlineData("phone")] [InlineData("email")] [InlineData("people")] [InlineData("notes")] [InlineData("date")]
    public async Task Invalid_patch_never_saves(string field)
    {
        var g = new GuestFixture(); var patch = new UpdateGuestReservationRequestDto();
        switch (field)
        {
            case "name": patch.Name = null; break;
            case "phone": patch.Phone = "letters"; break;
            case "email": patch.Email = "invalid"; break;
            case "people": patch.NumberOfPeople = 0; break;
            case "notes": patch.Notes = new string('a', 501); break;
            case "date": patch.ReservationDateTime = null; break;
        }
        Assert.True((await g.Guests.UpdateAsync(10, g.Credential, patch)).IsFailure); g.F.NoSave();
    }
}
