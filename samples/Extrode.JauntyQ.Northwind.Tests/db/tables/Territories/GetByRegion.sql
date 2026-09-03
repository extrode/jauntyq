-- @each RegionIds
select t.TerritoryId, t.Description
from Territories t
where t.RegionId in (@RegionIds)
