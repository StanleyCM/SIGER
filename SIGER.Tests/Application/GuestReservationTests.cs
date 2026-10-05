using System.Text.Json;
using Moq;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Exceptions;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Security;

namespace SIGER.Tests.Application;

internal sealed class GuestFixture
{
    public ServiceFixture F { get; } = new();
    public GuestReservationTokenService Tokens { get; } = new();
    public string Credential { get; }
    public Reservation Reservation { get; }
    public Order? SavedOrder { get; private set; }
    public Reservation? SavedReservation { get; private set; }
    public GuestReservationService Guests => new(F.Reservations.Object, F.Tables.Object, F.Orders.Object, Tokens, F.Work.Object, TimeProvider.System);
    public PreOrderService Preorders => new(F.Reservations.Object, F.Orders.Object, F.Products.Object, Tokens, F.Work.Object, TimeProvider.System);
    public CreateGuestReservationRequestDto Request => new() { Name = " Ana ", Phone = "+58 412 1234567", NumberOfPeople = 2, ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1) };
    public CreatePreOrderRequestDto Items => new() { Items = [new() { ProductId = 3, Quantity = 2, Notes = " Sin sal " }] };

    public GuestFixture()
    {
        Credential = Tokens.Generate();
        Reservation = new() { Id = 10, TableId = 4, Status = ReservationStatus.Pending, NumberOfPeople = 2,
            ContactName = "Ana", ContactPhone = "123", ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1),
            AccessTokenHash = Tokens.Hash(Credential), AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2) };
        F.Transaction<Result<GuestReservationCreatedDto>>(); F.Transaction<Result<PreOrderDto>>(); F.Transaction<Result<GuestReservationDto>>();
        F.Tables.Setup(x => x.GetReservationCandidateIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([4L]);
        F.Reservations.Setup(x => x.AddAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()))
            .Callback((Reservation r, CancellationToken _) => { r.Id = 10; SavedReservation = r; }).Returns(Task.CompletedTask);
        F.Reservations.Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(Reservation);
        F.Orders.Setup(x => x.GetByReservationIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(() => SavedOrder);
        F.Orders.Setup(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback((Order o, CancellationToken _) => { o.Id = 20; SavedOrder = o; }).Returns(Task.CompletedTask);
        F.Products.Setup(x => x.GetWithCategoriesByIdsAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<long> ids, CancellationToken _) => (IReadOnlyCollection<Product>)new[] { F.Product }.Where(p => ids.Contains(p.Id)).ToArray());
    }
}

public class GuestReservationTests
{
    [Fact]
    public async Task Valid_guest_is_pending_free_and_receives_credential_only_at_creation()
    {
        var f = new GuestFixture(); var response = await f.Guests.CreateAsync(f.Request);
        Assert.True(response.IsSuccess); var created = response.Value!; var r = f.SavedReservation!;
        Assert.Null(r.UserId); Assert.Null(r.User); Assert.Null(r.ContactEmail); Assert.Equal("Ana", r.ContactName);
        Assert.Equal(ReservationStatus.Pending, r.Status); Assert.Equal(4, r.TableId);
        Assert.Equal("Reserva recibida: pendiente", created.Message);
        Assert.Equal(f.Tokens.Hash(created.AccessToken), r.AccessTokenHash);
        Assert.Equal(r.ReservationDateTime.AddHours(2), r.AccessTokenExpiresAt);
        var json = JsonSerializer.Serialize(created);
        Assert.DoesNotContain("Hash", json); Assert.DoesNotContain("TableId", json); Assert.DoesNotContain("UserId", json);
        f.F.Users.VerifyNoOtherCalls(); f.F.Provider.VerifyNoOtherCalls(); f.F.Payments.VerifyNoOtherCalls();
        f.F.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
    }

    [Theory]
    [InlineData("name")] [InlineData("phone")] [InlineData("email")] [InlineData("past")]
    [InlineData("zero")] [InlineData("negative")] [InlineData("many")] [InlineData("notes")]
    [InlineData("longname")] [InlineData("longphone")] [InlineData("maxdate")]
    public async Task Invalid_guest_input_never_saves(string field)
    {
        var f = new GuestFixture(); var r = f.Request;
        switch (field)
        {
            case "name": r.Name = "  "; break;
            case "phone": r.Phone = ""; break;
            case "email": r.Email = "invalid"; break;
            case "past": r.ReservationDateTime = DateTimeOffset.UtcNow.AddMinutes(-1); break;
            case "zero": r.NumberOfPeople = 0; break;
            case "negative": r.NumberOfPeople = -1; break;
            case "many": r.NumberOfPeople = 101; break;
            case "notes": r.Notes = new string('n', 501); break;
            case "longname": r.Name = new string('n', 151); break;
            case "longphone": r.Phone = new string('1', 31); break;
            case "maxdate": r.ReservationDateTime = DateTimeOffset.MaxValue; break;
        }
        Assert.True((await f.Guests.CreateAsync(r)).IsFailure); f.F.NoSave();
    }

