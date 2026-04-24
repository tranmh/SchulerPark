namespace SchulerPark.Api.DTOs.Location;

public record ParkingSlotDto(Guid Id, string SlotNumber, string? Label, bool IsActive, int? GridRow, int? GridColumn);
