/* ============================================================================
   Retail25 — read-only diagnostics

   Every query here only reads. Nothing updates, deletes or inserts, so any of
   them is safe to run against the live shop.

   The rule they exist to serve: takings, tax and stock are *reconstructed* from
   append-only ledgers. When a screen disagrees with reality it is almost always
   because a snapshot and its ledger have drifted apart, and these find that.

   Change @LocationId if the shop ever has more than one.
   ============================================================================ */

DECLARE @LocationId bigint = 1;


/* ----------------------------------------------------------------------------
   1 · Today's takings, by how it was paid

   The number to reconcile the drawer against at close. Refunds appear as
   negatives, because they are.
   ---------------------------------------------------------------------------- */
SELECT
    tt.display_name                              AS tender,
    COUNT(DISTINCT st.id)                        AS transactions,
    SUM(sten.amount)                             AS taken
FROM sales_transactions st
JOIN sale_tenders  sten ON sten.transaction_id = st.id
JOIN tender_types  tt   ON tt.id = sten.tender_type_id
WHERE st.location_id = @LocationId
  AND st.business_date = CAST(SYSDATETIMEOFFSET() AS date)
  AND st.is_training = 0
GROUP BY tt.display_name
ORDER BY taken DESC;


/* ----------------------------------------------------------------------------
   2 · Stock that disagrees with its own ledger

   products.on_hand and stock_levels.on_hand are snapshots of
   stock_ledger_entries. If a row appears here, something wrote a snapshot
   without writing the movement behind it — the figure cannot be rebuilt, and
   whatever produced it is a bug worth finding.

   Expect zero rows. Anything else is a real finding.
   ---------------------------------------------------------------------------- */
WITH ledger AS (
    SELECT product_id, SUM(quantity) AS moved
    FROM stock_ledger_entries
    WHERE location_id = @LocationId
    GROUP BY product_id
)
SELECT
    p.stock_code,
    p.name,
    p.on_hand                       AS snapshot_on_product,
    sl.on_hand                      AS snapshot_on_level,
    ISNULL(l.moved, 0)              AS sum_of_ledger,
    p.on_hand - ISNULL(l.moved, 0)  AS drift
FROM products p
LEFT JOIN stock_levels sl ON sl.product_id = p.id AND sl.location_id = @LocationId
LEFT JOIN ledger       l  ON l.product_id  = p.id
WHERE p.location_id = @LocationId
  AND p.is_deleted = 0
  AND p.on_hand <> ISNULL(l.moved, 0)
ORDER BY ABS(p.on_hand - ISNULL(l.moved, 0)) DESC;


/* ----------------------------------------------------------------------------
   3 · Anything at or below zero on hand

   Negative stock means something was sold that was never recorded as arriving.
   Fix it through Inventory → Adjust or a stock count, never with an UPDATE:
   the adjustment writes the movement that makes the number explicable.
   ---------------------------------------------------------------------------- */
SELECT p.stock_code, p.name, p.on_hand, p.reorder_point
FROM products p
WHERE p.location_id = @LocationId
  AND p.is_deleted = 0
  AND p.on_hand < 0
ORDER BY p.on_hand;


/* ----------------------------------------------------------------------------
   4 · Tagged units against the stock figure for their product

   A serialized product's on-hand should equal the number of its units the shop
   is actually holding. Disagreement here is the same class of fault as 2, seen
   from the tag's side.
   ---------------------------------------------------------------------------- */
WITH held AS (
    SELECT product_id, COUNT(*) AS units
    FROM serialized_units
    WHERE location_id = @LocationId
      AND state IN ('InStock', 'InCart', 'Returned')
    GROUP BY product_id
)
SELECT
    p.stock_code,
    p.name,
    ISNULL(h.units, 0) AS tagged_units_held,
    p.on_hand          AS says_on_hand
FROM products p
JOIN serialized_units su ON su.product_id = p.id
LEFT JOIN held h ON h.product_id = p.id
WHERE p.location_id = @LocationId
  AND p.is_deleted = 0
GROUP BY p.stock_code, p.name, h.units, p.on_hand
HAVING ISNULL(h.units, 0) <> p.on_hand
ORDER BY p.stock_code;


