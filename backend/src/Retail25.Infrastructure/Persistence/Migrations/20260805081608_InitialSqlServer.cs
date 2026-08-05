using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ar_ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    entry_type = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ar_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    legacy_level = table.Column<int>(type: "int", nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    display_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    default_location_id = table.Column<long>(type: "bigint", nullable: true),
                    last_signed_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    security_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    action = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_staff_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    station_id = table.Column<long>(type: "bigint", nullable: true),
                    location_id = table.Column<long>(type: "bigint", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    entity_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    operation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    approver_staff_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bonus_pricings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    buy_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    free_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bonus_pricings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    business_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    address_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address_state_or_province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address_postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    address_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    contact_mobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_fax = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    contact_website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    licence_number = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    tax_registration_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cart_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    cart_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    percent = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    serial = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    applied_by_staff_id = table.Column<long>(type: "bigint", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_adjustments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cart_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    cart_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    serialized_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    epc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    manual_unit_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    manual_discount_pct = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    requested_price_level = table.Column<int>(type: "int", nullable: true),
                    tax1override = table.Column<bool>(type: "bit", nullable: true),
                    tax2override = table.Column<bool>(type: "bit", nullable: true),
                    embedded_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    line_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    return_to_stock = table.Column<bool>(type: "bit", nullable: false),
                    note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    price_origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    line_discount_pct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    tax1applies = table.Column<bool>(type: "bit", nullable: false),
                    tax2applies = table.Column<bool>(type: "bit", nullable: false),
                    extended_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax1amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax2amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    stock_code_snapshot = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    name_snapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    unit_cost_snapshot = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cart_tax_overrides",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    cart_id = table.Column<long>(type: "bigint", nullable: false),
                    tax1 = table.Column<bool>(type: "bit", nullable: true),
                    tax2 = table.Column<bool>(type: "bit", nullable: true),
                    applies_from_sequence = table.Column<int>(type: "int", nullable: false),
                    applied_by_staff_id = table.Column<long>(type: "bigint", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_tax_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    held_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    suspended_by_staff_id = table.Column<long>(type: "bigint", nullable: true),
                    next_line_sequence = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false),
                    completed_transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commission_ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    sale_line_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    stock_code_snapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    commission_rule_id = table.Column<long>(type: "bigint", nullable: true),
                    commission_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    rate_applied = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    line_net = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    line_cost = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    was_capped = table.Column<bool>(type: "bit", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commission_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    commission_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    max_commission = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    scale = table.Column<int>(type: "int", nullable: false),
                    rounding = table.Column<int>(type: "int", nullable: false),
                    minimum_tender = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    is_base_currency = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    exchange_rate_updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    account_number = table.Column<long>(type: "bigint", nullable: false),
                    credit_limit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_due = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_order_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    ordered_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    filled_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_order_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_number = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    ordered_on = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_pricing_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    usual_discount_pct = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    price_level = table.Column<int>(type: "int", nullable: false),
                    exempt_tax1 = table.Column<bool>(type: "bit", nullable: false),
                    exempt_tax2 = table.Column<bool>(type: "bit", nullable: false),
                    reward_points = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_pricing_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_number = table.Column<long>(type: "bigint", nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    company = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    billing_address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    billing_address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    billing_address_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    billing_address_state_or_province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    billing_address_postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    billing_address_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ship_to_address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ship_to_address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ship_to_address_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ship_to_address_state_or_province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ship_to_address_postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ship_to_address_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    contact_mobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_fax = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    contact_website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    client_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    birthday = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    last_purchase_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_mailing_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    friendly_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drawer_ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    drawer_session_id = table.Column<long>(type: "bigint", nullable: false),
                    entry_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drawer_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drawer_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    opened_by_staff_id = table.Column<long>(type: "bigint", nullable: false),
                    closed_by_staff_id = table.Column<long>(type: "bigint", nullable: true),
                    opening_float = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    counted_cash = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    expected_cash = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    variance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tender_totals_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    department_net_sales_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    net_sales = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax1collected = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax2collected = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    cost_of_goods_sold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    transaction_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drawer_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_entity_maps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    local_id = table.Column<long>(type: "bigint", nullable: true),
                    local_key = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    remote_id = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    remote_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    content_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_entity_maps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_years",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    archived_rows = table.Column<int>(type: "int", nullable: false),
                    archived_net_sales = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_years", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gift_cards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    serial_number = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    original_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    remaining_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    issued_to_customer_id = table.Column<long>(type: "bigint", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_cards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gift_certificates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    serial_number = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    original_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    remaining_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    issued_to_customer_id = table.Column<long>(type: "bigint", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_certificates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    applied_to_penalty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    applied_to_principal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tender_type_id = table.Column<long>(type: "bigint", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    was_distributed = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_number = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    invoice_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    penalty_accrued = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_due = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    last_payment_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kit_components",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    kit_product_id = table.Column<long>(type: "bigint", nullable: false),
                    component_product_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reduce_stock = table.Column<bool>(type: "bit", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kit_components", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "late_charge_policies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    monthly_rate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    grace_period_days = table.Column<int>(type: "int", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_late_charge_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layaway_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    layaway_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layaway_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layaway_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    layaway_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tender_type_id = table.Column<long>(type: "bigint", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layaway_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layaways",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    layaway_number = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_paid = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    created_on = table.Column<DateOnly>(type: "date", nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layaways", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    legacy_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    address_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address_state_or_province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address_postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    address_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    contact_mobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_fax = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    contact_website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    time_zone_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    business_day_start = table.Column<TimeOnly>(type: "time", nullable: false),
                    base_currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    entry_type = table.Column<int>(type: "int", nullable: false),
                    points = table.Column<int>(type: "int", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_policies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    points_per_dollar = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    minimum_required = table.Column<int>(type: "int", nullable: false),
                    percent_enabled = table.Column<bool>(type: "bit", nullable: false),
                    reward_percent = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    fixed_enabled = table.Column<bool>(type: "bit", nullable: false),
                    reward_fixed_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    suppress_if_subtotal_discount_applied = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "matrix_dimensions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matrix_dimensions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "migration_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    source_file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    entity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    rows_staged = table.Column<int>(type: "int", nullable: false),
                    rows_deleted_in_source = table.Column<int>(type: "int", nullable: false),
                    blocking_errors = table.Column<int>(type: "int", nullable: false),
                    warnings = table.Column<int>(type: "int", nullable: false),
                    rows_imported = table.Column<int>(type: "int", nullable: false),
                    rows_skipped = table.Column<int>(type: "int", nullable: false),
                    analysis_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    validation_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reconciliation_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    dry_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_migration_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "migration_staging_rows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    row_number = table.Column<int>(type: "int", nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_deleted_in_source = table.Column<bool>(type: "bit", nullable: false),
                    legacy_key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    is_valid = table.Column<bool>(type: "bit", nullable: true),
                    problems = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_migration_staging_rows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    pad_width = table.Column<int>(type: "int", nullable: false),
                    next_number = table.Column<long>(type: "bigint", nullable: false),
                    high_water_mark = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    application_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    client_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    display_names = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    json_web_key_set = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    permissions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    redirect_uris = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    requirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    settings = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    descriptions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    display_names = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    resources = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    group = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    is_sensitive = table.Column<bool>(type: "bit", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pole_display_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    port = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    baud_rate = table.Column<int>(type: "int", nullable: false),
                    line1width = table.Column<int>(type: "int", nullable: false),
                    line2width = table.Column<int>(type: "int", nullable: false),
                    idle_line1 = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    idle_line2 = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    clear_command = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    line1command = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    line2command = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pole_display_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pos_policies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    apply_tax1 = table.Column<bool>(type: "bit", nullable: false),
                    apply_tax2 = table.Column<bool>(type: "bit", nullable: false),
                    allow_tax_override = table.Column<bool>(type: "bit", nullable: false),
                    apply_add_on_charge = table.Column<bool>(type: "bit", nullable: false),
                    fast_scan_mode = table.Column<bool>(type: "bit", nullable: false),
                    auto_save_sales = table.Column<bool>(type: "bit", nullable: false),
                    confirm_before_saving_sales = table.Column<bool>(type: "bit", nullable: false),
                    scan_random_weight_barcodes = table.Column<bool>(type: "bit", nullable: false),
                    staff_may_discount = table.Column<bool>(type: "bit", nullable: false),
                    allow_item_list_edit = table.Column<bool>(type: "bit", nullable: false),
                    track_staff_sales = table.Column<bool>(type: "bit", nullable: false),
                    require_supervisor_to_void = table.Column<bool>(type: "bit", nullable: false),
                    use_employee_time_clock = table.Column<bool>(type: "bit", nullable: false),
                    print_credit_card_signature_line = table.Column<bool>(type: "bit", nullable: false),
                    print_client_name_on_sales_slip = table.Column<bool>(type: "bit", nullable: false),
                    carry_over_city_state_zip = table.Column<bool>(type: "bit", nullable: false),
                    default_tender_type_id = table.Column<long>(type: "bigint", nullable: true),
                    abandoned_cart_timeout_minutes = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_breaks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "int", nullable: false),
                    min_quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_breaks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_quote_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    price_quote_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_quote_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_quotes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    quote_number = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_quotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_rule_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    order = table.Column<int>(type: "int", nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pricing_rule_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "printer_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    setup_command = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    cutter_command = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    red_command = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    black_command = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    port = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    default_copies = table.Column<int>(type: "int", nullable: false),
                    page_eject = table.Column<bool>(type: "bit", nullable: false),
                    extra_copy_on_card = table.Column<bool>(type: "bit", nullable: false),
                    initialize_serial = table.Column<bool>(type: "bit", nullable: false),
                    output = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    columns = table.Column<int>(type: "int", nullable: false),
                    drawer_trigger = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    drawer_repeat = table.Column<int>(type: "int", nullable: false),
                    open_drawer_on_print = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_printer_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_prices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "int", nullable: false),
                    price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_suppliers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    rank = table.Column<int>(type: "int", nullable: false),
                    cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reorder_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    case_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    minimum_order_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    dim1value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dim2value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dim3value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    variant_code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    upc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    on_hand = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    stock_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    type = table.Column<int>(type: "int", nullable: false),
                    upc = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    tax1applies = table.Column<bool>(type: "bit", nullable: false),
                    tax2applies = table.Column<bool>(type: "bit", nullable: false),
                    regular_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    last_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    avg_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    gross_margin_pct = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    base_stock = table.Column<int>(type: "int", nullable: false),
                    reorder_point = table.Column<int>(type: "int", nullable: false),
                    reorder_qty = table.Column<int>(type: "int", nullable: false),
                    on_hand = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    on_order = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    case_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ship_weight = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    bin_location = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pos_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    invoice_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    has_image = table.Column<bool>(type: "bit", nullable: false),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    substitute_product_id = table.Column<long>(type: "bigint", nullable: true),
                    tag_along_product_id = table.Column<long>(type: "bigint", nullable: true),
                    parent_product_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    order_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    case_qty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    cost_each = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    order_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    qty_received = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    in_stock_at_generation = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    on_order_at_generation = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    back_orders = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_receipts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    freight_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    po_number = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    quantity_strategy = table.Column<int>(type: "int", nullable: false),
                    header_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    posted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    accounting_bill_ref = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reader_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    host = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    port = table.Column<int>(type: "int", nullable: false),
                    protocol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    antenna_zones = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    rssi_threshold_dbm = table.Column<int>(type: "int", nullable: false),
                    minimum_read_count = table.Column<int>(type: "int", nullable: false),
                    debounce_ms = table.Column<int>(type: "int", nullable: false),
                    coalesce_ms = table.Column<int>(type: "int", nullable: false),
                    flush_interval_ms = table.Column<int>(type: "int", nullable: false),
                    max_batch_size = table.Column<int>(type: "int", nullable: false),
                    auto_accept_batches = table.Column<bool>(type: "bit", nullable: false),
                    continuous_mode = table.Column<bool>(type: "bit", nullable: false),
                    output_power_dbm = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    region = table.Column<int>(type: "int", nullable: false),
                    frequency_start_index = table.Column<int>(type: "int", nullable: false),
                    frequency_end_index = table.Column<int>(type: "int", nullable: false),
                    link_profile = table.Column<int>(type: "int", nullable: false),
                    beeper = table.Column<int>(type: "int", nullable: false),
                    antenna_return_loss_threshold_db = table.Column<int>(type: "int", nullable: false),
                    impinj_fast_tid = table.Column<bool>(type: "bit", nullable: false),
                    dense_reader_mode = table.Column<bool>(type: "bit", nullable: false),
                    device_address = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reader_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    permission_key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    serial = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_adjustments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    serialized_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    epc = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    serial_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    stock_code_snapshot = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    name_snapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    chargeable_quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_pct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    extended_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    prorated_adjustment = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    taxable_net = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax1applies = table.Column<bool>(type: "bit", nullable: false),
                    tax2applies = table.Column<bool>(type: "bit", nullable: false),
                    tax1amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax2amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_cost_snapshot = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    price_origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    line_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    returned_to_stock = table.Column<bool>(type: "bit", nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_pricings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    discount_pct = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_pricings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_tax_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    tax1name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    tax1rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    tax2name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    tax2rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    tax2compound = table.Column<bool>(type: "bit", nullable: false),
                    add_on_name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    add_on_rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    add_on_taxable = table.Column<bool>(type: "bit", nullable: false),
                    tax_inclusive = table.Column<bool>(type: "bit", nullable: false),
                    tax_registration_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_tax_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_tenders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    tender_type_id = table.Column<long>(type: "bigint", nullable: false),
                    behaviour = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_tendered = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    change_given = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_id = table.Column<long>(type: "bigint", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    auth_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    card_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    gateway_reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_tenders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_history_archives",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    fiscal_year_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    month = table.Column<int>(type: "int", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    stock_code_snapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name_snapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity_sold = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    net_sales = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    cost_of_goods_sold = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    transaction_count = table.Column<int>(type: "int", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_history_archives", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_transactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transaction_number = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    drawer_session_id = table.Column<long>(type: "bigint", nullable: true),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    add_on_charge_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax1total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax2total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    grand_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    rounding_adjustment = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    change_given = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    cost_of_goods_sold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    loyalty_points_earned = table.Column<int>(type: "int", nullable: false),
                    loyalty_points_redeemed = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    is_training = table.Column<bool>(type: "bit", nullable: false),
                    voided_by_transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    reverses_transaction_id = table.Column<long>(type: "bigint", nullable: true),
                    void_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    void_approved_by_staff_id = table.Column<long>(type: "bigint", nullable: true),
                    invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    reprint_count = table.Column<int>(type: "int", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scale_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    port = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    baud_rate = table.Column<int>(type: "int", nullable: false),
                    data_bits = table.Column<int>(type: "int", nullable: false),
                    parity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    stop_bits = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    get_weight_command = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    zero_command = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    timeout_ms = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scale_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "serialized_units",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    serial_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    epc = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    state = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    received_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serialized_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    pin_hash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    failed_pin_attempts = table.Column<int>(type: "int", nullable: false),
                    pin_locked_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    access_level = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    station_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    fast_scan_mode = table.Column<bool>(type: "bit", nullable: true),
                    auto_save_sales = table.Column<bool>(type: "bit", nullable: true),
                    confirm_before_saving = table.Column<bool>(type: "bit", nullable: true),
                    scan_random_weight_barcodes = table.Column<bool>(type: "bit", nullable: true),
                    default_tender_type_id = table.Column<long>(type: "bigint", nullable: true),
                    printer_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    reader_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    scale_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    pole_display_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    reader_mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    agent_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    last_heartbeat = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    agent_token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    stock_count_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    stock_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    product_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    counted_qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    system_qty_at_count = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_count_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    count_number = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    department_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_counts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    movement_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    reference_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reference_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    staff_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    on_hand = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    on_order = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    committed = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    last_sold_on = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_levels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    stock_transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: true),
                    stock_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    product_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_received = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "decimal(19,3)", precision: 19, scale: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    transfer_number = table.Column<long>(type: "bigint", nullable: false),
                    from_location_id = table.Column<long>(type: "bigint", nullable: false),
                    to_location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supervisor_approvals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    permission = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    action = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    context = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    requested_by_staff_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approved_by_staff_id = table.Column<long>(type: "bigint", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    denial_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supervisor_approvals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    company = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    contact_first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    address_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address_state_or_province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address_postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    address_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    contact_mobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_fax = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    contact_website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    direction = table.Column<int>(type: "int", nullable: false),
                    entity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    request_payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    response_payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    record_count = table.Column<int>(type: "int", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_configurations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    tax1enabled = table.Column<bool>(type: "bit", nullable: false),
                    tax1name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    tax1rate = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    tax2enabled = table.Column<bool>(type: "bit", nullable: false),
                    tax2name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    tax2rate = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    tax2compound = table.Column<bool>(type: "bit", nullable: false),
                    add_on_charge_enabled = table.Column<bool>(type: "bit", nullable: false),
                    add_on_charge_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    add_on_charge_rate = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    add_on_charge_taxable = table.Column<bool>(type: "bit", nullable: false),
                    taxation_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    registration_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_configurations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tender_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    behaviour = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    icon_key = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    opens_cash_drawer = table.Column<bool>(type: "bit", nullable: false),
                    allows_over_tender = table.Column<bool>(type: "bit", nullable: false),
                    rounds_to_minimum_tender = table.Column<bool>(type: "bit", nullable: false),
                    counts_towards_drawer_cash = table.Column<bool>(type: "bit", nullable: false),
                    requires_reference = table.Column<bool>(type: "bit", nullable: false),
                    prints_signature_copy = table.Column<bool>(type: "bit", nullable: false),
                    allowed_for_refunds = table.Column<bool>(type: "bit", nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    external_accounting_key = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "time_clock_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    staff_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    clock_in = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    clock_out = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    hours_worked = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_clock_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "AspNetRoles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "AspNetRoles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branding_assets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    slot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    e_tag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    opacity_pct = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branding_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_branding_assets_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictAuthorizations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    application_id = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    scopes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_open_iddict_authorizations_open_iddict_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "OpenIddictApplications",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "product_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    e_tag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_images_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    application_id = table.Column<long>(type: "bigint", nullable: true),
                    authorization_id = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reference_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_open_iddict_tokens_open_iddict_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "OpenIddictApplications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_open_iddict_tokens_open_iddict_authorizations_authorization_id",
                        column: x => x.authorization_id,
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "AspNetRoleClaims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "normalized_name",
                unique: true,
                filter: "[normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "AspNetUserClaims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "AspNetUserLogins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "AspNetUserRoles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "normalized_user_name",
                unique: true,
                filter: "[normalized_user_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_actor_staff_id_occurred_at",
                table: "audit_log_entries",
                columns: new[] { "actor_staff_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_correlation_id",
                table: "audit_log_entries",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_entity_type_entity_id",
                table: "audit_log_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entries_occurred_at",
                table: "audit_log_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_branding_assets_location_id_slot",
                table: "branding_assets",
                columns: new[] { "location_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_profiles_location_id",
                table: "business_profiles",
                column: "location_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cart_adjustments_cart_id",
                table: "cart_adjustments",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_lines_cart_id",
                table: "cart_lines",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_tax_overrides_cart_id",
                table: "cart_tax_overrides",
                column: "cart_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_carts_station_id_status",
                table: "carts",
                columns: new[] { "station_id", "status" },
                filter: "[status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_commission_ledger_entries_location_id_business_date",
                table: "commission_ledger_entries",
                columns: new[] { "location_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_ledger_entries_staff_id_business_date",
                table: "commission_ledger_entries",
                columns: new[] { "staff_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_ledger_entries_transaction_id",
                table: "commission_ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_staff_id",
                table: "commission_rules",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_staff_id_product_id_department_id",
                table: "commission_rules",
                columns: new[] { "staff_id", "product_id", "department_id" },
                unique: true,
                filter: "[product_id] IS NOT NULL AND [department_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_location_id_customer_number",
                table: "customers",
                columns: new[] { "location_id", "customer_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_location_id_name",
                table: "departments",
                columns: new[] { "location_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drawer_ledger_entries_drawer_session_id",
                table: "drawer_ledger_entries",
                column: "drawer_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_drawer_sessions_station_id_status",
                table: "drawer_sessions",
                columns: new[] { "station_id", "status" },
                filter: "[status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_years_location_id_starts_on",
                table: "fiscal_years",
                columns: new[] { "location_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_years_location_id_year",
                table: "fiscal_years",
                columns: new[] { "location_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer_id_status",
                table: "invoices",
                columns: new[] { "customer_id", "status" },
                filter: "[status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number");

            migrationBuilder.CreateIndex(
                name: "ix_locations_legacy_code",
                table: "locations",
                column: "legacy_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_migration_batches_location_id_stage",
                table: "migration_batches",
                columns: new[] { "location_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_migration_batches_source_hash",
                table: "migration_batches",
                column: "source_hash");

            migrationBuilder.CreateIndex(
                name: "ix_migration_staging_rows_batch_id_legacy_key",
                table: "migration_staging_rows",
                columns: new[] { "batch_id", "legacy_key" });

            migrationBuilder.CreateIndex(
                name: "ix_migration_staging_rows_batch_id_row_number",
                table: "migration_staging_rows",
                columns: new[] { "batch_id", "row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_location_id_kind",
                table: "number_sequences",
                columns: new[] { "location_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_applications_client_id",
                table: "OpenIddictApplications",
                column: "client_id",
                unique: true,
                filter: "[client_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_authorizations_application_id_status_subject_type",
                table: "OpenIddictAuthorizations",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_scopes_name",
                table: "OpenIddictScopes",
                column: "name",
                unique: true,
                filter: "[name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_application_id_status_subject_type",
                table: "OpenIddictTokens",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_authorization_id",
                table: "OpenIddictTokens",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_reference_id",
                table: "OpenIddictTokens",
                column: "reference_id",
                unique: true,
                filter: "[reference_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_key",
                table: "permissions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_display_profiles_location_id_station_id",
                table: "pole_display_profiles",
                columns: new[] { "location_id", "station_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pricing_rule_settings_location_id_rule_key",
                table: "pricing_rule_settings",
                columns: new[] { "location_id", "rule_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_printer_profiles_location_id_station_id",
                table: "printer_profiles",
                columns: new[] { "location_id", "station_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id",
                table: "product_images",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_location_id_stock_code",
                table: "products",
                columns: new[] { "location_id", "stock_code" },
                unique: true,
                filter: "[is_deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_reader_profiles_location_id_station_id",
                table: "reader_profiles",
                columns: new[] { "location_id", "station_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_id_permission_key",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_adjustments_transaction_id",
                table: "sale_adjustments",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_product_id",
                table: "sale_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_transaction_id",
                table: "sale_lines",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_tax_snapshots_transaction_id",
                table: "sale_tax_snapshots",
                column: "transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_transaction_id",
                table: "sale_tenders",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_history_archives_fiscal_year_id",
                table: "sales_history_archives",
                column: "fiscal_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_history_archives_location_id_year_month_product_id",
                table: "sales_history_archives",
                columns: new[] { "location_id", "year", "month", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_transactions_location_id_completed_at",
                table: "sales_transactions",
                columns: new[] { "location_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_transactions_transaction_number",
                table: "sales_transactions",
                column: "transaction_number");

            migrationBuilder.CreateIndex(
                name: "ix_scale_profiles_location_id_station_id",
                table: "scale_profiles",
                columns: new[] { "location_id", "station_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serialized_units_epc",
                table: "serialized_units",
                column: "epc",
                unique: true,
                filter: "[epc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_serialized_units_product_id_state",
                table: "serialized_units",
                columns: new[] { "product_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_profiles_staff_code",
                table: "staff_profiles",
                column: "staff_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_profiles_user_id",
                table: "staff_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stations_location_id_station_code",
                table: "stations",
                columns: new[] { "location_id", "station_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_count_lines_stock_count_id",
                table: "stock_count_lines",
                column: "stock_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_count_lines_stock_count_id_product_id_variant_id",
                table: "stock_count_lines",
                columns: new[] { "stock_count_id", "product_id", "variant_id" },
                unique: true,
                filter: "[variant_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_location_id_count_number",
                table: "stock_counts",
                columns: new[] { "location_id", "count_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_location_id_status",
                table: "stock_counts",
                columns: new[] { "location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_product_id_location_id_occurred_at",
                table: "stock_ledger_entries",
                columns: new[] { "product_id", "location_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_product_id_variant_id_location_id",
                table: "stock_levels",
                columns: new[] { "product_id", "variant_id", "location_id" },
                unique: true,
                filter: "[variant_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_stock_transfer_id",
                table: "stock_transfer_lines",
                column: "stock_transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_stock_transfer_id_product_id_variant_id",
                table: "stock_transfer_lines",
                columns: new[] { "stock_transfer_id", "product_id", "variant_id" },
                unique: true,
                filter: "[variant_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_location_id_status",
                table: "stock_transfers",
                columns: new[] { "from_location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_location_id_transfer_number",
                table: "stock_transfers",
                columns: new[] { "from_location_id", "transfer_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_to_location_id_status",
                table: "stock_transfers",
                columns: new[] { "to_location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_supervisor_approvals_location_id_status_expires_at",
                table: "supervisor_approvals",
                columns: new[] { "location_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_configurations_location_id_effective_from",
                table: "tax_configurations",
                columns: new[] { "location_id", "effective_from" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_time_clock_entries_location_id_clock_in",
                table: "time_clock_entries",
                columns: new[] { "location_id", "clock_in" });

            migrationBuilder.CreateIndex(
                name: "ix_time_clock_entries_staff_id_clock_in",
                table: "time_clock_entries",
                columns: new[] { "staff_id", "clock_in" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ar_ledger_entries");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "audit_log_entries");

            migrationBuilder.DropTable(
                name: "bonus_pricings");

            migrationBuilder.DropTable(
                name: "branding_assets");

            migrationBuilder.DropTable(
                name: "business_profiles");

            migrationBuilder.DropTable(
                name: "cart_adjustments");

            migrationBuilder.DropTable(
                name: "cart_lines");

            migrationBuilder.DropTable(
                name: "cart_tax_overrides");

            migrationBuilder.DropTable(
                name: "carts");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "commission_ledger_entries");

            migrationBuilder.DropTable(
                name: "commission_rules");

            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropTable(
                name: "customer_accounts");

            migrationBuilder.DropTable(
                name: "customer_order_lines");

            migrationBuilder.DropTable(
                name: "customer_orders");

            migrationBuilder.DropTable(
                name: "customer_pricing_profiles");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "drawer_ledger_entries");

            migrationBuilder.DropTable(
                name: "drawer_sessions");

            migrationBuilder.DropTable(
                name: "external_entity_maps");

            migrationBuilder.DropTable(
                name: "fiscal_years");

            migrationBuilder.DropTable(
                name: "gift_cards");

            migrationBuilder.DropTable(
                name: "gift_certificates");

            migrationBuilder.DropTable(
                name: "invoice_payments");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "kit_components");

            migrationBuilder.DropTable(
                name: "late_charge_policies");

            migrationBuilder.DropTable(
                name: "layaway_lines");

            migrationBuilder.DropTable(
                name: "layaway_payments");

            migrationBuilder.DropTable(
                name: "layaways");

            migrationBuilder.DropTable(
                name: "loyalty_ledger_entries");

            migrationBuilder.DropTable(
                name: "loyalty_policies");

            migrationBuilder.DropTable(
                name: "matrix_dimensions");

            migrationBuilder.DropTable(
                name: "migration_batches");

            migrationBuilder.DropTable(
                name: "migration_staging_rows");

            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "OpenIddictScopes");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "pole_display_profiles");

            migrationBuilder.DropTable(
                name: "pos_policies");

            migrationBuilder.DropTable(
                name: "price_breaks");

            migrationBuilder.DropTable(
                name: "price_quote_lines");

            migrationBuilder.DropTable(
                name: "price_quotes");

            migrationBuilder.DropTable(
                name: "pricing_rule_settings");

            migrationBuilder.DropTable(
                name: "printer_profiles");

            migrationBuilder.DropTable(
                name: "product_images");

            migrationBuilder.DropTable(
                name: "product_prices");

            migrationBuilder.DropTable(
                name: "product_suppliers");

            migrationBuilder.DropTable(
                name: "product_variants");

            migrationBuilder.DropTable(
                name: "purchase_order_lines");

            migrationBuilder.DropTable(
                name: "purchase_order_receipts");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "reader_profiles");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "sale_adjustments");

            migrationBuilder.DropTable(
                name: "sale_lines");

            migrationBuilder.DropTable(
                name: "sale_pricings");

            migrationBuilder.DropTable(
                name: "sale_tax_snapshots");

            migrationBuilder.DropTable(
                name: "sale_tenders");

            migrationBuilder.DropTable(
                name: "sales_history_archives");

            migrationBuilder.DropTable(
                name: "sales_transactions");

            migrationBuilder.DropTable(
                name: "scale_profiles");

            migrationBuilder.DropTable(
                name: "serialized_units");

            migrationBuilder.DropTable(
                name: "staff_profiles");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropTable(
                name: "stock_ledger_entries");

            migrationBuilder.DropTable(
                name: "stock_levels");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropTable(
                name: "supervisor_approvals");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "sync_logs");

            migrationBuilder.DropTable(
                name: "tax_configurations");

            migrationBuilder.DropTable(
                name: "tender_types");

            migrationBuilder.DropTable(
                name: "time_clock_entries");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications");
        }
    }
}
