START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619111941_AddUserAreas') THEN
    CREATE TABLE user_areas (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        area_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_user_areas" PRIMARY KEY (id),
        CONSTRAINT "FK_user_areas_areas_area_id" FOREIGN KEY (area_id) REFERENCES areas (id) ON DELETE CASCADE,
        CONSTRAINT "FK_user_areas_users_user_id" FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619111941_AddUserAreas') THEN
    CREATE INDEX "IX_user_areas_area_id" ON user_areas (area_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619111941_AddUserAreas') THEN
    CREATE INDEX "IX_user_areas_tenant_id_area_id" ON user_areas (tenant_id, area_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619111941_AddUserAreas') THEN
    CREATE UNIQUE INDEX "IX_user_areas_user_id_area_id" ON user_areas (user_id, area_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619111941_AddUserAreas') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260619111941_AddUserAreas', '10.0.4');
    END IF;
END $EF$;
COMMIT;

