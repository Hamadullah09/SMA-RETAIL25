-- Enriches a copy of Retail25_Source (Retail25's demo seed: location TST, station 001, 136 products, 1,152 serialized
-- units, 8 departments, 17 categories, 7 tenders, 1 admin user, 5 roles) with what the seed lacks, so that every mapping
-- of the cutover is exercised: a second location with a clashing station code, branding, taxes past and future, a
-- foreign currency, suppliers, matrix, kit, serialized, gift card and case items, customers with accounts, points and
-- clashing numbers, users with Identity v3 password hashes (passwords Cashier-2026, Manager-2026, Clerk-2026), a custom
-- role, sales (split tender, on account, voided with Retail25's reversal, a return, a foreign currency, an add-on charge,
-- a serialized unit, a gift card sold, a practice sale, one after midnight that belongs to the day before), invoices,
-- layaways, quotes, gift cards and certificates, loyalty, purchase orders, customer orders, transfers, commission rules,
-- the time clock, and history that is not carried over (a stock count in progress, a closed drawer).
-- The rows follow Retail25's own rules (its entity configurations): enum columns stored as names or numbers as Retail25
-- stores them, balances equal to their ledgers (except one customer's points, on purpose), stock levels by location.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;

DECLARE @png varbinary(max) = (SELECT TOP 1 content FROM product_images ORDER BY id);
DECLARE @webp varbinary(max) = 0x524946461A000000574542505650384C0D0000002F0000000700000000000000;

-- Locations and stations ------------------------------------------------------------------------------------------------
UPDATE locations SET time_zone_id = 'America/Toronto', business_day_start = '04:00', address_line1 = '12 Main Street',
    address_city = 'Toronto', address_state_or_province = 'ON', address_postal_code = 'M5V 2T6', address_country = 'Canada',
    contact_phone = '416-555-0100', contact_email = 'shop@corner.test' WHERE id = 1;

SET IDENTITY_INSERT locations ON;
INSERT locations (id, name, legacy_code, address_line1, address_city, address_state_or_province, address_country, contact_phone,
    time_zone_id, business_day_start, base_currency_code, is_active, is_deleted, created_at, row_version)
VALUES (2, 'North Store', 'NTH', '400 Lakeshore Road', 'Barrie', 'ON', 'Canada', '705-555-0123', 'America/Toronto', '00:00', 'CAD', 1, 0, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT locations OFF;

SET IDENTITY_INSERT stations ON;
INSERT stations (id, station_code, location_id, name, reader_mode, is_active, created_at, row_version)
VALUES (2, '001', 2, 'North counter', 'OnDemand', 1, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT stations OFF;

-- Business profile, branding, policies, taxes, currencies, tenders --------------------------------------------------------
SET IDENTITY_INSERT business_profiles ON;
INSERT business_profiles (id, location_id, business_name, address_line1, address_city, address_state_or_province, address_postal_code,
    address_country, contact_phone, contact_email, contact_website, licence_number, tax_registration_number, created_at, row_version)
VALUES (1, 1, 'Corner Grocery Ltd', '12 Main Street', 'Toronto', 'ON', 'M5V 2T6', 'Canada', '416-555-0100', 'shop@corner.test', 'corner.test', 'LIC-77', '123456789 RT0001', '2026-01-01T12:00:00+00:00', 0),
       (2, 2, 'Corner Grocery North', '400 Lakeshore Road', 'Barrie', 'ON', 'L4N 1A1', 'Canada', '705-555-0123', NULL, NULL, NULL, NULL, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT business_profiles OFF;

SET IDENTITY_INSERT branding_assets ON;
INSERT branding_assets (id, location_id, slot, content, content_type, e_tag, opacity_pct, created_at, row_version)
VALUES (1, 1, 'CompanyLogo', @png, 'image/png', 'logo1', 100, '2026-01-02T12:00:00+00:00', 0),
       (2, 1, 'Watermark', @png, 'image/png', 'mark1', 12, '2026-01-02T12:00:00+00:00', 0),
       (3, 2, 'CompanyLogo', @webp, 'image/webp', 'logo2', 100, '2026-01-11T12:00:00+00:00', 0);
SET IDENTITY_INSERT branding_assets OFF;

SET IDENTITY_INSERT pos_policies ON;
INSERT pos_policies (id, location_id, apply_tax1, apply_tax2, allow_tax_override, apply_add_on_charge, fast_scan_mode, auto_save_sales,
    confirm_before_saving_sales, scan_random_weight_barcodes, staff_may_discount, allow_item_list_edit, track_staff_sales,
    require_supervisor_to_void, use_employee_time_clock, print_credit_card_signature_line, print_client_name_on_sales_slip,
    carry_over_city_state_zip, abandoned_cart_timeout_minutes, created_at, row_version)
VALUES (2, 2, 1, 0, 0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 720, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT pos_policies OFF;

UPDATE loyalty_policies SET is_enabled = 1 WHERE location_id = 1;
SET IDENTITY_INSERT loyalty_policies ON;
INSERT loyalty_policies (id, location_id, is_enabled, points_per_dollar, minimum_required, percent_enabled, reward_percent, fixed_enabled,
    reward_fixed_amount, suppress_if_subtotal_discount_applied, created_at, row_version)
VALUES (2, 2, 0, 1, 500, 0, 0, 1, 5.00, 0, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT loyalty_policies OFF;

SET IDENTITY_INSERT late_charge_policies ON;
INSERT late_charge_policies (id, location_id, monthly_rate, grace_period_days, is_enabled, created_at, row_version)
VALUES (1, 1, 1.5, 30, 1, '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT late_charge_policies OFF;

SET IDENTITY_INSERT tax_configurations ON;
INSERT tax_configurations (id, location_id, effective_from, effective_to, tax1enabled, tax1name, tax1rate, tax2enabled, tax2name, tax2rate,
    tax2compound, add_on_charge_enabled, add_on_charge_name, add_on_charge_rate, add_on_charge_taxable, taxation_type, registration_number, created_at, row_version)
VALUES (2, 1, '2027-01-01', NULL, 1, 'GST', 5, 1, 'PST', 8, 0, 0, 'Service', 0, 0, 'Exclusive', NULL, '2026-10-01T12:00:00+00:00', 0),
       (3, 2, '2025-01-01', NULL, 1, 'HST', 13, 0, 'None', 0, 0, 0, 'Service', 0, 0, 'Exclusive', '987654321 RT0001', '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT tax_configurations OFF;

SET IDENTITY_INSERT currencies ON;
INSERT currencies (id, code, name, symbol, scale, rounding, minimum_tender, is_base_currency, is_active, exchange_rate, exchange_rate_updated_at, created_at, row_version)
VALUES (2, 'USD', 'US Dollar', 'US$', 2, 0, 0.01, 0, 1, 0.7300, '2026-10-01T12:00:00+00:00', '2026-01-01T12:00:00+00:00', 0),
       (3, 'EUR', 'Euro', '€', 2, 0, 0.01, 0, 0, 0.6600, NULL, '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT currencies OFF;

SET IDENTITY_INSERT tender_types ON;
INSERT tender_types (id, code, display_name, behaviour, sort_order, icon_key, opens_cash_drawer, allows_over_tender, rounds_to_minimum_tender,
    counts_towards_drawer_cash, requires_reference, prints_signature_copy, allowed_for_refunds, currency_code, is_active, is_deleted, created_at, row_version)
VALUES (8, 'USCASH', 'US cash', 0, 15, 'banknote', 1, 1, 1, 1, 0, 0, 1, 'USD', 1, 0, '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT tender_types OFF;

-- Departments, categories, suppliers ----------------------------------------------------------------------------------------
SET IDENTITY_INSERT departments ON;
INSERT departments (id, location_id, name, code, sort_order, is_active, is_deleted, created_at, row_version)
VALUES (9, 2, 'Grocery', 'GROC', 10, 1, 0, '2026-01-10T12:00:00+00:00', 0),
       (10, 2, 'Garden centre supplies', 'GARD', 20, 1, 0, '2026-01-10T12:00:00+00:00', 0),
       (11, 1, 'Discontinued', 'DISC', 90, 0, 1, '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT departments OFF;

SET IDENTITY_INSERT categories ON;
INSERT categories (id, location_id, name, code, sort_order, is_active, is_deleted, created_at, row_version)
VALUES (18, 2, 'Hoses and watering', 'GARD-HOSE', 10, 1, 0, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT categories OFF;

SET IDENTITY_INSERT suppliers ON;
INSERT suppliers (id, location_id, supplier_number, company, contact_first_name, contact_last_name, address_line1, address_city,
    address_state_or_province, address_country, contact_phone, contact_email, is_deleted, deleted_at, created_at, row_version)
VALUES (1, 1, 'S1', 'Prairie Foods Wholesale', 'Pat', 'Nguyen', '9 Mill Road', 'Winnipeg', 'MB', 'Canada', '204-555-0101', 'orders@prairie.test', 0, NULL, '2026-01-01T12:00:00+00:00', 0),
       (2, 1, 'S2', 'Northern Hardware Supply Company Limited', 'Lee', 'Morgan', NULL, 'Sudbury', 'ON', 'Canada', '705-555-0202', NULL, 0, NULL, '2026-01-01T12:00:00+00:00', 0),
       (3, 2, 'S1', 'Green Thumb Distributors', 'Ana', 'Silva', NULL, 'Guelph', 'ON', 'Canada', '519-555-0303', NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
       (4, 1, 'S9', 'Closed Supplier Inc', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, '2026-05-01T12:00:00+00:00', '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT suppliers OFF;

-- Products of the North store, and a gift card product at TST -----------------------------------------------------------
SET IDENTITY_INSERT products ON;
INSERT products (id, location_id, stock_code, name, description, type, upc, tax1applies, tax2applies, regular_price, last_cost, avg_cost,
    gross_margin_pct, base_stock, reorder_point, reorder_qty, on_hand, on_order, case_qty, ship_weight, bin_location, pos_message,
    invoice_message, notes, has_image, department_id, category_id, substitute_product_id, tag_along_product_id, parent_product_id,
    is_deleted, deleted_at, created_at, row_version)
VALUES
 (201, 2, 'DEMO-0001', 'Long grain rice 1kg', NULL, 0, '2500000000010', 1, 0, 24.50, 18.00, 18.10, 26.12, 20, 10, 24, 40, 0, 12, 1.1, 'A1', NULL, NULL, NULL, 0, 9, 1, NULL, NULL, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (202, 2, 'GARDEN-HOSE-25M-GREEN', 'Garden hose 25 m green', 'Kink-free, with brass fittings', 0, 'HOSE25G', 1, 0, 39.99, 22.00, 21.50, 46.24, 10, 5, 12, 15, 12, 1, 3.2, 'G4', 'Ask about the reel', NULL, NULL, 0, 10, 18, NULL, NULL, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (203, 2, 'TSHIRT-CREW', 'Crew neck T-shirt', NULL, 1, NULL, 1, 0, 19.99, 7.40, 7.50, 62.48, 0, 0, 0, 12, 0, 1, 0.2, NULL, NULL, NULL, NULL, 1, 9, 14, NULL, NULL, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (204, 2, 'GIFTBASKET', 'Picnic gift basket', NULL, 3, NULL, 1, 0, 49.99, 30.00, 30.00, 39.99, 0, 0, 0, 2, 0, 1, 2.5, NULL, NULL, 'Contents may vary', NULL, 0, 9, NULL, NULL, 205, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (205, 2, 'JUICE-1L', 'Apple juice 1 L', NULL, 0, '0062639301234', 1, 0, 4.49, 2.20, 2.20, 51.00, 24, 12, 24, 48, 0, 1, 1.05, NULL, NULL, NULL, NULL, 0, 9, 7, 201, NULL, 206, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (206, 2, 'JUICE-1L-CASE', 'Apple juice 1 L case of 12', NULL, 0, NULL, 1, 0, 45.00, 26.40, 26.40, 41.33, 2, 1, 4, 6, 0, 12, 12.6, NULL, NULL, NULL, NULL, 0, 9, 7, NULL, NULL, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (207, 2, 'GIFTCARD', 'Gift card', NULL, 9, NULL, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (208, 2, 'OLD-STOCK', 'Old stock item', NULL, 0, NULL, 1, 0, 1.00, 0.50, 0.50, 50, 0, 0, 0, 0, 0, 1, 0, NULL, NULL, NULL, NULL, 0, 9, NULL, NULL, NULL, NULL, 1, '2026-06-01T12:00:00+00:00', '2026-01-10T12:00:00+00:00', 0),
 (209, 2, 'SER-DRILL', 'Cordless drill 18V', NULL, 2, '0012345678905', 1, 0, 129.00, 80.00, 80.00, 37.98, 2, 1, 2, 2, 0, 1, 2.0, 'H2', NULL, NULL, NULL, 0, 9, 16, NULL, NULL, NULL, 0, NULL, '2026-01-10T12:00:00+00:00', 0),
 (210, 1, 'GIFTCARD', 'Gift card', NULL, 9, NULL, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL, NULL, 0, NULL, '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT products OFF;

SET IDENTITY_INSERT product_prices ON;
INSERT product_prices (id, product_id, level, price, row_version)
VALUES (901, 201, 2, 23.00, 0), (902, 201, 3, 22.00, 0), (903, 201, 4, 21.00, 0);
SET IDENTITY_INSERT product_prices OFF;

SET IDENTITY_INSERT price_breaks ON;
INSERT price_breaks (id, product_id, level, min_quantity, row_version) VALUES (1, 201, 2, 10, 0), (2, 201, 3, 25, 0), (3, 201, 4, 50, 0);
SET IDENTITY_INSERT price_breaks OFF;

SET IDENTITY_INSERT sale_pricings ON;
INSERT sale_pricings (id, product_id, discount_pct, starts_on, ends_on, row_version)
VALUES (1, 202, 15, '2026-06-01', '2026-07-31', 0), (2, 202, 20, '2026-10-01', '2026-12-31', 0);
SET IDENTITY_INSERT sale_pricings OFF;

SET IDENTITY_INSERT bonus_pricings ON;
INSERT bonus_pricings (id, product_id, buy_qty, free_qty, row_version) VALUES (1, 205, 3, 1, 0);
SET IDENTITY_INSERT bonus_pricings OFF;

SET IDENTITY_INSERT product_suppliers ON;
INSERT product_suppliers (id, product_id, supplier_id, rank, cost, reorder_number, case_qty, minimum_order_qty, created_at, row_version)
VALUES (1, 201, 3, 1, 17.500, 'GT-RICE', 12, 24, '2026-01-10T12:00:00+00:00', 0),
       (2, 201, 1, 2, 18.200, 'PF-0001', 12, 12, '2026-01-10T12:00:00+00:00', 0),
       (3, 202, 3, 1, 21.000, 'GT-HOSE25', 6, 6, '2026-01-10T12:00:00+00:00', 0),
       (4, 1, 1, 1, 17.900, 'PF-0001', 12, 12, '2026-01-01T12:00:00+00:00', 0),
       (5, 135, 2, 1, 9.750, 'NH-SCR-200', 10, 10, '2026-01-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT product_suppliers OFF;

SET IDENTITY_INSERT kit_components ON;
INSERT kit_components (id, kit_product_id, component_product_id, quantity, reduce_stock, row_version)
VALUES (1, 204, 201, 1, 1, 0), (2, 204, 205, 2, 0, 0);
SET IDENTITY_INSERT kit_components OFF;

SET IDENTITY_INSERT matrix_dimensions ON;
INSERT matrix_dimensions (id, product_id, position, name, row_version) VALUES (1, 203, 1, 'Colour', 0), (2, 203, 2, 'Size', 0);
SET IDENTITY_INSERT matrix_dimensions OFF;

SET IDENTITY_INSERT product_variants ON;
INSERT product_variants (id, product_id, dim1value, dim2value, dim3value, variant_code, upc, on_hand, is_active, created_at, row_version)
VALUES (2031, 203, 'Red', 'M', NULL, 'TSC-RED-M', '0123456789012', 5, 1, '2026-01-10T12:00:00+00:00', 0),
       (2032, 203, 'Red', 'L', NULL, 'TSC-RED-L', NULL, 3, 1, '2026-01-10T12:00:00+00:00', 0),
       (2033, 203, 'Blue', 'M', NULL, 'TSC-BLU-M', NULL, 4, 1, '2026-01-10T12:00:00+00:00', 0),
       (2034, 203, 'Blue', 'L', NULL, 'TSC-BLU-L', NULL, 0, 0, '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT product_variants OFF;

SET IDENTITY_INSERT serialized_units ON;
INSERT serialized_units (id, product_id, variant_id, serial_number, epc, state, location_id, received_on, last_seen_at, created_at, row_version)
VALUES (2001, 209, NULL, 'DR-1001', 'E2801170000002000000A001', 'InStock', 2, '2026-02-01T12:00:00+00:00', NULL, '2026-02-01T12:00:00+00:00', 0),
       (2002, 209, NULL, 'DR-1002', 'E2801170000002000000A002', 'InStock', 2, '2026-02-01T12:00:00+00:00', NULL, '2026-02-01T12:00:00+00:00', 0),
       (2003, 209, NULL, 'DR-1003', 'E2801170000002000000A003', 'Sold', 2, '2026-02-01T12:00:00+00:00', '2026-09-30T15:00:00+00:00', '2026-02-01T12:00:00+00:00', 0),
       (2004, 209, NULL, 'DR-1004', NULL, 'Lost', 2, '2026-02-01T12:00:00+00:00', NULL, '2026-02-01T12:00:00+00:00', 0),
       (2005, 209, NULL, 'DR-1005', 'E2801170000002000000A005', 'Provisioned', 2, '2026-02-01T12:00:00+00:00', NULL, '2026-02-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT serialized_units OFF;
-- The cable sold on sale 9008.
UPDATE serialized_units SET state = 'Sold', last_seen_at = '2026-10-22T17:00:00+00:00' WHERE id = 5;

SET IDENTITY_INSERT product_images ON;
INSERT product_images (id, product_id, content, content_type, e_tag, created_at, row_version)
VALUES (901, 203, @png, 'image/png', 'img203', '2026-01-10T12:00:00+00:00', 0),
       (902, 202, @webp, 'image/webp', 'img202', '2026-01-10T12:00:00+00:00', 0);
SET IDENTITY_INSERT product_images OFF;

SET IDENTITY_INSERT stock_levels ON;
INSERT stock_levels (id, product_id, variant_id, location_id, on_hand, on_order, committed, last_sold_on, row_version)
VALUES (901, 201, NULL, 2, 40, 0, 0, '2026-10-21T13:00:00+00:00', 0),
       (902, 202, NULL, 2, 15, 12, 0, NULL, 0),
       (903, 203, 2031, 2, 5, 0, 0, NULL, 0),
       (904, 203, 2032, 2, 3, 0, 1, NULL, 0),
       (905, 203, 2033, 2, 4, 0, 0, NULL, 0),
       (906, 203, 2034, 2, 0, 0, 0, NULL, 0),
       (907, 204, NULL, 2, 2, 0, 0, NULL, 0),
       (908, 205, NULL, 2, 48, 0, 0, NULL, 0),
       (909, 206, NULL, 2, 6, 0, 0, NULL, 0),
       (910, 208, NULL, 2, 0, 0, 0, NULL, 0),
       (911, 209, NULL, 2, 2, 0, 0, NULL, 0);
SET IDENTITY_INSERT stock_levels OFF;
-- Goods on TST's open layaway 1 are committed.
UPDATE stock_levels SET committed = 2 WHERE product_id = 50 AND variant_id IS NULL;
UPDATE stock_levels SET committed = 1 WHERE product_id = 51 AND variant_id IS NULL;

-- Customers --------------------------------------------------------------------------------------------------------------
SET IDENTITY_INSERT customers ON;
INSERT customers (id, location_id, customer_number, first_name, last_name, company, title, billing_address_line1, billing_address_city,
    billing_address_state_or_province, billing_address_postal_code, billing_address_country, ship_to_address_line1, ship_to_address_city,
    ship_to_address_state_or_province, ship_to_address_postal_code, contact_phone, contact_mobile, contact_email, client_type, birthday,
    notes, last_purchase_on, last_mailing_on, is_deleted, deleted_at, created_at, row_version)
VALUES
 (1, 1, 1001, 'Anna', 'Bergström-Lindqvist', NULL, 'Ms', '5 Elm Street', 'Toronto', 'ON', 'M4C 1A1', 'Canada', '77 Dock Road', 'Toronto', 'ON', 'M4C 9Z9',
     '416-555-0111', '647-555-0111', 'anna@mail.test', 'RETAIL', '1985-04-12', 'Prefers email receipts', '2026-10-22', '2026-09-01', 0, NULL, '2026-02-01T12:00:00+00:00', 0),
 (2, 1, 1002, 'Ben', 'Okafor', 'Okafor Catering Services Incorporated', NULL, '88 King Street W', 'Toronto', 'ON', 'M5H 1A1', 'Canada', NULL, NULL,
     NULL, NULL, '416-555-0122', NULL, 'ben@okafor.test', 'TRADE', NULL, NULL, '2026-10-22', NULL, 0, NULL, '2026-02-01T12:00:00+00:00', 0),
 (3, 1, 1003, 'Carla', 'Diaz', NULL, NULL, '3 Pine Avenue', 'Toronto', 'ON', NULL, 'Canada', NULL, NULL,
     NULL, NULL, '416-555-0133', NULL, NULL, 'RETAIL', NULL, NULL, '2026-10-20', NULL, 0, NULL, '2026-02-01T12:00:00+00:00', 0),
 (4, 1, 1004, 'Dan', 'Old', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, '2026-03-01T12:00:00+00:00', '2026-02-01T12:00:00+00:00', 0),
 (5, 2, 1001, 'Erin', 'North', NULL, NULL, '1 Bay Street', 'Barrie', 'ON', NULL, 'Canada', NULL, NULL,
     NULL, NULL, '705-555-0155', NULL, NULL, 'RETAIL', NULL, NULL, '2026-10-22', NULL, 0, NULL, '2026-02-01T12:00:00+00:00', 0),
 (6, 2, 1002, 'Femi', 'Adeyemi', NULL, NULL, NULL, 'Barrie', 'ON', NULL, 'Canada', NULL, NULL,
     NULL, NULL, '705-555-0166', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, NULL, '2026-02-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT customers OFF;

SET IDENTITY_INSERT customer_accounts ON;
INSERT customer_accounts (id, customer_id, account_number, credit_limit, balance_due, created_at, row_version)
VALUES (1, 2, 7001, 2000.00, 84.40, '2026-02-01T12:00:00+00:00', 0),
       (2, 5, 7001, 0, 0, '2026-02-01T12:00:00+00:00', 0),
       (3, 1, 7002, 500.00, 0, '2026-02-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT customer_accounts OFF;

SET IDENTITY_INSERT customer_pricing_profiles ON;
INSERT customer_pricing_profiles (id, customer_id, usual_discount_pct, price_level, exempt_tax1, exempt_tax2, reward_points, created_at, row_version)
VALUES (1, 3, 5, 3, 0, 1, 640, '2026-02-01T12:00:00+00:00', 0),
       (2, 1, 0, 1, 0, 0, 120, '2026-02-01T12:00:00+00:00', 0),
       (3, 6, 0, 1, 0, 0, 0, '2026-02-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT customer_pricing_profiles OFF;

-- Carla's ledger adds up to her 640 points; Anna's says 125 where her profile says 120 (the profile's balance is carried).
SET IDENTITY_INSERT loyalty_ledger_entries ON;
INSERT loyalty_ledger_entries (id, customer_id, transaction_id, entry_type, points, occurred_at, row_version)
VALUES (1, 3, NULL, 0, 647, '2026-09-15T15:00:00+00:00', 0), (2, 3, NULL, 1, -60, '2026-10-01T15:00:00+00:00', 0),
       (3, 3, 9002, 0, 53, '2026-10-20T18:00:00+00:00', 0), (4, 1, NULL, 0, 100, '2026-08-01T15:00:00+00:00', 0),
       (5, 1, NULL, 3, 25, '2026-08-02T15:00:00+00:00', 0);
SET IDENTITY_INSERT loyalty_ledger_entries OFF;

-- Users, roles and staff -------------------------------------------------------------------------------------------------
SET IDENTITY_INSERT AspNetRoles ON;
INSERT AspNetRoles (id, legacy_level, description, name, normalized_name, concurrency_stamp)
VALUES (6, 3, 'Runs one shop day to day', 'Store manager', 'STORE MANAGER', 'b6f2c1e0-0000-4000-8000-000000000006');
SET IDENTITY_INSERT AspNetRoles OFF;

SET IDENTITY_INSERT role_permissions ON;
INSERT role_permissions (id, role_id, permission_key, row_version)
VALUES (901, 6, 'pos.sell', 0), (902, 6, 'pos.discount', 0), (903, 6, 'pos.void_sale', 0), (904, 6, 'pos.return', 0),
       (905, 6, 'drawer.close', 0), (906, 6, 'drawer.pop', 0), (907, 6, 'catalog.read', 0), (908, 6, 'catalog.write', 0),
       (909, 6, 'inventory.adjust', 0), (910, 6, 'inventory.count', 0), (911, 6, 'purchasing.read', 0), (912, 6, 'purchasing.write', 0),
       (913, 6, 'customer.read', 0), (914, 6, 'customer.write', 0), (915, 6, 'ar.payment', 0), (916, 6, 'reports.sales', 0),
       (917, 6, 'reports.hours', 0), (918, 6, 'staff.time_clock', 0), (919, 6, 'staff.time_clock_edit', 0), (920, 6, 'settings.read', 0),
       (921, 6, 'loyalty.adjust', 0);
SET IDENTITY_INSERT role_permissions OFF;

SET IDENTITY_INSERT AspNetUsers ON;
INSERT AspNetUsers (id, display_name, is_enabled, default_location_id, last_signed_in_at, user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp, phone_number_confirmed, two_factor_enabled, lockout_end, lockout_enabled, access_failed_count)
VALUES
 (2, 'Casey Cashier', 1, 1, '2026-10-22T13:00:00+00:00', 'cashier1@corner.test', 'CASHIER1@CORNER.TEST', 'cashier1@corner.test', 'CASHIER1@CORNER.TEST', 1,
     'AQAAAAIAAYagAAAAEAagRNCF5ERwKbFL/4V3AjpKfU1S1BYMH5XJGGGgZpFXEs1h8NBIEsQrN4+G6osmUA==', 'S2', 'C2', 0, 0, NULL, 1, 0),
 (3, 'Morgan Manager', 1, 1, NULL, 'manager1@corner.test', 'MANAGER1@CORNER.TEST', 'manager1@corner.test', 'MANAGER1@CORNER.TEST', 1,
     'AQAAAAIAAYagAAAAECzDeE+o+y9HqQbZ7yvBHt6QVUTmEQSF+3DbmvFV9dsqHSk4x6nB6kzeoHs5H3iutg==', 'S3', 'C3', 0, 0, NULL, 1, 0),
 (4, 'Kim Clerk', 1, 2, NULL, 'clerk.north@corner.test', 'CLERK.NORTH@CORNER.TEST', 'clerk.north@corner.test', 'CLERK.NORTH@CORNER.TEST', 1,
     'AQAAAAIAAYagAAAAENz/IbnJMDS4j3Qk+3i1vXB3ruklkkyrkqqm8gTyTev2jvGqjx2O3DCuQjN4l53YyQ==', 'S4', 'C4', 0, 0, NULL, 1, 0),
 (5, 'Former Staff', 0, 1, NULL, 'former@corner.test', 'FORMER@CORNER.TEST', 'former@corner.test', 'FORMER@CORNER.TEST', 1,
     'AQAAAAIAAYagAAAAEAagRNCF5ERwKbFL/4V3AjpKfU1S1BYMH5XJGGGgZpFXEs1h8NBIEsQrN4+G6osmUA==', 'S5', 'C5', 0, 0, '2026-01-01T00:00:00+00:00', 1, 3);
SET IDENTITY_INSERT AspNetUsers OFF;

INSERT AspNetUserRoles (user_id, role_id) VALUES (2, 2), (3, 6), (4, 3), (5, 2);

SET IDENTITY_INSERT staff_profiles ON;
INSERT staff_profiles (id, user_id, staff_code, first_name, last_name, pin_hash, failed_pin_attempts, pin_locked_until, access_level, is_active, created_at, row_version)
VALUES (2, 2, 'C01', 'Casey', 'Cashier', 'argon2id$4$65536$2$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=', 0, NULL, 1, 1, '2026-02-01T12:00:00+00:00', 0),
       (3, 3, 'M01', 'Morgan', 'Manager', NULL, 0, NULL, 3, 1, '2026-02-01T12:00:00+00:00', 0),
       (4, 4, 'K01', 'Kim', 'Clerk', 'argon2id$4$65536$2$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=', 0, NULL, 2, 1, '2026-02-01T12:00:00+00:00', 0),
       (5, 5, 'X99', 'Former', 'Staff', NULL, 0, NULL, 1, 0, '2026-02-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT staff_profiles OFF;

-- Sales -----------------------------------------------------------------------------------------------------------------
-- TST is in Toronto (UTC-4 in October) and its business day starts at 04:00; sale 9006 at 02:30 belongs to the 21st.
SET IDENTITY_INSERT sales_transactions ON;
INSERT sales_transactions (id, transaction_number, location_id, station_id, staff_id, customer_id, drawer_session_id, business_date, subtotal,
    discount_total, add_on_charge_total, tax1total, tax2total, grand_total, rounding_adjustment, change_given, cost_of_goods_sold,
    loyalty_points_earned, loyalty_points_redeemed, status, is_training, voided_by_transaction_id, reverses_transaction_id, void_reason,
    void_approved_by_staff_id, invoice_id, reprint_count, completed_at, created_at, row_version)
VALUES
 (9001, 1, 1, 1, 2, NULL, 1, '2026-10-20', 63.85, 0, 0, 3.19, 4.47, 71.51, -0.01, 8.50, 47.83, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-20T15:10:00+00:00', '2026-10-20T15:10:00+00:00', 0),
 (9002, 2, 1, 1, 2, 3, 1, '2026-10-20', 56.39, 2.82, 0, 2.68, 0, 56.25, 0, 0, 42.49, 53, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-20T18:00:00+00:00', '2026-10-20T18:00:00+00:00', 0),
 (9003, 3, 1, 1, 2, 2, NULL, '2026-10-21', 120.00, 0, 0, 6.00, 8.40, 134.40, 0, 0, 105.80, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, 1, 0, '2026-10-21T14:00:00+00:00', '2026-10-21T14:00:00+00:00', 0),
 (9004, 4, 1, 1, 2, NULL, NULL, '2026-10-21', 22.95, 0, 0, 1.15, 1.61, 25.71, 0, 4.29, 17.35, 0, 0, 'Voided', 0, 9005, NULL, 'Rang up twice', 3, NULL, 0, '2026-10-21T16:00:00+00:00', '2026-10-21T16:00:00+00:00', 0),
 (9005, 5, 1, 1, 3, NULL, NULL, '2026-10-21', -22.95, 0, 0, -1.15, -1.61, -25.71, 0, 0, -17.35, 0, 0, 'Reversal', 0, NULL, 9004, 'Rang up twice', 3, NULL, 0, '2026-10-21T16:05:00+00:00', '2026-10-21T16:05:00+00:00', 0),
 (9006, 6, 1, 1, 2, NULL, NULL, '2026-10-21', 34.98, 0, 0, 1.75, 2.45, 39.18, 0, 0, 23.28, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-22T06:30:00+00:00', '2026-10-22T06:30:00+00:00', 0),
 (9007, 7, 1, 1, 2, 1, NULL, '2026-10-22', -16.46, 0, 0, -0.83, -1.16, -18.45, 0, 0, -13.02, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-22T15:00:00+00:00', '2026-10-22T15:00:00+00:00', 0),
 (9008, 8, 1, 1, 3, NULL, NULL, '2026-10-22', 10.95, 0, 1.00, 0.55, 0.77, 13.27, -0.02, 1.75, 8.67, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-22T17:00:00+00:00', '2026-10-22T17:00:00+00:00', 0),
 (9009, 9, 1, 1, 2, 2, NULL, '2026-10-22', 50.00, 0, 0, 0, 0, 50.00, 0, 0, 0, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-22T18:00:00+00:00', '2026-10-22T18:00:00+00:00', 0),
 (9010, 10, 1, 1, 2, NULL, NULL, '2026-10-20', 23.95, 0, 0, 1.20, 1.68, 26.83, 0, 0, 17.99, 0, 0, 'Completed', 1, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-20T19:00:00+00:00', '2026-10-20T19:00:00+00:00', 0),
 (9011, 1, 2, 2, 4, NULL, NULL, '2026-10-21', 49.00, 0, 0, 6.37, 0, 55.37, 0, 4.63, 36.20, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, NULL, 0, '2026-10-21T13:00:00+00:00', '2026-10-21T13:00:00+00:00', 0),
 (9012, 2, 2, 2, 4, 5, NULL, '2026-10-22', 19.99, 0, 0, 2.60, 0, 22.59, 0, 0, 7.50, 0, 0, 'Completed', 0, NULL, NULL, NULL, NULL, 2, 0, '2026-10-22T13:00:00+00:00', '2026-10-22T13:00:00+00:00', 0);
SET IDENTITY_INSERT sales_transactions OFF;

SET IDENTITY_INSERT sale_lines ON;
INSERT sale_lines (id, transaction_id, sequence, product_id, variant_id, serialized_unit_id, epc, serial_number, stock_code_snapshot, name_snapshot,
    source, quantity, chargeable_quantity, unit_price, discount_pct, extended_net, prorated_adjustment, taxable_net, tax1applies, tax2applies,
    tax1amount, tax2amount, unit_cost_snapshot, price_origin, line_type, returned_to_stock, note, row_version, refunds_sale_line_id)
VALUES
 (90001, 9001, 1, 1, NULL, NULL, NULL, NULL, 'DEMO-0001', 'Long grain rice 1kg', 'Barcode', 2, 2, 23.95, 0, 47.90, 0, 47.90, 1, 1, 2.40, 3.35, 17.99, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90002, 9001, 2, 5, NULL, NULL, NULL, NULL, 'DEMO-0005', 'Self-raising oats 1kg', 'Barcode', 1, 1, 15.95, 0, 15.95, 0, 15.95, 1, 1, 0.80, 1.12, 11.85, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90003, 9002, 1, 2, NULL, NULL, NULL, NULL, 'DEMO-0002', 'Basmati flour 1.5kg', 'Barcode', 1, 1, 16.49, 0, 16.49, 0.82, 15.67, 1, 1, 0.78, 0, 12.37, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90004, 9002, 2, 3, NULL, NULL, NULL, NULL, 'DEMO-0003', 'Wholemeal barley 500g', 'StockCode', 2, 2, 19.95, 0, 39.90, 2.00, 37.90, 1, 1, 1.90, 0, 15.06, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90005, 9003, 1, 10, NULL, NULL, NULL, NULL, 'DEMO-0010', 'Whole beans 415g', 'Manual', 10, 10, 12.00, 0, 120.00, 0, 120.00, 1, 1, 6.00, 8.40, 10.58, 'Manual', 'Sale', 0, 'Trade price', 0, NULL),
 (90006, 9004, 1, 20, NULL, NULL, NULL, NULL, 'DEMO-0020', 'Dark chocolate 90g', 'Barcode', 1, 1, 22.95, 0, 22.95, 0, 22.95, 1, 1, 1.15, 1.61, 17.35, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90007, 9005, 1, 20, NULL, NULL, NULL, NULL, 'DEMO-0020', 'Dark chocolate 90g', 'Barcode', 1, 1, 22.95, 0, -22.95, 0, -22.95, 1, 1, -1.15, -1.61, 17.35, 'Regular', 'Return', 1, NULL, 0, 90006),
 (90008, 9006, 1, 30, NULL, NULL, NULL, NULL, 'DEMO-0030', 'Hass avocado each', 'Barcode', 2, 2, 17.49, 0, 34.98, 0, 34.98, 1, 1, 1.75, 2.45, 11.64, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90009, 9007, 1, 1, NULL, NULL, NULL, NULL, 'DEMO-0001', 'Long grain rice 1kg', 'Barcode', 1, 1, 23.95, 0, -23.95, 0, -23.95, 1, 1, -1.20, -1.68, 17.99, 'Regular', 'Return', 1, NULL, 0, NULL),
 (90010, 9007, 2, 40, NULL, NULL, NULL, NULL, 'DEMO-0040', 'Truss tomatoes 500g', 'Barcode', 1, 1, 7.49, 0, 7.49, 0, 7.49, 1, 1, 0.37, 0.52, 4.97, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90011, 9008, 1, 89, NULL, 5, '3076452148003E8000000005', 'SN00000005', 'DEMO-0089', 'Braided USB-C cable 2m', 'Rfid', 1, 1, 10.95, 0, 10.95, 0, 10.95, 1, 1, 0.55, 0.77, 8.67, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90012, 9009, 1, 210, NULL, NULL, NULL, 'GC-1000-0002', 'GIFTCARD', 'Gift card', 'StockCode', 1, 1, 50.00, 0, 50.00, 0, 50.00, 0, 0, 0, 0, 0, 'Manual', 'Sale', 0, NULL, 0, NULL),
 (90013, 9010, 1, 1, NULL, NULL, NULL, NULL, 'DEMO-0001', 'Long grain rice 1kg', 'Barcode', 1, 1, 23.95, 0, 23.95, 0, 23.95, 1, 1, 1.20, 1.68, 17.99, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90014, 9011, 1, 201, NULL, NULL, NULL, NULL, 'DEMO-0001', 'Long grain rice 1kg', 'Barcode', 2, 2, 24.50, 0, 49.00, 0, 49.00, 1, 0, 6.37, 0, 18.10, 'Regular', 'Sale', 0, NULL, 0, NULL),
 (90015, 9012, 1, 203, 2031, NULL, NULL, NULL, 'TSHIRT-CREW', 'Crew neck T-shirt Red M', 'Variant', 1, 1, 19.99, 0, 19.99, 0, 19.99, 1, 0, 2.60, 0, 7.50, 'Regular', 'Sale', 0, NULL, 0, NULL);
SET IDENTITY_INSERT sale_lines OFF;

SET IDENTITY_INSERT sale_adjustments ON;
INSERT sale_adjustments (id, transaction_id, type, label, amount, serial, row_version) VALUES (1, 9002, 'SubtotalDiscount', 'Client discount 5%', 2.82, NULL, 0);
SET IDENTITY_INSERT sale_adjustments OFF;

SET IDENTITY_INSERT sale_tax_snapshots ON;
INSERT sale_tax_snapshots (id, transaction_id, tax1name, tax1rate, tax2name, tax2rate, tax2compound, add_on_name, add_on_rate, add_on_taxable, tax_inclusive, tax_registration_number, row_version)
VALUES (9001, 9001, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0), (9002, 9002, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0),
       (9003, 9003, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0), (9004, 9004, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0),
       (9005, 9005, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0), (9006, 9006, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0),
       (9007, 9007, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0), (9008, 9008, 'GST', 5, 'PST', 7, 0, 'Service fee', 9.13, 0, 0, NULL, 0),
       (9009, 9009, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0), (9010, 9010, 'GST', 5, 'PST', 7, 0, 'Service', 0, 0, 0, NULL, 0),
       (9011, 9011, 'HST', 13, 'None', 0, 0, 'Service', 0, 0, 0, '987654321 RT0001', 0), (9012, 9012, 'HST', 13, 'None', 0, 0, 'Service', 0, 0, 0, '987654321 RT0001', 0);
SET IDENTITY_INSERT sale_tax_snapshots OFF;

SET IDENTITY_INSERT sale_tenders ON;
INSERT sale_tenders (id, transaction_id, tender_type_id, behaviour, amount, amount_tendered, change_given, currency_id, exchange_rate, reference, auth_code, card_last4, gateway_reference, row_version)
VALUES (1, 9001, 1, 'Cash', 71.50, 80.00, 8.50, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (2, 9002, 2, 'Card', 30.00, 30.00, 0, NULL, 1, NULL, 'A1B2C3', '4242', 'gw-1', 0),
       (3, 9002, 4, 'GiftCard', 26.25, 26.25, 0, NULL, 1, 'GC-1000-0001', NULL, NULL, NULL, 0),
       (4, 9003, 7, 'OnAccount', 134.40, 134.40, 0, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (5, 9004, 1, 'Cash', 25.71, 30.00, 4.29, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (6, 9005, 1, 'Cash', -25.71, -25.71, 0, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (7, 9006, 8, 'Cash', 39.18, 39.18, 0, 2, 0.73, NULL, NULL, NULL, NULL, 0),
       (8, 9007, 1, 'Cash', -18.45, -18.45, 0, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (9, 9008, 1, 'Cash', 13.25, 15.00, 1.75, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (10, 9009, 3, 'Card', 50.00, 50.00, 0, NULL, 1, NULL, 'D9E8F7', '1881', 'gw-2', 0),
       (11, 9010, 1, 'Cash', 26.83, 26.83, 0, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (12, 9011, 1, 'Cash', 55.37, 60.00, 4.63, NULL, 1, NULL, NULL, NULL, NULL, 0),
       (13, 9012, 7, 'OnAccount', 22.59, 22.59, 0, NULL, 1, NULL, NULL, NULL, NULL, 0);
SET IDENTITY_INSERT sale_tenders OFF;

SET IDENTITY_INSERT commission_ledger_entries ON;
INSERT commission_ledger_entries (id, staff_id, location_id, transaction_id, sale_line_id, product_id, stock_code_snapshot, department_id, commission_rule_id,
    commission_type, rate_applied, line_net, line_cost, quantity, amount, was_capped, business_date, occurred_at, row_version)
VALUES (1, 2, 1, 9001, 90001, 1, 'DEMO-0001', 1, 1, 'Percentage', 2, 47.90, 35.98, 2, 0.96, 0, '2026-10-20', '2026-10-20T15:10:00+00:00', 0),
       (2, 2, 1, 9001, 90002, 5, 'DEMO-0005', 1, 1, 'Percentage', 2, 15.95, 11.85, 1, 0.32, 0, '2026-10-20', '2026-10-20T15:10:00+00:00', 0),
       (3, 4, 2, 9011, 90014, 201, 'DEMO-0001', 9, 4, 'Percentage', 1.5, 49.00, 36.20, 2, 0.74, 0, '2026-10-21', '2026-10-21T13:00:00+00:00', 0);
SET IDENTITY_INSERT commission_ledger_entries OFF;

-- Receivables: invoice 1 (TST) is open, invoice 1 of NTH is paid; the accounts are the sums of their ledgers ---------------
SET IDENTITY_INSERT invoices ON;
INSERT invoices (id, invoice_number, customer_id, transaction_id, issued_on, due_on, invoice_total, penalty_accrued, balance_due, last_payment_on, status, staff_id, created_at, row_version)
VALUES (1, 1, 2, 9003, '2026-10-21', '2026-11-20', 134.40, 0, 84.40, '2026-10-25', 'Open', 2, '2026-10-21T14:00:00+00:00', 0),
       (2, 1, 5, 9012, '2026-10-22', '2026-11-21', 22.59, 0, 0, '2026-10-23', 'Paid', 4, '2026-10-22T13:00:00+00:00', 0);
SET IDENTITY_INSERT invoices OFF;

SET IDENTITY_INSERT invoice_payments ON;
INSERT invoice_payments (id, invoice_id, amount, applied_to_penalty, applied_to_principal, tender_type_id, paid_on, was_distributed, created_at, row_version)
VALUES (1, 1, 50.00, 0, 50.00, 6, '2026-10-25', 0, '2026-10-25T15:00:00+00:00', 0),
       (2, 2, 22.59, 0, 22.59, 3, '2026-10-23', 1, '2026-10-23T15:00:00+00:00', 0);
SET IDENTITY_INSERT invoice_payments OFF;

SET IDENTITY_INSERT ar_ledger_entries ON;
INSERT ar_ledger_entries (id, customer_id, invoice_id, entry_type, amount, occurred_at, row_version)
VALUES (1, 2, 1, 0, 134.40, '2026-10-21T14:00:00+00:00', 0), (2, 2, 1, 1, -50.00, '2026-10-25T15:00:00+00:00', 0),
       (3, 5, 2, 0, 22.59, '2026-10-22T13:00:00+00:00', 0), (4, 5, 2, 1, -22.59, '2026-10-23T15:00:00+00:00', 0);
SET IDENTITY_INSERT ar_ledger_entries OFF;

-- Layaways: one open at each store, one paid in full and one cancelled --------------------------------------------------
SET IDENTITY_INSERT layaways ON;
INSERT layaways (id, layaway_number, customer_id, location_id, status, total, amount_paid, created_on, staff_id, created_at, row_version)
VALUES (1, 1, 1, 1, 0, 65.93, 30.00, '2026-10-19', 2, '2026-10-19T15:00:00+00:00', 0),
       (2, 2, 3, 1, 1, 26.49, 26.49, '2026-09-10', 2, '2026-09-10T15:00:00+00:00', 0),
       (3, 3, 1, 1, 2, 2.95, 0, '2026-09-12', 2, '2026-09-12T15:00:00+00:00', 0),
       (4, 1, 6, 2, 0, 19.99, 5.00, '2026-10-20', 4, '2026-10-20T15:00:00+00:00', 0);
SET IDENTITY_INSERT layaways OFF;

SET IDENTITY_INSERT layaway_lines ON;
INSERT layaway_lines (id, layaway_id, product_id, variant_id, quantity, unit_price, row_version)
VALUES (1, 1, 50, NULL, 2, 22.49, 0), (2, 1, 51, NULL, 1, 20.95, 0), (3, 2, 60, NULL, 1, 26.49, 0), (4, 3, 71, NULL, 1, 2.95, 0),
       (5, 4, 203, 2032, 1, 19.99, 0);
SET IDENTITY_INSERT layaway_lines OFF;

SET IDENTITY_INSERT layaway_payments ON;
INSERT layaway_payments (id, layaway_id, amount, tender_type_id, paid_on, created_at, row_version)
VALUES (1, 1, 20.00, 1, '2026-10-19', '2026-10-19T15:00:00+00:00', 0), (2, 1, 10.00, 1, '2026-10-24', '2026-10-24T15:00:00+00:00', 0),
       (3, 2, 26.49, 2, '2026-09-30', '2026-09-30T15:00:00+00:00', 0), (4, 4, 5.00, 1, '2026-10-20', '2026-10-20T15:00:00+00:00', 0);
SET IDENTITY_INSERT layaway_payments OFF;

SET IDENTITY_INSERT price_quotes ON;
INSERT price_quotes (id, quote_number, customer_id, location_id, status, issued_on, expires_on, total, staff_id, created_at, row_version)
VALUES (1, 1, 2, 1, 0, '2026-10-22', '2026-11-30', 80.00, 3, '2026-10-22T15:00:00+00:00', 0),
       (2, 2, 2, 1, 1, '2026-09-01', '2026-09-30', 26.49, 3, '2026-09-01T15:00:00+00:00', 0);
SET IDENTITY_INSERT price_quotes OFF;

SET IDENTITY_INSERT price_quote_lines ON;
INSERT price_quote_lines (id, price_quote_id, product_id, variant_id, quantity, unit_price, row_version)
VALUES (1, 1, 60, NULL, 10, 8.00, 0), (2, 2, 60, NULL, 1, 26.49, 0);
SET IDENTITY_INSERT price_quote_lines OFF;

-- Gift cards and certificates: certificate 2 has the same number as card 3 ----------------------------------------------
SET IDENTITY_INSERT gift_cards ON;
INSERT gift_cards (id, serial_number, original_value, remaining_value, issued_to_customer_id, issued_on, expires_on, is_active, created_at, row_version)
VALUES (1, 'GC-1000-0001', 50.00, 23.75, 3, '2026-09-01', NULL, 1, '2026-09-01T15:00:00+00:00', 0),
       (2, 'GC-1000-0002', 50.00, 50.00, 2, '2026-10-22', '2027-10-22', 1, '2026-10-22T18:00:00+00:00', 0),
       (3, 'GC-1000-0003', 25.00, 0, NULL, '2026-03-01', NULL, 0, '2026-03-01T15:00:00+00:00', 0);
SET IDENTITY_INSERT gift_cards OFF;

SET IDENTITY_INSERT gift_certificates ON;
INSERT gift_certificates (id, serial_number, original_value, remaining_value, issued_to_customer_id, issued_on, expires_on, is_active, created_at, row_version)
VALUES (1, 'CERT-500', 20.00, 20.00, 1, '2026-08-15', '2027-08-15', 1, '2026-08-15T15:00:00+00:00', 0),
       (2, 'GC-1000-0003', 10.00, 10.00, NULL, '2026-07-01', NULL, 1, '2026-07-01T15:00:00+00:00', 0);
SET IDENTITY_INSERT gift_certificates OFF;

-- Purchasing ------------------------------------------------------------------------------------------------------------
SET IDENTITY_INSERT purchase_orders ON;
INSERT purchase_orders (id, po_number, supplier_id, location_id, status, quantity_strategy, header_text, posted_on, due_on, total, accounting_bill_ref, created_at, row_version)
VALUES (1, 1, 1, 1, 0, 3, NULL, NULL, NULL, 89.50, NULL, '2026-10-23T14:00:00+00:00', 0),
       (2, 2, 2, 1, 1, 0, 'Screws for the spring', '2026-10-20', '2026-11-19', 97.50, NULL, '2026-10-20T14:00:00+00:00', 0),
       (3, 1, 3, 2, 2, 1, NULL, '2026-10-15', '2026-11-14', 420.00, NULL, '2026-10-15T14:00:00+00:00', 0),
       (4, 3, 1, 1, 3, 0, NULL, '2026-09-01', '2026-10-01', 179.00, 'BILL-77', '2026-09-01T14:00:00+00:00', 0),
       (5, 4, 1, 1, 5, 0, NULL, NULL, NULL, 10.00, NULL, '2026-09-05T14:00:00+00:00', 0);
SET IDENTITY_INSERT purchase_orders OFF;

SET IDENTITY_INSERT purchase_order_lines ON;
INSERT purchase_order_lines (id, purchase_order_id, product_id, variant_id, order_qty, case_qty, cost_each, order_cost, qty_received,
    in_stock_at_generation, on_order_at_generation, back_orders, created_at, row_version)
VALUES (1, 1, 1, NULL, 5, 1, 17.90, 89.50, 0, 68, 0, 0, '2026-10-23T14:00:00+00:00', 0),
       (2, 2, 135, NULL, 1, 10, 9.75, 97.50, 0, 64, 0, 0, '2026-10-20T14:00:00+00:00', 0),
       (3, 3, 202, NULL, 20, 1, 21.00, 420.00, 8, 7, 0, 0, '2026-10-15T14:00:00+00:00', 0),
       (4, 4, 1, NULL, 10, 1, 17.90, 179.00, 10, 58, 0, 0, '2026-09-01T14:00:00+00:00', 0),
       (5, 5, 2, NULL, 1, 1, 10.00, 10.00, 0, 56, 0, 0, '2026-09-05T14:00:00+00:00', 0);
SET IDENTITY_INSERT purchase_order_lines OFF;

SET IDENTITY_INSERT customer_orders ON;
INSERT customer_orders (id, order_number, customer_id, location_id, status, ordered_on, notes, staff_id, created_at, row_version)
VALUES (1, 1, 1, 1, 1, '2026-10-18', 'Call when in', 2, '2026-10-18T15:00:00+00:00', 0),
       (2, 2, 3, 1, 2, '2026-09-18', NULL, 2, '2026-09-18T15:00:00+00:00', 0);
SET IDENTITY_INSERT customer_orders OFF;

SET IDENTITY_INSERT customer_order_lines ON;
INSERT customer_order_lines (id, customer_order_id, product_id, variant_id, ordered_qty, filled_qty, unit_price, created_at, row_version)
VALUES (1, 1, 70, NULL, 3, 1, 21.49, '2026-10-18T15:00:00+00:00', 0), (2, 1, 71, NULL, 2, 2, 2.95, '2026-10-18T15:00:00+00:00', 0),
       (3, 2, 60, NULL, 1, 1, 26.49, '2026-09-18T15:00:00+00:00', 0);
SET IDENTITY_INSERT customer_order_lines OFF;

SET IDENTITY_INSERT stock_transfers ON;
INSERT stock_transfers (id, transfer_number, from_location_id, to_location_id, status, notes, shipped_at, received_at, created_at, row_version)
VALUES (1, 1, 1, 2, 'InTransit', 'Weekly top-up', '2026-10-23T14:00:00+00:00', NULL, '2026-10-23T13:00:00+00:00', 0),
       (2, 2, 1, 2, 'Received', NULL, '2026-10-01T14:00:00+00:00', '2026-10-02T14:00:00+00:00', '2026-10-01T13:00:00+00:00', 0);
SET IDENTITY_INSERT stock_transfers OFF;

SET IDENTITY_INSERT stock_transfer_lines ON;
INSERT stock_transfer_lines (id, stock_transfer_id, product_id, variant_id, stock_code, product_name, quantity, quantity_received, unit_cost, created_at, row_version)
VALUES (1, 1, 1, NULL, 'DEMO-0001', 'Long grain rice 1kg', 6, 0, 17.990, '2026-10-23T13:00:00+00:00', 0),
       (2, 1, 2, NULL, 'DEMO-0002', 'Basmati flour 1.5kg', 4, 0, 12.370, '2026-10-23T13:00:00+00:00', 0),
       (3, 2, 1, NULL, 'DEMO-0001', 'Long grain rice 1kg', 5, 5, 17.990, '2026-10-01T13:00:00+00:00', 0);
SET IDENTITY_INSERT stock_transfer_lines OFF;

-- Staff: commission rules and the time clock ----------------------------------------------------------------------------
SET IDENTITY_INSERT commission_rules ON;
INSERT commission_rules (id, staff_id, product_id, department_id, commission_type, value, max_commission, is_active, created_at, row_version)
VALUES (1, 2, NULL, 1, 'Percentage', 2.0, NULL, 1, '2026-02-01T12:00:00+00:00', 0),
       (2, 2, 89, NULL, 'Fixed', 1.50, 5.00, 1, '2026-02-01T12:00:00+00:00', 0),
       (3, 3, NULL, NULL, 'PercentOfProfit', 10, NULL, 1, '2026-02-01T12:00:00+00:00', 0),
       (4, 4, NULL, 9, 'Percentage', 1.5, NULL, 1, '2026-02-01T12:00:00+00:00', 0);
SET IDENTITY_INSERT commission_rules OFF;

SET IDENTITY_INSERT time_clock_entries ON;
INSERT time_clock_entries (id, staff_id, location_id, clock_in, clock_out, hours_worked, created_at, row_version)
VALUES (1, 2, 1, '2026-10-20T13:00:00+00:00', '2026-10-20T21:00:00+00:00', 8.0, '2026-10-20T13:00:00+00:00', 0),
       (2, 2, 1, '2026-10-21T13:00:00+00:00', '2026-10-21T21:30:00+00:00', 8.5, '2026-10-21T13:00:00+00:00', 0),
       (3, 2, 1, '2026-10-22T13:00:00+00:00', NULL, NULL, '2026-10-22T13:00:00+00:00', 0),
       (4, 3, 1, '2026-10-21T12:00:00+00:00', '2026-10-21T20:00:00+00:00', 8.0, '2026-10-21T12:00:00+00:00', 0),
       (5, 4, 2, '2026-10-22T12:00:00+00:00', '2026-10-22T20:00:00+00:00', 8.0, '2026-10-22T12:00:00+00:00', 0);
SET IDENTITY_INSERT time_clock_entries OFF;

-- History that is not carried over --------------------------------------------------------------------------------------
SET IDENTITY_INSERT stock_counts ON;
INSERT stock_counts (id, count_number, location_id, status, notes, department_id, posted_at, created_at, row_version)
VALUES (1, 1, 1, 'InProgress', 'Aisle 3', NULL, NULL, '2026-10-24T13:00:00+00:00', 0);
SET IDENTITY_INSERT stock_counts OFF;

SET IDENTITY_INSERT stock_count_lines ON;
INSERT stock_count_lines (id, stock_count_id, product_id, variant_id, stock_code, product_name, counted_qty, system_qty_at_count, unit_cost, notes, created_at, row_version)
VALUES (1, 1, 60, NULL, 'DEMO-0060', 'Original bleach 1L', 61, 63, 17.120, NULL, '2026-10-24T13:00:00+00:00', 0);
SET IDENTITY_INSERT stock_count_lines OFF;

SET IDENTITY_INSERT drawer_sessions ON;
INSERT drawer_sessions (id, station_id, location_id, opened_by_staff_id, closed_by_staff_id, opening_float, status, business_date, opened_at, closed_at,
    counted_cash, expected_cash, variance, tender_totals_json, department_net_sales_json, net_sales, tax1collected, tax2collected, cost_of_goods_sold,
    transaction_count, created_at, row_version)
VALUES (1, 1, 1, 2, 3, 100.00, 'Closed', '2026-10-20', '2026-10-20T13:00:00+00:00', '2026-10-20T22:00:00+00:00', 171.50, 171.50, 0, NULL, NULL,
    117.42, 5.87, 4.47, 90.32, 2, '2026-10-20T13:00:00+00:00', 0);
SET IDENTITY_INSERT drawer_sessions OFF;

COMMIT TRANSACTION;
