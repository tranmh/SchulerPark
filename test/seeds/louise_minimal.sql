-- Minimal LouisE dump used by the fake pg_dump (synthetic data only).
CREATE TABLE "Locations" ("Id" uuid PRIMARY KEY, "Name" text NOT NULL);
INSERT INTO "Locations" VALUES ('00000000-0000-0000-0000-000000000001', 'Göppingen');
CREATE TABLE "Users" ("Id" uuid PRIMARY KEY, "Email" text NOT NULL);
INSERT INTO "Users" VALUES ('00000000-0000-0000-0000-000000000002', 'user@example.test');
