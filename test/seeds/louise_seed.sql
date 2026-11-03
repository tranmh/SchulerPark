-- Seed data for the shell script tests (test/*.bats).
--
-- A small, synthetic slice of the LouisE / SchulerPark schema (users with
-- first name, surname and company; locations; parking slots; bookings).
-- Used two ways:
--   * as the output of the fake pg_dump (test_helper.bash: fake_pg_dump), and
--   * against the run's PostgreSQL ($DATABASE_URL) by integration tests:
--       psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f test/seeds/louise_seed.sql
-- It lives in its own schema and is idempotent, so it never collides with the
-- application's tables in the shared test database. No real personal data.

CREATE SCHEMA IF NOT EXISTS shell_fixture;
SET search_path TO shell_fixture;

DROP TABLE IF EXISTS "Bookings";
DROP TABLE IF EXISTS "ParkingSlots";
DROP TABLE IF EXISTS "Locations";
DROP TABLE IF EXISTS "Users";

CREATE TABLE "Users" (
    "Id"           uuid PRIMARY KEY,
    "Email"        text NOT NULL UNIQUE,
    "FirstName"    text NOT NULL,
    "Surname"      text NOT NULL,
    "Company"      text NOT NULL,
    "Role"         text NOT NULL,
    "PasswordHash" text
);

CREATE TABLE "Locations" (
    "Id"   uuid PRIMARY KEY,
    "Name" text NOT NULL
);

CREATE TABLE "ParkingSlots" (
    "Id"         uuid PRIMARY KEY,
    "LocationId" uuid NOT NULL REFERENCES "Locations" ("Id"),
    "SlotNumber" text NOT NULL
);

CREATE TABLE "Bookings" (
    "Id"            uuid PRIMARY KEY,
    "UserId"        uuid NOT NULL REFERENCES "Users" ("Id"),
    "LocationId"    uuid NOT NULL REFERENCES "Locations" ("Id"),
    "ParkingSlotId" uuid REFERENCES "ParkingSlots" ("Id"),
    "Date"          date NOT NULL,
    "Status"        text NOT NULL,
    "LicensePlate"  text
);

INSERT INTO "Users" ("Id", "Email", "FirstName", "Surname", "Company", "Role", "PasswordHash") VALUES
    ('00000000-0000-0000-0000-000000000001', 'admin@schulerpark.local', 'Ada',   'Admin',  'Schuler', 'Admin', 'fixture-hash-admin'),
    ('00000000-0000-0000-0000-000000000002', 'erika.muster@example.test', 'Erika', 'Muster', 'Schuler', 'User',  'fixture-hash-erika'),
    ('00000000-0000-0000-0000-000000000003', 'ext.partner@example.test',  'Paul',  'Partner', 'Partner GmbH', 'User', NULL);

INSERT INTO "Locations" ("Id", "Name") VALUES
    ('10000000-0000-0000-0000-000000000001', 'Goeppingen'),
    ('10000000-0000-0000-0000-000000000002', 'Erfurt');

INSERT INTO "ParkingSlots" ("Id", "LocationId", "SlotNumber") VALUES
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'P1'),
    ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'P2'),
    ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000002', 'E1');

INSERT INTO "Bookings" ("Id", "UserId", "LocationId", "ParkingSlotId", "Date", "Status", "LicensePlate") VALUES
    ('30000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '2026-11-04', 'Confirmed', 'GP-EM 123'),
    ('30000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000002', NULL,                                   '2026-11-05', 'Pending',   'EF-PP 42');

RESET search_path;
