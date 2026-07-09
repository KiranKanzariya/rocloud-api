START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619103050_AddAmcSubscriptions') THEN
    CREATE TABLE amc_subscriptions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        customer_id uuid NOT NULL,
        plan_name character varying(100),
        interval_months integer NOT NULL,
        amount numeric(10,2) NOT NULL,
        start_date date NOT NULL,
        end_date date,
        last_service_date date,
        next_due_date date NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        is_deleted boolean NOT NULL,
        CONSTRAINT "PK_amc_subscriptions" PRIMARY KEY (id),
        CONSTRAINT ck_amc_subscriptions_interval CHECK (interval_months IN (3, 6, 12)),
        CONSTRAINT "FK_amc_subscriptions_customers_customer_id" FOREIGN KEY (customer_id) REFERENCES customers (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619103050_AddAmcSubscriptions') THEN
    CREATE INDEX "IX_amc_subscriptions_customer_id" ON amc_subscriptions (customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619103050_AddAmcSubscriptions') THEN
    CREATE INDEX "IX_amc_subscriptions_tenant_id_customer_id" ON amc_subscriptions (tenant_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619103050_AddAmcSubscriptions') THEN
    CREATE INDEX "IX_amc_subscriptions_tenant_id_next_due_date" ON amc_subscriptions (tenant_id, next_due_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260619103050_AddAmcSubscriptions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260619103050_AddAmcSubscriptions', '10.0.4');
    END IF;
END $EF$;
COMMIT;

