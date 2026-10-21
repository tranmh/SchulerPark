using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Data-only: replaces the Goeppingen grid with the site plan — 15 rows × 3 columns, one
    /// driving lane in column 1 with P110..P097 on the left (top to bottom) and P135..P122 on the
    /// right, and the charging stations ("Ladesäulen") labelled above the lane. Slots keep their
    /// ids, so bookings and preferences are unaffected; Goeppingen slots outside 97-110 / 122-135
    /// are taken off the grid (an admin can place them again). Idempotent. Hand-written (no
    /// dotnet-ef on the build host); no schema change, so the model snapshot is unaffected.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261021110000_GoeppingenSiteLayout")]
    public partial class GoeppingenSiteLayout : Migration
    {
        /// <summary>Public so the Postgres integration test can run the exact statements the migration applies.</summary>
        public const string UpSql = """
            UPDATE "Locations" SET "GridRows" = 15, "GridColumns" = 3
            WHERE lower("Name") IN ('goeppingen', 'göppingen');

            DELETE FROM "GridCells" c
            USING "Locations" l
            WHERE c."LocationId" = l."Id" AND lower(l."Name") IN ('goeppingen', 'göppingen');

            -- Clear first: the (LocationId, GridRow, GridColumn) index is unique and checked per row.
            UPDATE "ParkingSlots" ps
            SET "GridRow" = NULL, "GridColumn" = NULL
            FROM "Locations" l
            WHERE ps."LocationId" = l."Id" AND lower(l."Name") IN ('goeppingen', 'göppingen');

            -- Row r holds P(111-r) on the left and P(136-r) on the right.
            WITH target AS (
                SELECT no, 111 - no AS grid_row, 0 AS grid_col FROM generate_series(97, 110) AS no
                UNION ALL
                SELECT no, 136 - no, 2 FROM generate_series(122, 135) AS no
            )
            UPDATE "ParkingSlots" ps
            SET "GridRow" = t.grid_row, "GridColumn" = t.grid_col
            FROM target t, "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND lower(l."Name") IN ('goeppingen', 'göppingen')
              AND ps."SlotNumber" = 'P' || lpad(t.no::text, 3, '0');

            INSERT INTO "GridCells" ("Id", "LocationId", "Row", "Column", "CellType", "Label")
            SELECT gen_random_uuid(), l."Id", 0, 1, 'Label', 'Ladesäulen'
            FROM "Locations" l
            WHERE lower(l."Name") IN ('goeppingen', 'göppingen');

            INSERT INTO "GridCells" ("Id", "LocationId", "Row", "Column", "CellType", "Label")
            SELECT gen_random_uuid(), l."Id", r, 1, 'Road', NULL
            FROM "Locations" l CROSS JOIN generate_series(1, 14) AS r
            WHERE lower(l."Name") IN ('goeppingen', 'göppingen');
            """;

        /// <summary>Restores the previous seeded 8 × 12 Goeppingen layout.</summary>
        public const string DownSql = """
            UPDATE "Locations" SET "GridRows" = 8, "GridColumns" = 12
            WHERE lower("Name") IN ('goeppingen', 'göppingen');

            DELETE FROM "GridCells" c
            USING "Locations" l
            WHERE c."LocationId" = l."Id" AND lower(l."Name") IN ('goeppingen', 'göppingen');

            UPDATE "ParkingSlots" ps
            SET "GridRow" = NULL, "GridColumn" = NULL
            FROM "Locations" l
            WHERE ps."LocationId" = l."Id" AND lower(l."Name") IN ('goeppingen', 'göppingen');

            WITH target(no, grid_row, grid_col) AS (VALUES
                ( 97, 1,  1), ( 98, 1,  2), ( 99, 1,  4), (100, 1,  5), (101, 1,  7), (102, 1,  8), (103, 1, 10),
                (104, 2,  1), (105, 2,  2), (106, 2,  4), (107, 2,  5), (108, 2,  7), (109, 2,  8), (110, 2, 10),
                (122, 4,  1), (123, 4,  2), (124, 4,  4), (125, 4,  5), (126, 4,  7), (127, 4,  8), (128, 4, 10),
                (129, 5,  1), (130, 5,  2), (131, 5,  4), (132, 5,  5), (133, 5,  7), (134, 5,  8), (135, 5, 10))
            UPDATE "ParkingSlots" ps
            SET "GridRow" = t.grid_row, "GridColumn" = t.grid_col
            FROM target t, "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND lower(l."Name") IN ('goeppingen', 'göppingen')
              AND ps."SlotNumber" = 'P' || lpad(t.no::text, 3, '0');

            INSERT INTO "GridCells" ("Id", "LocationId", "Row", "Column", "CellType", "Label")
            SELECT gen_random_uuid(), l."Id", cell.r, cell.c, cell.t, cell.lbl
            FROM "Locations" l
            CROSS JOIN (
                SELECT 0 AS r, c, 'Obstacle' AS t, NULL AS lbl FROM generate_series(0, 11) AS c
                UNION ALL SELECT r, c, 'Road', NULL FROM generate_series(1, 5) AS r, unnest(ARRAY[3, 6, 9]) AS c
                UNION ALL SELECT 6, c, 'Road', NULL FROM generate_series(3, 6) AS c
                UNION ALL SELECT 7, 5, 'Entrance', NULL
                UNION ALL SELECT 3, 1, 'Label', 'A'
                UNION ALL SELECT 3, 5, 'Label', 'B'
                UNION ALL SELECT 3, 8, 'Label', 'C'
            ) cell
            WHERE lower(l."Name") IN ('goeppingen', 'göppingen');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownSql);
        }
    }
}
