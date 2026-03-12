UPDATE Customers
SET CompanyName = @CompanyName, ContactName = @ContactName, City = @City, Country = @Country
WHERE CustomerId = @CustomerId
