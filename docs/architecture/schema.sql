IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [migration_id] nvarchar(150) NOT NULL,
        [product_version] nvarchar(32) NOT NULL,
        CONSTRAINT [pk___ef_migrations_history] PRIMARY KEY ([migration_id])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [ar_ledger_entries] (
    [id] bigint NOT NULL IDENTITY,
    [customer_id] bigint NOT NULL,
    [invoice_id] bigint NOT NULL,
    [entry_type] int NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_ar_ledger_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [AspNetRoles] (
    [id] bigint NOT NULL IDENTITY,
    [legacy_level] int NULL,
    [description] nvarchar(max) NULL,
    [name] nvarchar(256) NULL,
    [normalized_name] nvarchar(256) NULL,
    [concurrency_stamp] nvarchar(max) NULL,
    CONSTRAINT [pk_asp_net_roles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [AspNetUsers] (
    [id] bigint NOT NULL IDENTITY,
    [display_name] nvarchar(max) NOT NULL,
    [is_enabled] bit NOT NULL,
    [default_location_id] bigint NULL,
    [last_signed_in_at] datetimeoffset NULL,
    [user_name] nvarchar(256) NULL,
    [normalized_user_name] nvarchar(256) NULL,
    [email] nvarchar(256) NULL,
    [normalized_email] nvarchar(256) NULL,
    [email_confirmed] bit NOT NULL,
    [password_hash] nvarchar(max) NULL,
    [security_stamp] nvarchar(max) NULL,
    [concurrency_stamp] nvarchar(max) NULL,
    [phone_number] nvarchar(max) NULL,
    [phone_number_confirmed] bit NOT NULL,
    [two_factor_enabled] bit NOT NULL,
    [lockout_end] datetimeoffset NULL,
    [lockout_enabled] bit NOT NULL,
    [access_failed_count] int NOT NULL,
    CONSTRAINT [pk_asp_net_users] PRIMARY KEY ([id])
);
GO

CREATE TABLE [audit_log_entries] (
    [id] bigint NOT NULL IDENTITY,
    [occurred_at] datetimeoffset NOT NULL,
    [action] nvarchar(24) NOT NULL,
    [actor_user_id] bigint NULL,
    [actor_staff_id] bigint NULL,
    [actor_name] nvarchar(200) NULL,
    [station_id] bigint NULL,
    [location_id] bigint NULL,
    [ip_address] nvarchar(64) NULL,
    [entity_type] nvarchar(80) NOT NULL,
    [entity_id] nvarchar(64) NULL,
    [operation] nvarchar(120) NULL,
    [before_json] nvarchar(max) NULL,
    [after_json] nvarchar(max) NULL,
    [correlation_id] nvarchar(128) NULL,
    [approver_staff_id] bigint NULL,
    [reason] nvarchar(500) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_audit_log_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [bonus_pricings] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [buy_qty] decimal(19,4) NOT NULL,
    [free_qty] decimal(19,4) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_bonus_pricings] PRIMARY KEY ([id])
);
GO

CREATE TABLE [business_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [business_name] nvarchar(200) NOT NULL,
    [address_line1] nvarchar(200) NULL,
    [address_line2] nvarchar(200) NULL,
    [address_city] nvarchar(100) NULL,
    [address_state_or_province] nvarchar(100) NULL,
    [address_postal_code] nvarchar(20) NULL,
    [address_country] nvarchar(100) NULL,
    [contact_phone] nvarchar(30) NULL,
    [contact_extension] nvarchar(10) NULL,
    [contact_mobile] nvarchar(30) NULL,
    [contact_fax] nvarchar(30) NULL,
    [contact_email] nvarchar(200) NULL,
    [contact_website] nvarchar(200) NULL,
    [licence_number] nvarchar(60) NULL,
    [tax_registration_number] nvarchar(40) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_business_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [cart_adjustments] (
    [id] bigint NOT NULL IDENTITY,
    [cart_id] bigint NOT NULL,
    [type] nvarchar(24) NOT NULL,
    [label] nvarchar(120) NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [percent] decimal(7,2) NOT NULL,
    [serial] nvarchar(64) NULL,
    [applied_by_staff_id] bigint NOT NULL,
    [applied_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_cart_adjustments] PRIMARY KEY ([id])
);
GO

CREATE TABLE [cart_lines] (
    [id] bigint NOT NULL IDENTITY,
    [cart_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [serialized_unit_id] bigint NULL,
    [epc] nvarchar(max) NULL,
    [source] nvarchar(20) NOT NULL,
    [quantity] decimal(18,4) NOT NULL,
    [manual_unit_price] decimal(19,4) NULL,
    [manual_discount_pct] decimal(19,4) NULL,
    [requested_price_level] int NULL,
    [tax1override] bit NULL,
    [tax2override] bit NULL,
    [embedded_price] decimal(19,4) NULL,
    [line_type] nvarchar(20) NOT NULL,
    [return_to_stock] bit NOT NULL,
    [note] nvarchar(max) NULL,
    [sequence] int NOT NULL,
    [unit_price] decimal(19,4) NOT NULL,
    [price_origin] nvarchar(20) NOT NULL,
    [line_discount_pct] decimal(7,2) NOT NULL,
    [tax1applies] bit NOT NULL,
    [tax2applies] bit NOT NULL,
    [extended_net] decimal(19,4) NOT NULL,
    [tax1amount] decimal(19,4) NOT NULL,
    [tax2amount] decimal(19,4) NOT NULL,
    [stock_code_snapshot] nvarchar(24) NULL,
    [name_snapshot] nvarchar(200) NULL,
    [unit_cost_snapshot] decimal(19,3) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_cart_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [cart_tax_overrides] (
    [id] bigint NOT NULL IDENTITY,
    [cart_id] bigint NOT NULL,
    [tax1] bit NULL,
    [tax2] bit NULL,
    [applies_from_sequence] int NOT NULL,
    [applied_by_staff_id] bigint NOT NULL,
    [applied_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_cart_tax_overrides] PRIMARY KEY ([id])
);
GO

CREATE TABLE [carts] (
    [id] bigint NOT NULL IDENTITY,
    [station_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [staff_id] bigint NOT NULL,
    [customer_id] bigint NULL,
    [status] nvarchar(20) NOT NULL,
    [held_name] nvarchar(100) NULL,
    [suspended_at] datetimeoffset NULL,
    [suspended_by_staff_id] bigint NULL,
    [next_line_sequence] int NOT NULL,
    [revision] int NOT NULL,
    [completed_transaction_id] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [expires_at] datetimeoffset NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_carts] PRIMARY KEY ([id])
);
GO

CREATE TABLE [categories] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [name] nvarchar(max) NOT NULL,
    [code] nvarchar(max) NULL,
    [sort_order] int NOT NULL,
    [is_active] bit NOT NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_categories] PRIMARY KEY ([id])
);
GO

CREATE TABLE [commission_ledger_entries] (
    [id] bigint NOT NULL IDENTITY,
    [staff_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [transaction_id] bigint NOT NULL,
    [sale_line_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [stock_code_snapshot] nvarchar(30) NOT NULL,
    [department_id] bigint NULL,
    [commission_rule_id] bigint NULL,
    [commission_type] nvarchar(20) NOT NULL,
    [rate_applied] decimal(9,4) NOT NULL,
    [line_net] decimal(19,2) NOT NULL,
    [line_cost] decimal(19,3) NOT NULL,
    [quantity] decimal(18,4) NOT NULL,
    [amount] decimal(19,2) NOT NULL,
    [was_capped] bit NOT NULL,
    [business_date] date NOT NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_commission_ledger_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [commission_rules] (
    [id] bigint NOT NULL IDENTITY,
    [staff_id] bigint NOT NULL,
    [product_id] bigint NULL,
    [department_id] bigint NULL,
    [commission_type] nvarchar(20) NOT NULL,
    [value] decimal(9,4) NOT NULL,
    [max_commission] decimal(19,2) NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_commission_rules] PRIMARY KEY ([id])
);
GO

CREATE TABLE [currencies] (
    [id] bigint NOT NULL IDENTITY,
    [code] nvarchar(max) NOT NULL,
    [name] nvarchar(max) NOT NULL,
    [symbol] nvarchar(max) NOT NULL,
    [scale] int NOT NULL,
    [rounding] int NOT NULL,
    [minimum_tender] decimal(19,4) NOT NULL,
    [is_base_currency] bit NOT NULL,
    [is_active] bit NOT NULL,
    [exchange_rate] decimal(19,4) NOT NULL,
    [exchange_rate_updated_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_currencies] PRIMARY KEY ([id])
);
GO

CREATE TABLE [customer_accounts] (
    [id] bigint NOT NULL IDENTITY,
    [customer_id] bigint NOT NULL,
    [account_number] bigint NOT NULL,
    [credit_limit] decimal(19,4) NOT NULL,
    [balance_due] decimal(19,4) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_customer_accounts] PRIMARY KEY ([id])
);
GO

CREATE TABLE [customer_order_lines] (
    [id] bigint NOT NULL IDENTITY,
    [customer_order_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [ordered_qty] decimal(19,4) NOT NULL,
    [filled_qty] decimal(19,4) NOT NULL,
    [unit_price] decimal(19,4) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_customer_order_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [customer_orders] (
    [id] bigint NOT NULL IDENTITY,
    [order_number] bigint NOT NULL,
    [customer_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [status] int NOT NULL,
    [ordered_on] date NOT NULL,
    [notes] nvarchar(max) NULL,
    [staff_id] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_customer_orders] PRIMARY KEY ([id])
);
GO

CREATE TABLE [customer_pricing_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [customer_id] bigint NOT NULL,
    [usual_discount_pct] decimal(19,4) NOT NULL,
    [price_level] int NOT NULL,
    [exempt_tax1] bit NOT NULL,
    [exempt_tax2] bit NOT NULL,
    [reward_points] int NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_customer_pricing_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [customers] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [customer_number] bigint NOT NULL,
    [first_name] nvarchar(100) NOT NULL,
    [last_name] nvarchar(100) NOT NULL,
    [company] nvarchar(200) NULL,
    [title] nvarchar(50) NULL,
    [billing_address_line1] nvarchar(200) NULL,
    [billing_address_line2] nvarchar(200) NULL,
    [billing_address_city] nvarchar(100) NULL,
    [billing_address_state_or_province] nvarchar(100) NULL,
    [billing_address_postal_code] nvarchar(20) NULL,
    [billing_address_country] nvarchar(100) NULL,
    [ship_to_address_line1] nvarchar(200) NULL,
    [ship_to_address_line2] nvarchar(200) NULL,
    [ship_to_address_city] nvarchar(100) NULL,
    [ship_to_address_state_or_province] nvarchar(100) NULL,
    [ship_to_address_postal_code] nvarchar(20) NULL,
    [ship_to_address_country] nvarchar(100) NULL,
    [contact_phone] nvarchar(30) NULL,
    [contact_extension] nvarchar(10) NULL,
    [contact_mobile] nvarchar(30) NULL,
    [contact_fax] nvarchar(30) NULL,
    [contact_email] nvarchar(200) NULL,
    [contact_website] nvarchar(200) NULL,
    [client_type] nvarchar(50) NULL,
    [birthday] date NULL,
    [notes] nvarchar(4000) NULL,
    [last_purchase_on] date NULL,
    [last_mailing_on] date NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_customers] PRIMARY KEY ([id])
);
GO

CREATE TABLE [data_protection_keys] (
    [id] int NOT NULL IDENTITY,
    [friendly_name] nvarchar(max) NULL,
    [xml] nvarchar(max) NULL,
    CONSTRAINT [pk_data_protection_keys] PRIMARY KEY ([id])
);
GO

CREATE TABLE [departments] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [name] nvarchar(100) NOT NULL,
    [code] nvarchar(20) NULL,
    [sort_order] int NOT NULL,
    [is_active] bit NOT NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_departments] PRIMARY KEY ([id])
);
GO

CREATE TABLE [drawer_ledger_entries] (
    [id] bigint NOT NULL IDENTITY,
    [drawer_session_id] bigint NOT NULL,
    [entry_type] nvarchar(20) NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [reason] nvarchar(200) NULL,
    [transaction_id] bigint NULL,
    [staff_id] bigint NOT NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_drawer_ledger_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [drawer_sessions] (
    [id] bigint NOT NULL IDENTITY,
    [station_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [opened_by_staff_id] bigint NOT NULL,
    [closed_by_staff_id] bigint NULL,
    [opening_float] decimal(19,4) NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [business_date] date NOT NULL,
    [opened_at] datetimeoffset NOT NULL,
    [closed_at] datetimeoffset NULL,
    [counted_cash] decimal(19,4) NULL,
    [expected_cash] decimal(19,4) NOT NULL,
    [variance] decimal(19,4) NOT NULL,
    [tender_totals_json] nvarchar(max) NULL,
    [department_net_sales_json] nvarchar(max) NULL,
    [net_sales] decimal(19,4) NOT NULL,
    [tax1collected] decimal(19,4) NOT NULL,
    [tax2collected] decimal(19,4) NOT NULL,
    [cost_of_goods_sold] decimal(19,4) NOT NULL,
    [transaction_count] int NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_drawer_sessions] PRIMARY KEY ([id])
);
GO

CREATE TABLE [external_entity_maps] (
    [id] bigint NOT NULL IDENTITY,
    [provider] nvarchar(max) NOT NULL,
    [entity_type] nvarchar(max) NOT NULL,
    [local_id] bigint NULL,
    [local_key] nvarchar(max) NULL,
    [remote_id] nvarchar(max) NOT NULL,
    [remote_name] nvarchar(max) NULL,
    [last_synced_at] datetimeoffset NULL,
    [content_hash] nvarchar(max) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_external_entity_maps] PRIMARY KEY ([id])
);
GO

CREATE TABLE [fiscal_years] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [year] int NOT NULL,
    [starts_on] date NOT NULL,
    [ends_on] date NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [closed_at] datetimeoffset NULL,
    [closed_by] bigint NULL,
    [archived_rows] int NOT NULL,
    [archived_net_sales] decimal(19,2) NOT NULL,
    [notes] nvarchar(500) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_fiscal_years] PRIMARY KEY ([id])
);
GO

CREATE TABLE [gift_cards] (
    [id] bigint NOT NULL IDENTITY,
    [serial_number] nvarchar(max) NOT NULL,
    [original_value] decimal(19,4) NOT NULL,
    [remaining_value] decimal(19,4) NOT NULL,
    [issued_to_customer_id] bigint NULL,
    [issued_on] date NOT NULL,
    [expires_on] date NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_gift_cards] PRIMARY KEY ([id])
);
GO

