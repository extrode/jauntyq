-- @each SupportRepIds
select CustomerId, FirstName, LastName, Company, Country, Email, SupportRepId
from Customer
where SupportRepId in (@SupportRepIds)
order by SupportRepId, CustomerId
