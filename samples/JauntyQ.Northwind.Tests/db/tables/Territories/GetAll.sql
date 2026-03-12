select t.TerritoryId, t.Description, t.RegionId, r.Description as RegionDescription
from Territories t
join Region r on t.RegionId = r.RegionId