CREATE TABLE [gift_certificates] (
    [id] bigint NOT NULL IDENTITY,
    [serial_number] nvarchar(max) NOT NULL,
    [original_value] decimal(19,4) NOT NULL,
    [remaining_value] decimal(19,4) NOT NULL,
    [issued_to_customer_id] bigint NULL,
    [issued_on] date NOT NULL,
    [expires_on] date NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_gift_certificates] PRIMARY KEY ([id])
);
GO

CREATE TABLE [invoice_payments] (
    [id] bigint NOT NULL IDENTITY,
    [invoice_id] bigint NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [applied_to_penalty] decimal(19,4) NOT NULL,
    [applied_to_principal] decimal(19,4) NOT NULL,
    [tender_type_id] bigint NOT NULL,
    [paid_on] date NOT NULL,
    [was_distributed] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_invoice_payments] PRIMARY KEY ([id])
);
GO

CREATE TABLE [invoices] (
    [id] bigint NOT NULL IDENTITY,
    [invoice_number] bigint NOT NULL,
    [customer_id] bigint NOT NULL,
    [transaction_id] bigint NOT NULL,
    [issued_on] date NOT NULL,
    [due_on] date NOT NULL,
    [invoice_total] decimal(19,4) NOT NULL,
    [penalty_accrued] decimal(19,4) NOT NULL,
    [balance_due] decimal(19,4) NOT NULL,
    [last_payment_on] date NULL,
    [status] nvarchar(20) NOT NULL,
    [staff_id] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_invoices] PRIMARY KEY ([id])
);
GO

