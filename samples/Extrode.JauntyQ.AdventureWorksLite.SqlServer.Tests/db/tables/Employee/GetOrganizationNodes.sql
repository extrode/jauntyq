-- Round 14 audit (§2.11): selects the exotic, unmapped hierarchyid column
-- directly, so its "object?" fallback (AUD-R13-01) round-trips against a
-- real running SQL Server -- including the NULL case (BusinessEntityID 4).
-- Expected to raise JNT2007 (unmapped db type "hierarchyid"): that is the
-- correct, documented behavior for this construct, not a build regression.
select BusinessEntityID, OrganizationNode
from HumanResources.Employee
order by BusinessEntityID
