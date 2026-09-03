-- @each CustomerIds
select InvoiceId, CustomerId, InvoiceDate, BillingCity, BillingCountry, Total
from Invoice
where CustomerId in (@CustomerIds)
order by CustomerId, InvoiceDate, InvoiceId