CREATE TABLE [kit_components] (
    [id] bigint NOT NULL IDENTITY,
    [kit_product_id] bigint NOT NULL,
    [component_product_id] bigint NOT NULL,
    [quantity] decimal(19,4) NOT NULL,
    [reduce_stock] bit NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_kit_components] PRIMARY KEY ([id])
);
GO

CREATE TABLE [late_charge_policies] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [monthly_rate] decimal(19,4) NOT NULL,
    [grace_period_days] int NOT NULL,
    [is_enabled] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_late_charge_policies] PRIMARY KEY ([id])
);
GO

CREATE TABLE [layaway_lines] (
    [id] bigint NOT NULL IDENTITY,
    [layaway_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [quantity] decimal(19,4) NOT NULL,
    [unit_price] decimal(19,4) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_layaway_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [layaway_payments] (
    [id] bigint NOT NULL IDENTITY,
    [layaway_id] bigint NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [tender_type_id] bigint NOT NULL,
    [paid_on] date NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_layaway_payments] PRIMARY KEY ([id])
);
GO

CREATE TABLE [layaways] (
    [id] bigint NOT NULL IDENTITY,
    [layaway_number] bigint NOT NULL,
    [customer_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [status] int NOT NULL,
    [total] decimal(19,4) NOT NULL,
    [amount_paid] decimal(19,4) NOT NULL,
    [created_on] date NOT NULL,
    [staff_id] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_layaways] PRIMARY KEY ([id])
);
GO

CREATE TABLE [locations] (
    [id] bigint NOT NULL IDENTITY,
    [name] nvarchar(100) NOT NULL,
    [legacy_code] nvarchar(3) NOT NULL,
    [address_line1] nvarchar(200) NULL,
    [address_line2] nvarchar(200) NULL,
    [address_city] nvarchar(100) NULL,
    [address_state_or_province] nvarchar(100) NULL,
    [address_postal_code] nvarchar(20) NULL,
    [address_country] nvarchar(100) NULL,
    [contact_phone] nvarchar(30) NULL,
    [contact_extension] nvarchar(10) NULL,
    [contact_mobile] nvarchar(30) NULL,
    [contact_fax] nvarchar(30) NULL,
    [contact_email] nvarchar(200) NULL,
    [contact_website] nvarchar(200) NULL,
    [time_zone_id] nvarchar(50) NOT NULL,
    [business_day_start] time NOT NULL,
    [base_currency_code] nvarchar(3) NOT NULL,
    [is_active] bit NOT NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_locations] PRIMARY KEY ([id])
);
GO

CREATE TABLE [loyalty_ledger_entries] (
    [id] bigint NOT NULL IDENTITY,
    [customer_id] bigint NOT NULL,
    [transaction_id] bigint NULL,
    [entry_type] int NOT NULL,
    [points] int NOT NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_loyalty_ledger_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [loyalty_policies] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [is_enabled] bit NOT NULL,
    [points_per_dollar] decimal(19,4) NOT NULL,
    [minimum_required] int NOT NULL,
    [percent_enabled] bit NOT NULL,
    [reward_percent] decimal(19,4) NOT NULL,
    [fixed_enabled] bit NOT NULL,
    [reward_fixed_amount] decimal(19,4) NOT NULL,
    [suppress_if_subtotal_discount_applied] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_loyalty_policies] PRIMARY KEY ([id])
);
GO

CREATE TABLE [matrix_dimensions] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [position] int NOT NULL,
    [name] nvarchar(max) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_matrix_dimensions] PRIMARY KEY ([id])
);
GO

CREATE TABLE [migration_batches] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [source_file_name] nvarchar(260) NOT NULL,
    [entity] nvarchar(30) NOT NULL,
    [source_hash] nvarchar(64) NOT NULL,
    [stage] nvarchar(20) NOT NULL,
    [rows_staged] int NOT NULL,
    [rows_deleted_in_source] int NOT NULL,
    [blocking_errors] int NOT NULL,
    [warnings] int NOT NULL,
    [rows_imported] int NOT NULL,
    [rows_skipped] int NOT NULL,
    [analysis_json] nvarchar(max) NULL,
    [validation_json] nvarchar(max) NULL,
    [reconciliation_json] nvarchar(max) NULL,
    [validated_at] datetimeoffset NULL,
    [dry_run_at] datetimeoffset NULL,
    [imported_at] datetimeoffset NULL,
    [notes] nvarchar(1000) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_migration_batches] PRIMARY KEY ([id])
);
GO

CREATE TABLE [migration_staging_rows] (
    [id] bigint NOT NULL IDENTITY,
    [batch_id] bigint NOT NULL,
    [row_number] int NOT NULL,
    [payload_json] nvarchar(max) NOT NULL,
    [is_deleted_in_source] bit NOT NULL,
    [legacy_key] nvarchar(60) NULL,
    [is_valid] bit NULL,
    [problems] nvarchar(2000) NULL,
    [outcome] nvarchar(200) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_migration_staging_rows] PRIMARY KEY ([id])
);
GO

CREATE TABLE [number_sequences] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [kind] nvarchar(30) NOT NULL,
    [prefix] nvarchar(10) NOT NULL,
    [pad_width] int NOT NULL,
    [next_number] bigint NOT NULL,
    [high_water_mark] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_number_sequences] PRIMARY KEY ([id])
);
GO

CREATE TABLE [OpenIddictApplications] (
    [id] bigint NOT NULL IDENTITY,
    [application_type] nvarchar(50) NULL,
    [client_id] nvarchar(100) NULL,
    [client_secret] nvarchar(max) NULL,
    [client_type] nvarchar(50) NULL,
    [concurrency_token] nvarchar(50) NULL,
    [consent_type] nvarchar(50) NULL,
    [display_name] nvarchar(max) NULL,
    [display_names] nvarchar(max) NULL,
    [json_web_key_set] nvarchar(max) NULL,
    [permissions] nvarchar(max) NULL,
    [post_logout_redirect_uris] nvarchar(max) NULL,
    [properties] nvarchar(max) NULL,
    [redirect_uris] nvarchar(max) NULL,
    [requirements] nvarchar(max) NULL,
    [settings] nvarchar(max) NULL,
    CONSTRAINT [pk_open_iddict_applications] PRIMARY KEY ([id])
);
GO

CREATE TABLE [OpenIddictScopes] (
    [id] bigint NOT NULL IDENTITY,
    [concurrency_token] nvarchar(50) NULL,
    [description] nvarchar(max) NULL,
    [descriptions] nvarchar(max) NULL,
    [display_name] nvarchar(max) NULL,
    [display_names] nvarchar(max) NULL,
    [name] nvarchar(200) NULL,
    [properties] nvarchar(max) NULL,
    [resources] nvarchar(max) NULL,
    CONSTRAINT [pk_open_iddict_scopes] PRIMARY KEY ([id])
);
GO

CREATE TABLE [permissions] (
    [id] bigint NOT NULL IDENTITY,
    [key] nvarchar(60) NOT NULL,
    [description] nvarchar(200) NOT NULL,
    [group] nvarchar(40) NOT NULL,
    [is_sensitive] bit NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_permissions] PRIMARY KEY ([id])
);
GO

