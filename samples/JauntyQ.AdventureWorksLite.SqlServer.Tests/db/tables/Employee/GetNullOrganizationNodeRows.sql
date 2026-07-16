-- Round 15 audit (§2.11 residual, carried forward from round 14 report §5 /
-- round 13 report §5): closes the NULL-handling half of AUD-R13-01's fix,
-- which round 14 (AUD-R14-01) discovered could never be reached through
-- GetOrganizationNodes.sql -- that query's row 1 (a non-null hierarchyid)
-- throws System.IO.FileNotFoundException before row 4's NULL is ever read,
-- because Microsoft.Data.SqlClient needs the deliberately-unreferenced
-- Microsoft.SqlServer.Types assembly to materialize any NON-NULL CLR UDT
-- value. Filtering to OrganizationNode IS NULL selects only BusinessEntityID
-- 4 (Gustavo) -- the generated reader code's `reader.IsDBNull(i)` guard
-- (AUD-R13-01's own fix) short-circuits before ever calling GetValue/the
-- CLR UDT materializer, so this query reaches the NULL branch live without
-- the crash, closing the residual without touching the crash itself (fixing
-- the crash is a separate, out-of-scope redesign -- see AUD-R14-01).
select BusinessEntityID, OrganizationNode
from HumanResources.Employee
where OrganizationNode is null
order by BusinessEntityID
