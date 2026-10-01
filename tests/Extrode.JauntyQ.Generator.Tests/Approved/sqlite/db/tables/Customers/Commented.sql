-- @each Ids
-- @type label varchar
select c.id, -- the key
       c."name", /* multi
          line */ 'it''s' as label
from customers c
where c.id in (@Ids) and c."name" <> 'x -- y /* z */'
