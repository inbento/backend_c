using FluentMigrator;
using System;

[Migration(5)]
public class AddStatusToOrderTable : Migration
{
    public override void Up()
    {
        // Make ALTER idempotent so migration is safe to run multiple times
        Execute.Sql(@"ALTER TABLE orders ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'created';");

        Execute.Sql(@"DROP TYPE IF EXISTS v1_order;");

        // Use DO block with conditional CREATE to be compatible with Postgres versions
        // that don't support CREATE TYPE IF NOT EXISTS
        Execute.Sql(@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'v1_order') THEN
                    CREATE TYPE v1_order AS (
                        id BIGINT,
                        customer_id BIGINT,
                        delivery_address TEXT,
                        total_price_cents BIGINT,
                        total_price_currency TEXT,
                        status TEXT,
                        created_at TIMESTAMPTZ,
                        updated_at TIMESTAMPTZ
                    );
                END IF;
            END
            $$;
        ");
    }

    public override void Down()
    {
        Execute.Sql(@"ALTER TABLE orders DROP COLUMN IF EXISTS status;");
        Execute.Sql(@"DROP TYPE IF EXISTS v1_order;");
    }
}   