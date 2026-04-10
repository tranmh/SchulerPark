namespace SchulerPark.Api.DTOs.Grid;

public record GridAvailabilityDto(
    int GridRows,
    int GridColumns,
    List<GridAvailabilitySlotDto> Slots,
    List<GridCellDto> Cells);

public record GridAvailabilitySlotDto(
    Guid Id, string SlotNumber, string? Label,
    int Row, int Column, string Status);
