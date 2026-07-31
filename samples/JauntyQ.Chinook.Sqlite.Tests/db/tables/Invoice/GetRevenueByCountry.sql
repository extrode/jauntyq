-- @type Revenue numeric
select BillingCountry, round(sum(Total), 2) as Revenue, count(InvoiceId) as InvoiceCount
from Invoice
group by BillingCountry
order by Revenue desc, BillingCountry
limit 10
