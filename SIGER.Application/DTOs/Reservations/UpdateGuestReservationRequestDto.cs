using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SIGER.Application.Services;

namespace SIGER.Application.DTOs.Reservations;

// Omitted properties are preserved; explicit null clears only optional fields.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateGuestReservationRequestDto : IValidatableObject
{
    private string? name, phone, email, notes;
    private DateTimeOffset? date;
    private int? people;
    [StringLength(150)] public string? Name { get => name; set { name = value; HasName = true; } }
    [RegularExpression(GuestReservationSchedule.PhonePattern)] public string? Phone { get => phone; set { phone = value; HasPhone = true; } }
    [RegularExpression(GuestReservationSchedule.EmailPattern), StringLength(150)] public string? Email { get => email; set { email = value; HasEmail = true; } }
    public DateTimeOffset? ReservationDateTime { get => date; set { date = value; HasDate = true; } }
    [Range(1, GuestReservationSchedule.MaxPeople)] public int? NumberOfPeople { get => people; set { people = value; HasPeople = true; } }
    [StringLength(500)] public string? Notes { get => notes; set { notes = value; HasNotes = true; } }
    [JsonIgnore] public bool HasName { get; private set; }
    [JsonIgnore] public bool HasPhone { get; private set; }
    [JsonIgnore] public bool HasEmail { get; private set; }
    [JsonIgnore] public bool HasDate { get; private set; }
    [JsonIgnore] public bool HasPeople { get; private set; }
    [JsonIgnore] public bool HasNotes { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HasName && string.IsNullOrWhiteSpace(Name)) yield return new("Name is required.", [nameof(Name)]);
        if (HasPhone && string.IsNullOrWhiteSpace(Phone)) yield return new("Phone is required.", [nameof(Phone)]);
        if (HasDate && ReservationDateTime is null) yield return new("Date is required.", [nameof(ReservationDateTime)]);
        if (HasPeople && NumberOfPeople is null) yield return new("Party size is required.", [nameof(NumberOfPeople)]);
    }
}
