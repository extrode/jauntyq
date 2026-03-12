select c.CustomerId, c.CompanyName, c.ContactName, c.ContactTitle, c.Address, c.City, c.Region, c.PostalCode, c.Country, c.Phone, c.Fax
from Customers c
where c.CustomerId = @customerId