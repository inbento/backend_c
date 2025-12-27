using FluentMigrator;
using System;

[Migration(3)]
public class CreateCompositeTypes : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'v1_order_item_dal') THEN
                    CREATE TYPE v1_order_item_dal AS (
                        id BIGINT,
                        order_id BIGINT,
                        product_id BIGINT,
                        quantity INT,
                        product_title TEXT,
                        product_url TEXT,
                        price_cents BIGINT,
                        price_currency TEXT,
                        created_at TIMESTAMPTZ,
                        updated_at TIMESTAMPTZ
                    );
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'v1_order_dal') THEN
                    CREATE TYPE v1_order_dal AS (
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
            $$");
    }

    public override void Down()
    {
        Execute.Sql(@"
            DROP TYPE IF EXISTS v1_order_dal;
            DROP TYPE IF EXISTS v1_order_item_dal;
            ");
    }
}