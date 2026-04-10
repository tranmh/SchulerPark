namespace SchulerPark.Api.DTOs.Grid;

public record GridConfigurationDto(
    int? GridRows,
    int? GridColumns,
    List<GridSlotDto> Slots,
    List<GridCellDto> Cells);

public record GridSlotDto(
    Guid Id, string SlotNumber, string? Label, bool IsActive,
    int? Row, int? Column);

public record GridCellDto(
    Guid Id, int Row, int Column, string CellType, string? Label);

public record SaveGridConfigurationRequest(
    int GridRows,
    int GridColumns,
    List<SaveGridSlotPosition> SlotPositions,
    List<SaveGridCellRequest> Cells);

public record SaveGridSlotPosition(Guid SlotId, int Row, int Column);

public record SaveGridCellRequest(int Row, int Column, string CellType, string? Label);