    [Theory]
    [InlineData("capacity")] [InlineData("out")] [InlineData("overlap")] [InlineData("none")]
    public async Task No_eligible_table_returns_conflict_without_changes(string reason)
    {
        var f = new GuestFixture();
        if (reason == "capacity") f.F.Table.Capacity = 1;
        if (reason == "out") f.F.Table.Status = TableStatus.OutOfService;
        if (reason == "none") f.F.Tables.Setup(x => x.GetReservationCandidateIdsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        if (reason == "overlap") f.F.Reservations.Setup(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.Equal("No table is available for this reservation.", (await f.Guests.CreateAsync(f.Request)).Error);
        f.F.NoSave();
    }

    [Fact]
    public async Task Assignment_locks_then_rechecks_and_skips_overlap_in_same_transaction()
    {
        var f = new GuestFixture(); var calls = new List<string>();
        f.F.Tables.Setup(x => x.GetReservationCandidateIdsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([8L, 4L]);
        f.F.Tables.Setup(x => x.GetByIdForUpdateAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => { Assert.True(f.F.InTransaction); calls.Add("lock" + id); return new Table { Id = id, Capacity = 4, Status = TableStatus.Occupied }; });
        f.F.Reservations.Setup(x => x.HasOverlapAsync(It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, DateTimeOffset start, DateTimeOffset end, long? _, CancellationToken _) =>
            { Assert.True(f.F.InTransaction); Assert.Equal(TimeSpan.FromHours(2), end - start); calls.Add("check" + id); return id == 4; });
        Assert.True((await f.Guests.CreateAsync(f.Request)).IsSuccess);
        Assert.Equal(new[] { "lock4", "check4", "lock8", "check8" }, calls); Assert.Equal(8, f.SavedReservation!.TableId);
    }

    [Theory]
    [InlineData("missing")] [InlineData("invalid")] [InlineData("expired")] [InlineData("other")]
    public async Task Reservation_access_requires_own_unexpired_credential(string kind)
    {
        var f = new GuestFixture(); string? token = kind == "missing" ? null : kind == "invalid" ? f.Tokens.Generate() : f.Credential;
        if (kind == "expired") f.Reservation.AccessTokenExpiresAt = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Guests.GetAsync(kind == "other" ? 11 : 10, token));
    }

    [Fact]
    public async Task Own_reservation_query_returns_editable_contact_but_excludes_credentials()
    {
        var f = new GuestFixture(); var response = await f.Guests.GetAsync(10, f.Credential);
        var json = JsonSerializer.Serialize(response.Value);
        Assert.True(response.IsSuccess); Assert.DoesNotContain("Token", json); Assert.DoesNotContain("Contact", json);
        Assert.Equal(f.Reservation.ContactName, response.Value!.Name);
        Assert.Equal(f.Reservation.ContactPhone, response.Value.Phone);
    }

    [Fact]
    public async Task Internal_cancellation_cancels_preorder_without_releasing_table()
    {
        var f = new GuestFixture(); await f.Preorders.CreateAsync(10, f.Credential, f.Items);
        f.F.Table.Status = TableStatus.Occupied;
        Assert.True((await f.F.ReservationService.ChangeStatusAsync(10, new() { Status = ReservationStatus.Cancelled })).IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, f.SavedOrder!.Status); Assert.Equal(TableStatus.Occupied, f.F.Table.Status);
        f.F.Tables.Verify(x => x.Update(It.IsAny<Table>()), Times.Never);
        Assert.Same(f.SavedOrder, await f.F.Orders.Object.GetByReservationIdAsync(10));
    }

    [Fact]
    public async Task Internal_guest_reschedule_updates_preorder_table_and_credential_expiry()
    {
        var f = new GuestFixture(); await f.Preorders.CreateAsync(10, f.Credential, f.Items);
        f.F.Tables.Setup(x => x.GetByIdForUpdateAsync(8, It.IsAny<CancellationToken>())).ReturnsAsync(new Table { Id = 8, Capacity = 4 });
        var date = DateTimeOffset.UtcNow.AddDays(2);
        Assert.True((await f.F.ReservationService.UpdateAsync(10, new() { TableId = 8, NumberOfPeople = 2, ReservationDateTime = date })).IsSuccess);
        Assert.Equal(8, f.SavedOrder!.TableId); Assert.Equal(date.AddHours(2), f.Reservation.AccessTokenExpiresAt);
    }
}
