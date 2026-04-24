namespace SchulerPark.Infrastructure.Services.Placement;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;

public class BfsDistanceMetric : ISlotDistanceMetric
{
    public int Distance(ParkingSlot from, ParkingSlot to, Location location, IReadOnlyList<GridCell> cells)
    {
        if (from.GridRow is null || from.GridColumn is null
            || to.GridRow is null || to.GridColumn is null
            || location.GridRows is null || location.GridColumns is null)
            return int.MaxValue;

        var rows = location.GridRows.Value;
        var cols = location.GridColumns.Value;
        var passable = new bool[rows, cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                passable[r, c] = true;

        foreach (var cell in cells)
        {
            if (cell.Row < 0 || cell.Row >= rows || cell.Column < 0 || cell.Column >= cols)
                continue;
            if (cell.CellType == GridCellType.Obstacle)
                passable[cell.Row, cell.Column] = false;
        }

        var sr = from.GridRow.Value;
        var sc = from.GridColumn.Value;
        var tr = to.GridRow.Value;
        var tc = to.GridColumn.Value;

        if (sr < 0 || sr >= rows || sc < 0 || sc >= cols
            || tr < 0 || tr >= rows || tc < 0 || tc >= cols)
            return int.MaxValue;

        if (sr == tr && sc == tc) return 0;

        var dist = new int[rows, cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                dist[r, c] = -1;

        var queue = new Queue<(int r, int c)>();
        queue.Enqueue((sr, sc));
        dist[sr, sc] = 0;

        int[] dr = [-1, 1, 0, 0];
        int[] dc = [0, 0, -1, 1];

        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            for (var i = 0; i < 4; i++)
            {
                var nr = r + dr[i];
                var nc = c + dc[i];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                if (dist[nr, nc] != -1) continue;
                if (!passable[nr, nc]) continue;
                dist[nr, nc] = dist[r, c] + 1;
                if (nr == tr && nc == tc) return dist[nr, nc];
                queue.Enqueue((nr, nc));
            }
        }

        return int.MaxValue;
    }
}
