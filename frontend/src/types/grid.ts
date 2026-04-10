export type GridCellType = 'Empty' | 'Obstacle' | 'Road' | 'Entrance' | 'Label';

export type SlotStatus = 'Free' | 'Booked' | 'Blocked' | 'Own' | 'Inactive';

export interface GridConfiguration {
  gridRows: number | null;
  gridColumns: number | null;
  slots: GridSlot[];
  cells: GridCell[];
}

export interface GridSlot {
  id: string;
  slotNumber: string;
  label: string | null;
  isActive: boolean;
  row: number | null;
  column: number | null;
}

export interface GridCell {
  id: string;
  row: number;
  column: number;
  cellType: GridCellType;
  label: string | null;
}

export interface GridAvailability {
  gridRows: number;
  gridColumns: number;
  slots: GridAvailabilitySlot[];
  cells: GridCell[];
}

export interface GridAvailabilitySlot {
  id: string;
  slotNumber: string;
  label: string | null;
  row: number;
  column: number;
  status: SlotStatus;
}

export interface SaveGridConfigurationRequest {
  gridRows: number;
  gridColumns: number;
  slotPositions: { slotId: string; row: number; column: number }[];
  cells: { row: number; column: number; cellType: GridCellType; label?: string }[];
}
