-- Regression fixture for AUD-R50-01: this string literal deliberately spans
-- two lines. The emitted CommandText's continuation-line indentation must
-- never leak into the literal's runtime value — the engine must receive and
-- return "line1\nline2" byte-for-byte.
-- @first
-- @type Labeled text
select CategoryName || '[line1
line2]' as Labeled
from Categories
where CategoryId = @CategoryId