CREATE TABLE [pole_display_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [station_id] bigint NULL,
    [name] nvarchar(60) NOT NULL,
    [port] nvarchar(60) NOT NULL,
    [baud_rate] int NOT NULL,
    [line1width] int NOT NULL,
    [line2width] int NOT NULL,
    [idle_line1] nvarchar(60) NOT NULL,
    [idle_line2] nvarchar(60) NOT NULL,
    [clear_command] nvarchar(60) NOT NULL,
    [line1command] nvarchar(60) NOT NULL,
    [line2command] nvarchar(60) NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_pole_display_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [pos_policies] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [apply_tax1] bit NOT NULL,
    [apply_tax2] bit NOT NULL,
    [allow_tax_override] bit NOT NULL,
    [apply_add_on_charge] bit NOT NULL,
    [fast_scan_mode] bit NOT NULL,
    [auto_save_sales] bit NOT NULL,
    [confirm_before_saving_sales] bit NOT NULL,
    [scan_random_weight_barcodes] bit NOT NULL,
    [staff_may_discount] bit NOT NULL,
    [allow_item_list_edit] bit NOT NULL,
    [track_staff_sales] bit NOT NULL,
    [require_supervisor_to_void] bit NOT NULL,
    [use_employee_time_clock] bit NOT NULL,
    [print_credit_card_signature_line] bit NOT NULL,
    [print_client_name_on_sales_slip] bit NOT NULL,
    [carry_over_city_state_zip] bit NOT NULL,
    [default_tender_type_id] bigint NULL,
    [abandoned_cart_timeout_minutes] int NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_pos_policies] PRIMARY KEY ([id])
);
GO

CREATE TABLE [price_breaks] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [level] int NOT NULL,
    [min_quantity] decimal(19,4) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_price_breaks] PRIMARY KEY ([id])
);
GO

CREATE TABLE [price_quote_lines] (
    [id] bigint NOT NULL IDENTITY,
    [price_quote_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [quantity] decimal(19,4) NOT NULL,
    [unit_price] decimal(19,4) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_price_quote_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [price_quotes] (
    [id] bigint NOT NULL IDENTITY,
    [quote_number] bigint NOT NULL,
    [customer_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [status] int NOT NULL,
    [issued_on] date NOT NULL,
    [expires_on] date NULL,
    [total] decimal(19,4) NOT NULL,
    [staff_id] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_price_quotes] PRIMARY KEY ([id])
);
GO

CREATE TABLE [pricing_rule_settings] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [rule_key] nvarchar(40) NOT NULL,
    [order] int NOT NULL,
    [enabled] bit NOT NULL,
    [parameters_json] nvarchar(max) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_pricing_rule_settings] PRIMARY KEY ([id])
);
GO

CREATE TABLE [printer_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [station_id] bigint NULL,
    [name] nvarchar(60) NOT NULL,
    [setup_command] nvarchar(120) NULL,
    [cutter_command] nvarchar(120) NULL,
    [red_command] nvarchar(120) NULL,
    [black_command] nvarchar(120) NULL,
    [port] nvarchar(120) NULL,
    [default_copies] int NOT NULL,
    [page_eject] bit NOT NULL,
    [extra_copy_on_card] bit NOT NULL,
    [initialize_serial] bit NOT NULL,
    [output] nvarchar(16) NOT NULL,
    [columns] int NOT NULL,
    [drawer_trigger] nvarchar(120) NOT NULL,
    [drawer_repeat] int NOT NULL,
    [open_drawer_on_print] bit NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_printer_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [product_prices] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [level] int NOT NULL,
    [price] decimal(19,4) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_product_prices] PRIMARY KEY ([id])
);
GO

CREATE TABLE [product_suppliers] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [supplier_id] bigint NOT NULL,
    [rank] int NOT NULL,
    [cost] decimal(19,4) NOT NULL,
    [reorder_number] nvarchar(max) NULL,
    [case_qty] decimal(19,4) NOT NULL,
    [minimum_order_qty] decimal(19,4) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_product_suppliers] PRIMARY KEY ([id])
);
GO

CREATE TABLE [product_variants] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [dim1value] nvarchar(max) NOT NULL,
    [dim2value] nvarchar(max) NULL,
    [dim3value] nvarchar(max) NULL,
    [variant_code] nvarchar(max) NOT NULL,
    [upc] nvarchar(max) NULL,
    [on_hand] decimal(19,4) NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_product_variants] PRIMARY KEY ([id])
);
GO

CREATE TABLE [products] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [stock_code] nvarchar(30) NOT NULL,
    [name] nvarchar(200) NOT NULL,
    [description] nvarchar(max) NULL,
    [type] int NOT NULL,
    [upc] nvarchar(30) NULL,
    [tax1applies] bit NOT NULL,
    [tax2applies] bit NOT NULL,
    [regular_price] decimal(19,4) NOT NULL,
    [last_cost] decimal(19,4) NOT NULL,
    [avg_cost] decimal(19,4) NOT NULL,
    [gross_margin_pct] decimal(19,4) NOT NULL,
    [base_stock] int NOT NULL,
    [reorder_point] int NOT NULL,
    [reorder_qty] int NOT NULL,
    [on_hand] decimal(19,4) NOT NULL,
    [on_order] decimal(19,4) NOT NULL,
    [case_qty] decimal(19,4) NOT NULL,
    [ship_weight] decimal(19,4) NOT NULL,
    [bin_location] nvarchar(50) NULL,
    [pos_message] nvarchar(max) NULL,
    [invoice_message] nvarchar(max) NULL,
    [notes] nvarchar(max) NULL,
    [has_image] bit NOT NULL,
    [department_id] bigint NULL,
    [category_id] bigint NULL,
    [substitute_product_id] bigint NULL,
    [tag_along_product_id] bigint NULL,
    [parent_product_id] bigint NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_products] PRIMARY KEY ([id])
);
GO

