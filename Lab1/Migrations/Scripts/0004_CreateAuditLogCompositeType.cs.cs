using FluentMigrator;
using System;

[Migration(4)]
public class CreateAuditLogCompositeType : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'v1_audit_log_order_dal') THEN
                    CREATE TYPE v1_audit_log_order_dal AS (
                        id BIGINT,
                        order_id BIGINT,
                        order_item_id BIGINT,
                        customer_id BIGINT,
                        order_status TEXT,
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
            DROP TYPE IF EXISTS v1_audit_log_order_dal;
            ");
    }
}