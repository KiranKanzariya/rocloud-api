-- ============================================================
-- ROCloud — mark the EF "InitialSchema" migration as already applied
--
-- The database schema was created in Phase 1 (schema.sql), so the
-- EF InitialSchema migration must NOT be run against the DB. Instead we
-- record it in EF's history table so EF treats the schema as up to date
-- and future `dotnet ef database update` runs only apply NEW migrations.
--
-- Run connected to the rocloud_dev database (any client; transaction-safe):
--   psql -U postgres -d rocloud_dev -f mark-initial-migration-applied.sql
-- ============================================================

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260619062510_InitialSchema', '10.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Verify:
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory";