CREATE TABLE [purchase_order_lines] (
    [id] bigint NOT NULL IDENTITY,
    [purchase_order_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [order_qty] decimal(19,4) NOT NULL,
    [case_qty] decimal(19,4) NOT NULL,
    [cost_each] decimal(19,4) NOT NULL,
    [order_cost] decimal(19,4) NOT NULL,
    [qty_received] decimal(19,4) NOT NULL,
    [in_stock_at_generation] decimal(19,4) NOT NULL,
    [on_order_at_generation] decimal(19,4) NOT NULL,
    [back_orders] decimal(19,4) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_purchase_order_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [purchase_order_receipts] (
    [id] bigint NOT NULL IDENTITY,
    [purchase_order_id] bigint NOT NULL,
    [received_on] date NOT NULL,
    [freight_total] decimal(19,4) NOT NULL,
    [staff_id] bigint NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_purchase_order_receipts] PRIMARY KEY ([id])
);
GO

CREATE TABLE [purchase_orders] (
    [id] bigint NOT NULL IDENTITY,
    [po_number] bigint NOT NULL,
    [supplier_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [status] int NOT NULL,
    [quantity_strategy] int NOT NULL,
    [header_text] nvarchar(max) NULL,
    [posted_on] date NULL,
    [due_on] date NULL,
    [total] decimal(19,4) NOT NULL,
    [accounting_bill_ref] nvarchar(max) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_purchase_orders] PRIMARY KEY ([id])
);
GO

CREATE TABLE [reader_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [station_id] bigint NULL,
    [name] nvarchar(60) NOT NULL,
    [host] nvarchar(120) NOT NULL,
    [port] int NOT NULL,
    [protocol] nvarchar(16) NOT NULL,
    [antenna_zones] nvarchar(200) NOT NULL,
    [rssi_threshold_dbm] int NOT NULL,
    [minimum_read_count] int NOT NULL,
    [debounce_ms] int NOT NULL,
    [coalesce_ms] int NOT NULL,
    [flush_interval_ms] int NOT NULL,
    [max_batch_size] int NOT NULL,
    [auto_accept_batches] bit NOT NULL,
    [continuous_mode] bit NOT NULL,
    [output_power_dbm] nvarchar(max) NOT NULL,
    [region] int NOT NULL,
    [frequency_start_index] int NOT NULL,
    [frequency_end_index] int NOT NULL,
    [link_profile] int NOT NULL,
    [beeper] int NOT NULL,
    [antenna_return_loss_threshold_db] int NOT NULL,
    [impinj_fast_tid] bit NOT NULL,
    [dense_reader_mode] bit NOT NULL,
    [device_address] int NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_reader_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [role_permissions] (
    [id] bigint NOT NULL IDENTITY,
    [role_id] bigint NOT NULL,
    [permission_key] nvarchar(60) NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_role_permissions] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sale_adjustments] (
    [id] bigint NOT NULL IDENTITY,
    [transaction_id] bigint NOT NULL,
    [type] nvarchar(24) NOT NULL,
    [label] nvarchar(120) NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [serial] nvarchar(64) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sale_adjustments] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sale_lines] (
    [id] bigint NOT NULL IDENTITY,
    [transaction_id] bigint NOT NULL,
    [sequence] int NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [serialized_unit_id] bigint NULL,
    [epc] nvarchar(96) NULL,
    [serial_number] nvarchar(64) NULL,
    [stock_code_snapshot] nvarchar(24) NULL,
    [name_snapshot] nvarchar(200) NULL,
    [source] nvarchar(20) NOT NULL,
    [quantity] decimal(18,4) NOT NULL,
    [chargeable_quantity] decimal(18,4) NOT NULL,
    [unit_price] decimal(19,4) NOT NULL,
    [discount_pct] decimal(7,2) NOT NULL,
    [extended_net] decimal(19,4) NOT NULL,
    [prorated_adjustment] decimal(19,4) NOT NULL,
    [taxable_net] decimal(19,4) NOT NULL,
    [tax1applies] bit NOT NULL,
    [tax2applies] bit NOT NULL,
    [tax1amount] decimal(19,4) NOT NULL,
    [tax2amount] decimal(19,4) NOT NULL,
    [unit_cost_snapshot] decimal(19,3) NOT NULL,
    [price_origin] nvarchar(20) NOT NULL,
    [line_type] nvarchar(20) NOT NULL,
    [returned_to_stock] bit NOT NULL,
    [note] nvarchar(500) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sale_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sale_pricings] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [discount_pct] decimal(19,4) NOT NULL,
    [starts_on] date NOT NULL,
    [ends_on] date NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sale_pricings] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sale_tax_snapshots] (
    [id] bigint NOT NULL IDENTITY,
    [transaction_id] bigint NOT NULL,
    [tax1name] nvarchar(40) NOT NULL,
    [tax1rate] decimal(9,4) NOT NULL,
    [tax2name] nvarchar(40) NOT NULL,
    [tax2rate] decimal(9,4) NOT NULL,
    [tax2compound] bit NOT NULL,
    [add_on_name] nvarchar(40) NOT NULL,
    [add_on_rate] decimal(9,4) NOT NULL,
    [add_on_taxable] bit NOT NULL,
    [tax_inclusive] bit NOT NULL,
    [tax_registration_number] nvarchar(40) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sale_tax_snapshots] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sale_tenders] (
    [id] bigint NOT NULL IDENTITY,
    [transaction_id] bigint NOT NULL,
    [tender_type_id] bigint NOT NULL,
    [behaviour] nvarchar(24) NOT NULL,
    [amount] decimal(19,4) NOT NULL,
    [amount_tendered] decimal(19,4) NOT NULL,
    [change_given] decimal(19,4) NOT NULL,
    [currency_id] bigint NULL,
    [exchange_rate] decimal(18,8) NOT NULL,
    [reference] nvarchar(64) NULL,
    [auth_code] nvarchar(32) NULL,
    [card_last4] nvarchar(4) NULL,
    [gateway_reference] nvarchar(64) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sale_tenders] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sales_history_archives] (
    [id] bigint NOT NULL IDENTITY,
    [fiscal_year_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [year] int NOT NULL,
    [month] int NOT NULL,
    [product_id] bigint NOT NULL,
    [stock_code_snapshot] nvarchar(30) NOT NULL,
    [name_snapshot] nvarchar(200) NOT NULL,
    [department_id] bigint NULL,
    [quantity_sold] decimal(18,4) NOT NULL,
    [net_sales] decimal(19,2) NOT NULL,
    [cost_of_goods_sold] decimal(19,3) NOT NULL,
    [transaction_count] int NOT NULL,
    [archived_at] datetimeoffset NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sales_history_archives] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sales_transactions] (
    [id] bigint NOT NULL IDENTITY,
    [transaction_number] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [station_id] bigint NOT NULL,
    [staff_id] bigint NOT NULL,
    [customer_id] bigint NULL,
    [drawer_session_id] bigint NULL,
    [business_date] date NOT NULL,
    [subtotal] decimal(19,4) NOT NULL,
    [discount_total] decimal(19,4) NOT NULL,
    [add_on_charge_total] decimal(19,4) NOT NULL,
    [tax1total] decimal(19,4) NOT NULL,
    [tax2total] decimal(19,4) NOT NULL,
    [grand_total] decimal(19,4) NOT NULL,
    [rounding_adjustment] decimal(19,4) NOT NULL,
    [change_given] decimal(19,4) NOT NULL,
    [cost_of_goods_sold] decimal(19,4) NOT NULL,
    [loyalty_points_earned] int NOT NULL,
    [loyalty_points_redeemed] int NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [is_training] bit NOT NULL,
    [voided_by_transaction_id] bigint NULL,
    [reverses_transaction_id] bigint NULL,
    [void_reason] nvarchar(max) NULL,
    [void_approved_by_staff_id] bigint NULL,
    [invoice_id] bigint NULL,
    [reprint_count] int NOT NULL,
    [completed_at] datetimeoffset NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sales_transactions] PRIMARY KEY ([id])
);
GO

CREATE TABLE [scale_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [station_id] bigint NULL,
    [name] nvarchar(60) NOT NULL,
    [port] nvarchar(60) NOT NULL,
    [baud_rate] int NOT NULL,
    [data_bits] int NOT NULL,
    [parity] nvarchar(16) NOT NULL,
    [stop_bits] nvarchar(16) NOT NULL,
    [get_weight_command] nvarchar(16) NOT NULL,
    [zero_command] nvarchar(16) NOT NULL,
    [unit] nvarchar(8) NOT NULL,
    [timeout_ms] int NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_scale_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [serialized_units] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [serial_number] nvarchar(64) NULL,
    [epc] nvarchar(96) NULL,
    [state] nvarchar(20) NOT NULL,
    [location_id] bigint NOT NULL,
    [received_on] datetimeoffset NOT NULL,
    [last_seen_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_serialized_units] PRIMARY KEY ([id])
);
GO