/* ----------------------------------------------------------------------------
   5 · Sales whose lines do not add up to their total

   The arithmetic on a finished sale, re-checked. A row here means a receipt
   says one thing and its own lines say another, which is the sort of thing an
   accountant finds months later.
   ---------------------------------------------------------------------------- */
SELECT
    st.transaction_number,
    st.completed_at,
    st.subtotal,
    SUM(sl.extended_net)                        AS lines_add_up_to,
    st.subtotal - SUM(sl.extended_net)          AS difference
FROM sales_transactions st
JOIN sale_lines sl ON sl.transaction_id = st.id
WHERE st.location_id = @LocationId
GROUP BY st.id, st.transaction_number, st.completed_at, st.subtotal
HAVING ABS(st.subtotal - SUM(sl.extended_net)) > 0.005
ORDER BY st.completed_at DESC;


/* ----------------------------------------------------------------------------
   6 · Sales whose tenders do not cover them

   Money in against money owed, per sale. Change given is money back out, so it
   is added to what the tenders had to cover.
   ---------------------------------------------------------------------------- */
SELECT
    st.transaction_number,
    st.completed_at,
    st.grand_total,
    SUM(sten.amount)     AS tendered,
    st.change_given
FROM sales_transactions st
JOIN sale_tenders sten ON sten.transaction_id = st.id
WHERE st.location_id = @LocationId
  AND st.status = 'Completed'
GROUP BY st.id, st.transaction_number, st.completed_at, st.grand_total, st.change_given
HAVING ABS(SUM(sten.amount) - st.grand_total) > 0.005
ORDER BY st.completed_at DESC;


/* ----------------------------------------------------------------------------
   7 · The same tagged unit sold more than once

   Should be impossible — the unit's state machine refuses it and the tag claim
   arbitrates between tills. This is the query that would prove otherwise, and
   it is worth running after any change to the sale path.
   ---------------------------------------------------------------------------- */
SELECT
    sl.epc,
    COUNT(*)                                  AS times_sold,
    STRING_AGG(CAST(st.transaction_number AS varchar(20)), ', ') AS on_receipts
FROM sale_lines sl
JOIN sales_transactions st ON st.id = sl.transaction_id
WHERE sl.epc IS NOT NULL
  AND sl.line_type = 'Sale'
  AND st.status = 'Completed'
GROUP BY sl.epc
HAVING COUNT(*) > 1
ORDER BY times_sold DESC;


/* ----------------------------------------------------------------------------
   8 · Refunds against what was sold

   Everything given back, with the sale it came from. More refunded than sold
   would be a hole in the refund rules; the handler refuses it, and this is how
   you would know if it ever stopped.
   ---------------------------------------------------------------------------- */
SELECT
    sold.transaction_number       AS sold_on_receipt,
    orig.stock_code_snapshot      AS item,
    orig.quantity                 AS sold_qty,
    SUM(-back.quantity)           AS refunded_qty
FROM sale_lines orig
JOIN sales_transactions sold ON sold.id = orig.transaction_id
JOIN sale_lines back          ON back.refunds_sale_line_id = orig.id
GROUP BY sold.transaction_number, orig.stock_code_snapshot, orig.quantity
ORDER BY sold.transaction_number DESC;


/* ----------------------------------------------------------------------------
   9 · Carts left open

   A cart nobody finished. Normal in small numbers — a customer walked away —
   but a station that never clears is a till somebody cannot sell at.
   ---------------------------------------------------------------------------- */
SELECT
    c.id,
    c.station_id,
    c.status,
    c.created_at,
    DATEDIFF(hour, c.created_at, SYSDATETIMEOFFSET()) AS hours_old,
    (SELECT COUNT(*) FROM cart_lines cl WHERE cl.cart_id = c.id) AS lines
FROM carts c
WHERE c.status = 'Active'
ORDER BY c.created_at;


/* ----------------------------------------------------------------------------
   10 · Who changed what

   The audit trail, most recent first. The answer to "this was different
   yesterday".
   ---------------------------------------------------------------------------- */
SELECT TOP 50
    a.occurred_at,
    a.actor_name,
    a.action,
    a.entity_type,
    a.entity_id,
    a.operation,
    a.station_id
FROM audit_log_entries a
ORDER BY a.occurred_at DESC;
