using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Data-only: aligns slots that were created by the old seed (P001..Pnnn per location) with the
    /// physical numbering on site, which the seed now uses for fresh databases:
    /// <list type="bullet">
    ///   <item>Goeppingen: P001..P014 become P097..P110 and P015..P020 become P122..P127 (grid
    ///   positions unchanged, slot ids unchanged, so bookings and preferences follow the slot).
    ///   Missing slots of the 97-110 / 122-135 range are added; P128 and P129..P135 get their
    ///   grid position when the location has a grid and the cell is free.</item>
    ///   <item>Erfurt keeps P001..P010, Hessdorf P001..P006, Netphen P001..P004. Higher seeded
    ///   numbers are deleted when no booking ever referenced them (their per-slot blocked days go
    ///   too — the FK would otherwise turn them into location-wide blocks), deactivated when only
    ///   past bookings reference them, and left untouched when a Won/Confirmed booking for today
    ///   or later still sits on them (deactivate those through the admin UI so the lifecycle
    ///   service reassigns the drivers).</item>
    /// </list>
    /// Only slot numbers of the seeded form <c>Pnnn</c> are touched; operator-created slots and
    /// locations with other names are ignored. Idempotent. Hand-written (no dotnet-ef on the
    /// build host); no schema change, so the model snapshot is unaffected.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261021100000_RenumberSeededParkingSlots")]
    public partial class RenumberSeededParkingSlots : Migration
    {
        /// <summary>Public so the Postgres integration test can run the exact statements the migration applies.</summary>
        public const string UpSql = """
            -- 1. Goeppingen: rename the 20 old seed slots in place (ids stay, bookings follow).
            WITH map AS (
                SELECT i AS old_no, CASE WHEN i <= 14 THEN i + 96 ELSE i + 107 END AS new_no
                FROM generate_series(1, 20) AS i
            )
            UPDATE "ParkingSlots" ps
            SET "SlotNumber" = 'P' || lpad(map.new_no::text, 3, '0'),
                "Label" = CASE WHEN ps."Label" = 'Parking Spot ' || map.old_no::text
                               THEN 'Parking Spot ' || map.new_no::text
                               ELSE ps."Label" END
            FROM map, "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND lower(l."Name") IN ('goeppingen', 'göppingen')
              AND ps."SlotNumber" = 'P' || lpad(map.old_no::text, 3, '0')
              AND NOT EXISTS (
                  SELECT 1 FROM "ParkingSlots" x
                  WHERE x."LocationId" = ps."LocationId"
                    AND x."SlotNumber" = 'P' || lpad(map.new_no::text, 3, '0'));

            -- 2. Goeppingen: add whatever is still missing from 97-110 / 122-135, placing it on the
            --    grid only when the location has one and nothing occupies the cell.
            WITH target(no, grid_row, grid_col) AS (VALUES
                ( 97, 1,  1), ( 98, 1,  2), ( 99, 1,  4), (100, 1,  5), (101, 1,  7), (102, 1,  8), (103, 1, 10),
                (104, 2,  1), (105, 2,  2), (106, 2,  4), (107, 2,  5), (108, 2,  7), (109, 2,  8), (110, 2, 10),
                (122, 4,  1), (123, 4,  2), (124, 4,  4), (125, 4,  5), (126, 4,  7), (127, 4,  8), (128, 4, 10),
                (129, 5,  1), (130, 5,  2), (131, 5,  4), (132, 5,  5), (133, 5,  7), (134, 5,  8), (135, 5, 10))
            INSERT INTO "ParkingSlots" ("Id", "LocationId", "SlotNumber", "Label", "IsActive", "GridRow", "GridColumn")
            SELECT gen_random_uuid(), l."Id", 'P' || lpad(t.no::text, 3, '0'), 'Parking Spot ' || t.no::text, true,
                   CASE WHEN placeable THEN t.grid_row END,
                   CASE WHEN placeable THEN t.grid_col END
            FROM target t
            CROSS JOIN "Locations" l
            CROSS JOIN LATERAL (
                SELECT l."GridRows" IS NOT NULL AND l."GridColumns" IS NOT NULL
                   AND t.grid_row < l."GridRows" AND t.grid_col < l."GridColumns"
                   AND NOT EXISTS (SELECT 1 FROM "ParkingSlots" s
                                   WHERE s."LocationId" = l."Id" AND s."GridRow" = t.grid_row AND s."GridColumn" = t.grid_col)
                   AND NOT EXISTS (SELECT 1 FROM "GridCells" c
                                   WHERE c."LocationId" = l."Id" AND c."Row" = t.grid_row AND c."Column" = t.grid_col)
                   AS placeable) p
            WHERE lower(l."Name") IN ('goeppingen', 'göppingen')
              AND NOT EXISTS (SELECT 1 FROM "ParkingSlots" s
                              WHERE s."LocationId" = l."Id" AND s."SlotNumber" = 'P' || lpad(t.no::text, 3, '0'));

            -- 3. Erfurt / Hessdorf / Netphen: seeded slots above the physical count ("surplus").
            -- 3a. Never referenced by a booking: delete (per-slot blocked days first).
            DELETE FROM "BlockedDays" bd
            USING "ParkingSlots" ps, "Locations" l
            WHERE bd."ParkingSlotId" = ps."Id"
              AND ps."LocationId" = l."Id"
              AND ps."SlotNumber" ~ '^P[0-9][0-9][0-9]$'
              AND substring(ps."SlotNumber" from 2)::int >
                  CASE lower(l."Name") WHEN 'erfurt' THEN 10 WHEN 'hessdorf' THEN 6 WHEN 'netphen' THEN 4 END
              AND NOT EXISTS (SELECT 1 FROM "Bookings" b WHERE b."ParkingSlotId" = ps."Id");

            DELETE FROM "ParkingSlots" ps
            USING "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND ps."SlotNumber" ~ '^P[0-9][0-9][0-9]$'
              AND substring(ps."SlotNumber" from 2)::int >
                  CASE lower(l."Name") WHEN 'erfurt' THEN 10 WHEN 'hessdorf' THEN 6 WHEN 'netphen' THEN 4 END
              AND NOT EXISTS (SELECT 1 FROM "Bookings" b WHERE b."ParkingSlotId" = ps."Id");

            -- 3b. Only historical bookings left: deactivate and take it off the grid.
            UPDATE "ParkingSlots" ps
            SET "IsActive" = false, "GridRow" = NULL, "GridColumn" = NULL
            FROM "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND ps."SlotNumber" ~ '^P[0-9][0-9][0-9]$'
              AND substring(ps."SlotNumber" from 2)::int >
                  CASE lower(l."Name") WHEN 'erfurt' THEN 10 WHEN 'hessdorf' THEN 6 WHEN 'netphen' THEN 4 END
              AND NOT EXISTS (
                  SELECT 1 FROM "Bookings" b
                  WHERE b."ParkingSlotId" = ps."Id"
                    AND b."Status" IN ('Won', 'Confirmed')
                    AND b."Date" >= (now() AT TIME ZONE 'Europe/Berlin')::date);

            -- Nobody should keep an inactive surplus slot as their preference.
            UPDATE "Users" u
            SET "PreferredSlotId" = NULL
            FROM "ParkingSlots" ps, "Locations" l
            WHERE u."PreferredSlotId" = ps."Id"
              AND ps."IsActive" = false
              AND ps."LocationId" = l."Id"
              AND ps."SlotNumber" ~ '^P[0-9][0-9][0-9]$'
              AND substring(ps."SlotNumber" from 2)::int >
                  CASE lower(l."Name") WHEN 'erfurt' THEN 10 WHEN 'hessdorf' THEN 6 WHEN 'netphen' THEN 4 END;
            """;

        /// <summary>Reverse renaming only; deleted surplus slots are not recreated.</summary>
        public const string DownSql = """
            -- Remove the Goeppingen slots this migration added, if nothing ever used them.
            DELETE FROM "ParkingSlots" ps
            USING "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND lower(l."Name") IN ('goeppingen', 'göppingen')
              AND ps."SlotNumber" IN ('P128', 'P129', 'P130', 'P131', 'P132', 'P133', 'P134', 'P135')
              AND NOT EXISTS (SELECT 1 FROM "Bookings" b WHERE b."ParkingSlotId" = ps."Id")
              AND NOT EXISTS (SELECT 1 FROM "BlockedDays" bd WHERE bd."ParkingSlotId" = ps."Id");

            WITH map AS (
                SELECT i AS old_no, CASE WHEN i <= 14 THEN i + 96 ELSE i + 107 END AS new_no
                FROM generate_series(1, 20) AS i
            )
            UPDATE "ParkingSlots" ps
            SET "SlotNumber" = 'P' || lpad(map.old_no::text, 3, '0'),
                "Label" = CASE WHEN ps."Label" = 'Parking Spot ' || map.new_no::text
                               THEN 'Parking Spot ' || map.old_no::text
                               ELSE ps."Label" END
            FROM map, "Locations" l
            WHERE ps."LocationId" = l."Id"
              AND lower(l."Name") IN ('goeppingen', 'göppingen')
              AND ps."SlotNumber" = 'P' || lpad(map.new_no::text, 3, '0')
              AND NOT EXISTS (
                  SELECT 1 FROM "ParkingSlots" x
                  WHERE x."LocationId" = ps."LocationId"
                    AND x."SlotNumber" = 'P' || lpad(map.old_no::text, 3, '0'));
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
