-- @type year int
-- @type month int
-- @type revenue numeric
-- @allow-unindexed canonical sakila ships no index on payment.staff_id, staff.store_id; this sample keeps the upstream schema unmodified
-- Normalized to UTC before extracting date parts: payment_date is TIMESTAMPTZ
-- and extract() otherwise uses the session timezone, which shifts rows near
-- month boundaries into a different month than the UTC-naive SQLite side.
-- See docs/torture-test-log.md ("month-boundary timezone divergence").
select
    extract(year from p.payment_date at time zone 'UTC')::int as year,
    extract(month from p.payment_date at time zone 'UTC')::int as month,
    sum(p.amount) as revenue
from payment p
join staff s on s.staff_id = p.staff_id
where s.store_id = @StoreId
group by 1, 2
order by 1, 2
