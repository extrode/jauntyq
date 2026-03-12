select c.CustomerId, c.CompanyName, c.ContactName, c.City
from Customers c
where c.City = @city