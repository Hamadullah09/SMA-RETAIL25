-- sqlcmd defaults QUOTED_IDENTIFIER off, which SQL Server refuses to combine with the filtered
-- indexes and computed columns on these tables.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @rate decimal(19,6) = 205.0;

BEGIN TRANSACTION;

-- ---------------------------------------------------------------- catalogue and forward prices
-- Rounded to the whole rupee, because these are the numbers a shopkeeper sets and a shelf label
-- prints. Paisa are not used, and the minimum tender is already 1.00.

UPDATE products SET
    regular_price = ROUND(regular_price * @rate, 0),
    last_cost     = ROUND(last_cost * @rate, 0),
    avg_cost      = ROUND(avg_cost * @rate, 0);

UPDATE product_prices    SET price = ROUND(price * @rate, 0);
UPDATE product_suppliers SET cost  = ROUND(cost * @rate, 0);

UPDATE customer_accounts SET credit_limit = ROUND(credit_limit * @rate, 0);

UPDATE gift_cards        SET original_value = ROUND(original_value * @rate, 0),
                             remaining_value = ROUND(remaining_value * @rate, 0);
UPDATE gift_certificates SET original_value = ROUND(original_value * @rate, 0),
                             remaining_value = ROUND(remaining_value * @rate, 0);

UPDATE loyalty_policies SET reward_fixed_amount = ROUND(reward_fixed_amount * @rate, 0);

-- Points are earned per unit of currency, so the rate has to move the other way: 1 point per dollar
-- would otherwise become 1 point per rupee and hand out 205 times the rewards.
UPDATE loyalty_policies SET points_per_dollar = points_per_dollar / @rate WHERE points_per_dollar > 0;

-- ---------------------------------------------------------------- open commitments and history
-- Multiplied exactly, NOT rounded. Each of these has to keep agreeing with the others: a sale's
-- grand total is the sum of its lines plus tax, a tender matches what was owed, a drawer's expected
-- cash is what its movements add up to. Rounding every column on its own would break those sums
-- independently and leave a set of books that no longer balances.

UPDATE purchase_order_lines    SET cost_each = cost_each * @rate, order_cost = order_cost * @rate;
UPDATE purchase_orders         SET total = total * @rate;
UPDATE purchase_order_receipts SET freight_total = freight_total * @rate;

UPDATE price_quote_lines SET unit_price = unit_price * @rate;
UPDATE price_quotes      SET total = total * @rate;

UPDATE customer_order_lines SET unit_price = unit_price * @rate;

UPDATE layaway_lines    SET unit_price = unit_price * @rate;
UPDATE layaway_payments SET amount = amount * @rate;
UPDATE layaways         SET total = total * @rate, amount_paid = amount_paid * @rate;

UPDATE invoices        SET invoice_total = invoice_total * @rate,
                           balance_due = balance_due * @rate,
                           penalty_accrued = penalty_accrued * @rate;
UPDATE invoice_payments SET amount = amount * @rate,
                            applied_to_principal = applied_to_principal * @rate,
                            applied_to_penalty = applied_to_penalty * @rate;

UPDATE customer_accounts SET balance_due = balance_due * @rate;
UPDATE ar_ledger_entries SET amount = amount * @rate;

UPDATE cart_lines       SET unit_price = unit_price * @rate,
                            manual_unit_price = manual_unit_price * @rate,
                            embedded_price = embedded_price * @rate,
                            extended_net = extended_net * @rate,
                            tax1amount = tax1amount * @rate,
                            tax2amount = tax2amount * @rate,
                            unit_cost_snapshot = unit_cost_snapshot * @rate;
UPDATE cart_adjustments SET amount = amount * @rate;

UPDATE sale_lines SET unit_price = unit_price * @rate,
                      extended_net = extended_net * @rate,
                      taxable_net = taxable_net * @rate,
                      prorated_adjustment = prorated_adjustment * @rate,
                      tax1amount = tax1amount * @rate,
                      tax2amount = tax2amount * @rate,
                      unit_cost_snapshot = unit_cost_snapshot * @rate;

UPDATE sale_adjustments SET amount = amount * @rate;

UPDATE sale_tenders SET amount = amount * @rate,
                        amount_tendered = amount_tendered * @rate,
                        change_given = change_given * @rate;

UPDATE sales_transactions SET subtotal = subtotal * @rate,
                              discount_total = discount_total * @rate,
                              add_on_charge_total = add_on_charge_total * @rate,
                              tax1total = tax1total * @rate,
                              tax2total = tax2total * @rate,
                              grand_total = grand_total * @rate,
                              change_given = change_given * @rate,
                              rounding_adjustment = rounding_adjustment * @rate,
                              cost_of_goods_sold = cost_of_goods_sold * @rate;

UPDATE drawer_ledger_entries SET amount = amount * @rate;

UPDATE drawer_sessions SET opening_float = opening_float * @rate,
                           expected_cash = expected_cash * @rate,
                           counted_cash = counted_cash * @rate,
                           variance = variance * @rate,
                           net_sales = net_sales * @rate,
                           tax1collected = tax1collected * @rate,
                           tax2collected = tax2collected * @rate,
                           cost_of_goods_sold = cost_of_goods_sold * @rate;

COMMIT TRANSACTION;
GO

-- ---------------------------------------------------------------- reconciliation
-- Proves the books still balance rather than assuming it: every sale's stored grand total must
-- still equal its own lines plus its own tax.

SELECT COUNT(*) AS sales_that_do_not_reconcile
FROM sales_transactions t
WHERE ABS(
        t.grand_total
        - (SELECT ISNULL(SUM(l.extended_net + l.tax1amount + l.tax2amount), 0)
           FROM sale_lines l WHERE l.transaction_id = t.id)
        - ISNULL(t.add_on_charge_total, 0)
        + ISNULL(t.discount_total, 0)
        - ISNULL(t.rounding_adjustment, 0)
      ) > 0.05;

SELECT TOP 5 stock_code, name, regular_price FROM products WHERE regular_price > 0 ORDER BY id;
SELECT id, grand_total FROM sales_transactions ORDER BY id DESC;