CREATE TABLE [staff_profiles] (
    [id] bigint NOT NULL IDENTITY,
    [user_id] bigint NOT NULL,
    [staff_code] nvarchar(8) NOT NULL,
    [first_name] nvarchar(100) NOT NULL,
    [last_name] nvarchar(100) NOT NULL,
    [pin_hash] nvarchar(256) NULL,
    [failed_pin_attempts] int NOT NULL,
    [pin_locked_until] datetimeoffset NULL,
    [access_level] int NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_staff_profiles] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stations] (
    [id] bigint NOT NULL IDENTITY,
    [station_code] nvarchar(3) NOT NULL,
    [location_id] bigint NOT NULL,
    [name] nvarchar(100) NULL,
    [fast_scan_mode] bit NULL,
    [auto_save_sales] bit NULL,
    [confirm_before_saving] bit NULL,
    [scan_random_weight_barcodes] bit NULL,
    [default_tender_type_id] bigint NULL,
    [printer_profile_id] bigint NULL,
    [reader_profile_id] bigint NULL,
    [scale_profile_id] bigint NULL,
    [pole_display_profile_id] bigint NULL,
    [reader_mode] nvarchar(16) NOT NULL,
    [agent_version] nvarchar(32) NULL,
    [last_heartbeat] datetimeoffset NULL,
    [agent_token_hash] nvarchar(128) NULL,
    [is_active] bit NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stations] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stock_count_lines] (
    [id] bigint NOT NULL IDENTITY,
    [stock_count_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [stock_code] nvarchar(30) NOT NULL,
    [product_name] nvarchar(200) NOT NULL,
    [counted_qty] decimal(18,4) NOT NULL,
    [system_qty_at_count] decimal(18,4) NOT NULL,
    [unit_cost] decimal(19,3) NOT NULL,
    [notes] nvarchar(200) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stock_count_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stock_counts] (
    [id] bigint NOT NULL IDENTITY,
    [count_number] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [notes] nvarchar(500) NULL,
    [department_id] bigint NULL,
    [posted_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stock_counts] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stock_ledger_entries] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [location_id] bigint NOT NULL,
    [movement_type] nvarchar(20) NOT NULL,
    [quantity] decimal(18,4) NOT NULL,
    [unit_cost] decimal(19,3) NOT NULL,
    [reference_type] nvarchar(max) NULL,
    [reference_id] bigint NULL,
    [reason] nvarchar(max) NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [staff_id] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stock_ledger_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stock_levels] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [location_id] bigint NOT NULL,
    [on_hand] decimal(18,4) NOT NULL,
    [on_order] decimal(18,4) NOT NULL,
    [committed] decimal(18,4) NOT NULL,
    [last_sold_on] datetimeoffset NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stock_levels] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stock_transfer_lines] (
    [id] bigint NOT NULL IDENTITY,
    [stock_transfer_id] bigint NOT NULL,
    [product_id] bigint NOT NULL,
    [variant_id] bigint NULL,
    [stock_code] nvarchar(30) NOT NULL,
    [product_name] nvarchar(200) NOT NULL,
    [quantity] decimal(18,4) NOT NULL,
    [quantity_received] decimal(18,4) NOT NULL,
    [unit_cost] decimal(19,3) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stock_transfer_lines] PRIMARY KEY ([id])
);
GO

CREATE TABLE [stock_transfers] (
    [id] bigint NOT NULL IDENTITY,
    [transfer_number] bigint NOT NULL,
    [from_location_id] bigint NOT NULL,
    [to_location_id] bigint NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [notes] nvarchar(500) NULL,
    [shipped_at] datetimeoffset NULL,
    [received_at] datetimeoffset NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_stock_transfers] PRIMARY KEY ([id])
);
GO

CREATE TABLE [supervisor_approvals] (
    [id] bigint NOT NULL IDENTITY,
    [permission] nvarchar(60) NOT NULL,
    [action] nvarchar(120) NOT NULL,
    [context] nvarchar(500) NULL,
    [requested_by_staff_id] bigint NOT NULL,
    [station_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [approved_by_staff_id] bigint NULL,
    [requested_at] datetimeoffset NOT NULL,
    [expires_at] datetimeoffset NOT NULL,
    [answered_at] datetimeoffset NULL,
    [consumed_at] datetimeoffset NULL,
    [denial_reason] nvarchar(500) NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_supervisor_approvals] PRIMARY KEY ([id])
);
GO

CREATE TABLE [suppliers] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [supplier_number] nvarchar(20) NOT NULL,
    [company] nvarchar(200) NOT NULL,
    [contact_first_name] nvarchar(100) NULL,
    [contact_last_name] nvarchar(100) NULL,
    [title] nvarchar(50) NULL,
    [address_line1] nvarchar(200) NULL,
    [address_line2] nvarchar(200) NULL,
    [address_city] nvarchar(100) NULL,
    [address_state_or_province] nvarchar(100) NULL,
    [address_postal_code] nvarchar(20) NULL,
    [address_country] nvarchar(100) NULL,
    [contact_phone] nvarchar(30) NULL,
    [contact_extension] nvarchar(10) NULL,
    [contact_mobile] nvarchar(30) NULL,
    [contact_fax] nvarchar(30) NULL,
    [contact_email] nvarchar(200) NULL,
    [contact_website] nvarchar(200) NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_suppliers] PRIMARY KEY ([id])
);
GO

CREATE TABLE [sync_logs] (
    [id] bigint NOT NULL IDENTITY,
    [provider] nvarchar(max) NOT NULL,
    [direction] int NOT NULL,
    [entity] nvarchar(max) NOT NULL,
    [request_payload] nvarchar(max) NULL,
    [response_payload] nvarchar(max) NULL,
    [status] int NOT NULL,
    [error_message] nvarchar(max) NULL,
    [record_count] int NOT NULL,
    [occurred_at] datetimeoffset NOT NULL,
    [duration_ms] bigint NOT NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_sync_logs] PRIMARY KEY ([id])
);
GO

CREATE TABLE [tax_configurations] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [effective_from] date NOT NULL,
    [effective_to] date NULL,
    [tax1enabled] bit NOT NULL,
    [tax1name] nvarchar(50) NOT NULL,
    [tax1rate] decimal(7,4) NOT NULL,
    [tax2enabled] bit NOT NULL,
    [tax2name] nvarchar(50) NOT NULL,
    [tax2rate] decimal(7,4) NOT NULL,
    [tax2compound] bit NOT NULL,
    [add_on_charge_enabled] bit NOT NULL,
    [add_on_charge_name] nvarchar(50) NOT NULL,
    [add_on_charge_rate] decimal(7,4) NOT NULL,
    [add_on_charge_taxable] bit NOT NULL,
    [taxation_type] nvarchar(20) NOT NULL,
    [registration_number] nvarchar(50) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_tax_configurations] PRIMARY KEY ([id])
);
GO

CREATE TABLE [tender_types] (
    [id] bigint NOT NULL IDENTITY,
    [code] nvarchar(max) NOT NULL,
    [display_name] nvarchar(max) NOT NULL,
    [behaviour] int NOT NULL,
    [sort_order] int NOT NULL,
    [icon_key] nvarchar(max) NULL,
    [opens_cash_drawer] bit NOT NULL,
    [allows_over_tender] bit NOT NULL,
    [rounds_to_minimum_tender] bit NOT NULL,
    [counts_towards_drawer_cash] bit NOT NULL,
    [requires_reference] bit NOT NULL,
    [prints_signature_copy] bit NOT NULL,
    [allowed_for_refunds] bit NOT NULL,
    [currency_code] nvarchar(max) NULL,
    [external_accounting_key] nvarchar(max) NULL,
    [is_active] bit NOT NULL,
    [is_deleted] bit NOT NULL,
    [deleted_at] datetimeoffset NULL,
    [deleted_by] bigint NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_tender_types] PRIMARY KEY ([id])
);
GO

CREATE TABLE [time_clock_entries] (
    [id] bigint NOT NULL IDENTITY,
    [staff_id] bigint NOT NULL,
    [location_id] bigint NOT NULL,
    [clock_in] datetimeoffset NOT NULL,
    [clock_out] datetimeoffset NULL,
    [hours_worked] decimal(9,4) NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_time_clock_entries] PRIMARY KEY ([id])
);
GO

