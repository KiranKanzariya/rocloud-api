START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619075702_AddRefreshTokenExpiry') THEN
    ALTER TABLE users ADD refresh_token_expires_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619075702_AddRefreshTokenExpiry') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260619075702_AddRefreshTokenExpiry', '10.0.4');
    END IF;
END $EF$;
COMMIT;

