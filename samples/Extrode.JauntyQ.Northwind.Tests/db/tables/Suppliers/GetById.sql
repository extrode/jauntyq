-- @first
select s.SupplierId, s.CompanyName, s.ContactName, s.ContactTitle, s.Address, s.City, s.Region, s.PostalCode, s.Country, s.Phone, s.Fax
from Suppliers s
where s.SupplierId = @SupplierId