CREATE TABLE [AspNetRoleClaims] (
    [id] int NOT NULL IDENTITY,
    [role_id] bigint NOT NULL,
    [claim_type] nvarchar(max) NULL,
    [claim_value] nvarchar(max) NULL,
    CONSTRAINT [pk_asp_net_role_claims] PRIMARY KEY ([id]),
    CONSTRAINT [fk_asp_net_role_claims_asp_net_roles_role_id] FOREIGN KEY ([role_id]) REFERENCES [AspNetRoles] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserClaims] (
    [id] int NOT NULL IDENTITY,
    [user_id] bigint NOT NULL,
    [claim_type] nvarchar(max) NULL,
    [claim_value] nvarchar(max) NULL,
    CONSTRAINT [pk_asp_net_user_claims] PRIMARY KEY ([id]),
    CONSTRAINT [fk_asp_net_user_claims_asp_net_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [AspNetUsers] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserLogins] (
    [login_provider] nvarchar(450) NOT NULL,
    [provider_key] nvarchar(450) NOT NULL,
    [provider_display_name] nvarchar(max) NULL,
    [user_id] bigint NOT NULL,
    CONSTRAINT [pk_asp_net_user_logins] PRIMARY KEY ([login_provider], [provider_key]),
    CONSTRAINT [fk_asp_net_user_logins_asp_net_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [AspNetUsers] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserRoles] (
    [user_id] bigint NOT NULL,
    [role_id] bigint NOT NULL,
    CONSTRAINT [pk_asp_net_user_roles] PRIMARY KEY ([user_id], [role_id]),
    CONSTRAINT [fk_asp_net_user_roles_asp_net_roles_role_id] FOREIGN KEY ([role_id]) REFERENCES [AspNetRoles] ([id]) ON DELETE CASCADE,
    CONSTRAINT [fk_asp_net_user_roles_asp_net_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [AspNetUsers] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserTokens] (
    [user_id] bigint NOT NULL,
    [login_provider] nvarchar(450) NOT NULL,
    [name] nvarchar(450) NOT NULL,
    [value] nvarchar(max) NULL,
    CONSTRAINT [pk_asp_net_user_tokens] PRIMARY KEY ([user_id], [login_provider], [name]),
    CONSTRAINT [fk_asp_net_user_tokens_asp_net_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [AspNetUsers] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [branding_assets] (
    [id] bigint NOT NULL IDENTITY,
    [location_id] bigint NOT NULL,
    [slot] nvarchar(20) NOT NULL,
    [content] varbinary(max) NOT NULL,
    [content_type] nvarchar(40) NOT NULL,
    [e_tag] nvarchar(32) NOT NULL,
    [opacity_pct] int NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_branding_assets] PRIMARY KEY ([id]),
    CONSTRAINT [fk_branding_assets_locations_location_id] FOREIGN KEY ([location_id]) REFERENCES [locations] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [OpenIddictAuthorizations] (
    [id] bigint NOT NULL IDENTITY,
    [application_id] bigint NULL,
    [concurrency_token] nvarchar(50) NULL,
    [creation_date] datetime2 NULL,
    [properties] nvarchar(max) NULL,
    [scopes] nvarchar(max) NULL,
    [status] nvarchar(50) NULL,
    [subject] nvarchar(400) NULL,
    [type] nvarchar(50) NULL,
    CONSTRAINT [pk_open_iddict_authorizations] PRIMARY KEY ([id]),
    CONSTRAINT [fk_open_iddict_authorizations_open_iddict_applications_application_id] FOREIGN KEY ([application_id]) REFERENCES [OpenIddictApplications] ([id])
);
GO

CREATE TABLE [product_images] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [content] varbinary(max) NOT NULL,
    [content_type] nvarchar(40) NOT NULL,
    [e_tag] nvarchar(32) NOT NULL,
    [created_at] datetimeoffset NOT NULL,
    [created_by] bigint NULL,
    [modified_at] datetimeoffset NULL,
    [modified_by] bigint NULL,
    [row_version] bigint NOT NULL,
    CONSTRAINT [pk_product_images] PRIMARY KEY ([id]),
    CONSTRAINT [fk_product_images_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE CASCADE
);
GO

CREATE TABLE [OpenIddictTokens] (
    [id] bigint NOT NULL IDENTITY,
    [application_id] bigint NULL,
    [authorization_id] bigint NULL,
    [concurrency_token] nvarchar(50) NULL,
    [creation_date] datetime2 NULL,
    [expiration_date] datetime2 NULL,
    [payload] nvarchar(max) NULL,
    [properties] nvarchar(max) NULL,
    [redemption_date] datetime2 NULL,
    [reference_id] nvarchar(100) NULL,
    [status] nvarchar(50) NULL,
    [subject] nvarchar(400) NULL,
    [type] nvarchar(50) NULL,
    CONSTRAINT [pk_open_iddict_tokens] PRIMARY KEY ([id]),
    CONSTRAINT [fk_open_iddict_tokens_open_iddict_applications_application_id] FOREIGN KEY ([application_id]) REFERENCES [OpenIddictApplications] ([id]),
    CONSTRAINT [fk_open_iddict_tokens_open_iddict_authorizations_authorization_id] FOREIGN KEY ([authorization_id]) REFERENCES [OpenIddictAuthorizations] ([id])
);
GO

CREATE INDEX [ix_asp_net_role_claims_role_id] ON [AspNetRoleClaims] ([role_id]);
GO

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([normalized_name]) WHERE [normalized_name] IS NOT NULL;
GO

CREATE INDEX [ix_asp_net_user_claims_user_id] ON [AspNetUserClaims] ([user_id]);
GO

CREATE INDEX [ix_asp_net_user_logins_user_id] ON [AspNetUserLogins] ([user_id]);
GO

CREATE INDEX [ix_asp_net_user_roles_role_id] ON [AspNetUserRoles] ([role_id]);
GO

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([normalized_email]);
GO

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([normalized_user_name]) WHERE [normalized_user_name] IS NOT NULL;
GO

CREATE INDEX [ix_audit_log_entries_actor_staff_id_occurred_at] ON [audit_log_entries] ([actor_staff_id], [occurred_at]);
GO

CREATE INDEX [ix_audit_log_entries_correlation_id] ON [audit_log_entries] ([correlation_id]);
GO

CREATE INDEX [ix_audit_log_entries_entity_type_entity_id] ON [audit_log_entries] ([entity_type], [entity_id]);
GO

CREATE INDEX [ix_audit_log_entries_occurred_at] ON [audit_log_entries] ([occurred_at]);
GO

CREATE UNIQUE INDEX [ix_branding_assets_location_id_slot] ON [branding_assets] ([location_id], [slot]);
GO

CREATE UNIQUE INDEX [ix_business_profiles_location_id] ON [business_profiles] ([location_id]);
GO

CREATE INDEX [ix_cart_adjustments_cart_id] ON [cart_adjustments] ([cart_id]);
GO

CREATE INDEX [ix_cart_lines_cart_id] ON [cart_lines] ([cart_id]);
GO

CREATE UNIQUE INDEX [ix_cart_tax_overrides_cart_id] ON [cart_tax_overrides] ([cart_id]);
GO

CREATE INDEX [ix_carts_station_id_status] ON [carts] ([station_id], [status]) WHERE [status] = 'Active';
GO

CREATE INDEX [ix_commission_ledger_entries_location_id_business_date] ON [commission_ledger_entries] ([location_id], [business_date]);
GO

CREATE INDEX [ix_commission_ledger_entries_staff_id_business_date] ON [commission_ledger_entries] ([staff_id], [business_date]);
GO

CREATE INDEX [ix_commission_ledger_entries_transaction_id] ON [commission_ledger_entries] ([transaction_id]);
GO

CREATE INDEX [ix_commission_rules_staff_id] ON [commission_rules] ([staff_id]);
GO

CREATE UNIQUE INDEX [ix_commission_rules_staff_id_product_id_department_id] ON [commission_rules] ([staff_id], [product_id], [department_id]) WHERE [product_id] IS NOT NULL AND [department_id] IS NOT NULL;
GO

CREATE UNIQUE INDEX [ix_customers_location_id_customer_number] ON [customers] ([location_id], [customer_number]);
GO

CREATE UNIQUE INDEX [ix_departments_location_id_name] ON [departments] ([location_id], [name]);
GO

CREATE INDEX [ix_drawer_ledger_entries_drawer_session_id] ON [drawer_ledger_entries] ([drawer_session_id]);
GO

CREATE INDEX [ix_drawer_sessions_station_id_status] ON [drawer_sessions] ([station_id], [status]) WHERE [status] = 'Open';
GO

CREATE INDEX [ix_fiscal_years_location_id_starts_on] ON [fiscal_years] ([location_id], [starts_on]);
GO

CREATE UNIQUE INDEX [ix_fiscal_years_location_id_year] ON [fiscal_years] ([location_id], [year]);
GO

CREATE INDEX [ix_invoices_customer_id_status] ON [invoices] ([customer_id], [status]) WHERE [status] = 'Open';
GO

CREATE INDEX [ix_invoices_invoice_number] ON [invoices] ([invoice_number]);
GO

CREATE UNIQUE INDEX [ix_locations_legacy_code] ON [locations] ([legacy_code]);
GO

CREATE INDEX [ix_migration_batches_location_id_stage] ON [migration_batches] ([location_id], [stage]);
GO

CREATE INDEX [ix_migration_batches_source_hash] ON [migration_batches] ([source_hash]);
GO

CREATE INDEX [ix_migration_staging_rows_batch_id_legacy_key] ON [migration_staging_rows] ([batch_id], [legacy_key]);
GO

CREATE INDEX [ix_migration_staging_rows_batch_id_row_number] ON [migration_staging_rows] ([batch_id], [row_number]);
GO

CREATE UNIQUE INDEX [ix_number_sequences_location_id_kind] ON [number_sequences] ([location_id], [kind]);
GO

CREATE UNIQUE INDEX [ix_open_iddict_applications_client_id] ON [OpenIddictApplications] ([client_id]) WHERE [client_id] IS NOT NULL;
GO

CREATE INDEX [ix_open_iddict_authorizations_application_id_status_subject_type] ON [OpenIddictAuthorizations] ([application_id], [status], [subject], [type]);
GO

CREATE UNIQUE INDEX [ix_open_iddict_scopes_name] ON [OpenIddictScopes] ([name]) WHERE [name] IS NOT NULL;
GO

CREATE INDEX [ix_open_iddict_tokens_application_id_status_subject_type] ON [OpenIddictTokens] ([application_id], [status], [subject], [type]);
GO

CREATE INDEX [ix_open_iddict_tokens_authorization_id] ON [OpenIddictTokens] ([authorization_id]);
GO

CREATE UNIQUE INDEX [ix_open_iddict_tokens_reference_id] ON [OpenIddictTokens] ([reference_id]) WHERE [reference_id] IS NOT NULL;
GO

CREATE UNIQUE INDEX [ix_permissions_key] ON [permissions] ([key]);
GO

CREATE INDEX [ix_pole_display_profiles_location_id_station_id] ON [pole_display_profiles] ([location_id], [station_id]);
GO

CREATE UNIQUE INDEX [ix_pricing_rule_settings_location_id_rule_key] ON [pricing_rule_settings] ([location_id], [rule_key]);
GO

CREATE INDEX [ix_printer_profiles_location_id_station_id] ON [printer_profiles] ([location_id], [station_id]);
GO

CREATE UNIQUE INDEX [ix_product_images_product_id] ON [product_images] ([product_id]);
GO

CREATE UNIQUE INDEX [ix_products_location_id_stock_code] ON [products] ([location_id], [stock_code]) WHERE [is_deleted] = 0;
GO

CREATE INDEX [ix_reader_profiles_location_id_station_id] ON [reader_profiles] ([location_id], [station_id]);
GO

CREATE UNIQUE INDEX [ix_role_permissions_role_id_permission_key] ON [role_permissions] ([role_id], [permission_key]);
GO

CREATE INDEX [ix_sale_adjustments_transaction_id] ON [sale_adjustments] ([transaction_id]);
GO

CREATE INDEX [ix_sale_lines_product_id] ON [sale_lines] ([product_id]);
GO

CREATE INDEX [ix_sale_lines_transaction_id] ON [sale_lines] ([transaction_id]);
GO

CREATE UNIQUE INDEX [ix_sale_tax_snapshots_transaction_id] ON [sale_tax_snapshots] ([transaction_id]);
GO

CREATE INDEX [ix_sale_tenders_transaction_id] ON [sale_tenders] ([transaction_id]);
GO

CREATE INDEX [ix_sales_history_archives_fiscal_year_id] ON [sales_history_archives] ([fiscal_year_id]);
GO

CREATE UNIQUE INDEX [ix_sales_history_archives_location_id_year_month_product_id] ON [sales_history_archives] ([location_id], [year], [month], [product_id]);
GO

CREATE INDEX [ix_sales_transactions_location_id_completed_at] ON [sales_transactions] ([location_id], [completed_at]);
GO

CREATE INDEX [ix_sales_transactions_transaction_number] ON [sales_transactions] ([transaction_number]);
GO

CREATE INDEX [ix_scale_profiles_location_id_station_id] ON [scale_profiles] ([location_id], [station_id]);
GO

CREATE UNIQUE INDEX [ix_serialized_units_epc] ON [serialized_units] ([epc]) WHERE [epc] IS NOT NULL;
GO

CREATE INDEX [ix_serialized_units_product_id_state] ON [serialized_units] ([product_id], [state]);
GO

CREATE UNIQUE INDEX [ix_staff_profiles_staff_code] ON [staff_profiles] ([staff_code]);
GO

CREATE UNIQUE INDEX [ix_staff_profiles_user_id] ON [staff_profiles] ([user_id]);
GO

CREATE UNIQUE INDEX [ix_stations_location_id_station_code] ON [stations] ([location_id], [station_code]);
GO

CREATE INDEX [ix_stock_count_lines_stock_count_id] ON [stock_count_lines] ([stock_count_id]);
GO

CREATE UNIQUE INDEX [ix_stock_count_lines_stock_count_id_product_id_variant_id] ON [stock_count_lines] ([stock_count_id], [product_id], [variant_id]) WHERE [variant_id] IS NOT NULL;
GO

CREATE UNIQUE INDEX [ix_stock_counts_location_id_count_number] ON [stock_counts] ([location_id], [count_number]);
GO

CREATE INDEX [ix_stock_counts_location_id_status] ON [stock_counts] ([location_id], [status]);
GO

CREATE INDEX [ix_stock_ledger_entries_product_id_location_id_occurred_at] ON [stock_ledger_entries] ([product_id], [location_id], [occurred_at]);
GO

CREATE UNIQUE INDEX [ix_stock_levels_product_id_variant_id_location_id] ON [stock_levels] ([product_id], [variant_id], [location_id]) WHERE [variant_id] IS NOT NULL;
GO

CREATE INDEX [ix_stock_transfer_lines_stock_transfer_id] ON [stock_transfer_lines] ([stock_transfer_id]);
GO

CREATE UNIQUE INDEX [ix_stock_transfer_lines_stock_transfer_id_product_id_variant_id] ON [stock_transfer_lines] ([stock_transfer_id], [product_id], [variant_id]) WHERE [variant_id] IS NOT NULL;
GO

CREATE INDEX [ix_stock_transfers_from_location_id_status] ON [stock_transfers] ([from_location_id], [status]);
GO

CREATE UNIQUE INDEX [ix_stock_transfers_from_location_id_transfer_number] ON [stock_transfers] ([from_location_id], [transfer_number]);
GO

CREATE INDEX [ix_stock_transfers_to_location_id_status] ON [stock_transfers] ([to_location_id], [status]);
GO

CREATE INDEX [ix_supervisor_approvals_location_id_status_expires_at] ON [supervisor_approvals] ([location_id], [status], [expires_at]);
GO

CREATE INDEX [ix_tax_configurations_location_id_effective_from] ON [tax_configurations] ([location_id] DESC, [effective_from] DESC);
GO

CREATE INDEX [ix_time_clock_entries_location_id_clock_in] ON [time_clock_entries] ([location_id], [clock_in]);
GO

CREATE INDEX [ix_time_clock_entries_staff_id_clock_in] ON [time_clock_entries] ([staff_id], [clock_in]);
GO

INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
VALUES (N'20260805081608_InitialSqlServer', N'8.0.11');
GO

COMMIT;
GO

