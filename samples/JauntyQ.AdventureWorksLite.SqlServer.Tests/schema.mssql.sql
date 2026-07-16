-- AdventureWorksLite: a trimmed, hand-authored port of Microsoft's canonical
-- AdventureWorks sample schema, kept to the structural traits that make real
-- AdventureWorks the standard torture test for a multi-schema catalog:
--   - 5 real schemas (Person, HumanResources, Production, Purchasing, Sales),
--     with foreign keys crossing schema boundaries in both directions.
--   - Two genuine persisted computed columns lifted verbatim from real
--     AdventureWorks: Sales.SalesOrderDetail.LineTotal and
--     Sales.SalesOrderHeader.TotalDue.
--   - HumanResources.Employee.OrganizationNode (hierarchyid) — an exotic
--     SQL-Server-only type with no JauntyQ mapping, to confirm it degrades to
--     the documented "object" fallback + JNT2007 rather than crashing codegen.
--   - A cross-schema composite-PK junction (Purchasing.ProductVendor).
--   - A reporting view (Sales.vSalesOrderDetailExtended), read via a plain
--     hand-written SELECT — this schema deliberately does NOT enable
--     JauntyQAutoCrud, so the view is never registered as an AutoCrud target;
--     it's here purely to confirm SELECT-from-view works like any other query.
--
-- Deviation from real AdventureWorks (scope decision, not an oversight):
-- Purchasing.Vendor.BusinessEntityID is its own identity, not a foreign key
-- into a shared Person.BusinessEntity base table — the real schema's
-- table-per-hierarchy BusinessEntity base table adds no further stress on
-- the multi-schema/computed-column/exotic-type surface this pilot targets,
-- so it's dropped to keep the fixture reviewable. Person.Person and
-- HumanResources.Employee keep the real BusinessEntityID-as-shared-PK
-- relationship (Employee.BusinessEntityID is both its own PK and a FK to
-- Person.Person), since that's exactly the shape a cross-schema 1:1 FK
-- takes in the real schema.
-- Real AdventureWorks constraint convention: create every table's own
-- columns and primary key first, then add every foreign key afterward in a
-- separate batch of ALTER TABLE ... ADD CONSTRAINT statements. Kept here
-- deliberately (rather than inline REFERENCES) to match that convention.

if schema_id('Person') is null exec('create schema Person');
if schema_id('HumanResources') is null exec('create schema HumanResources');
if schema_id('Production') is null exec('create schema Production');
if schema_id('Purchasing') is null exec('create schema Purchasing');
if schema_id('Sales') is null exec('create schema Sales');
go

create table Person.Person (
    BusinessEntityID int not null identity(1,1) primary key,
    PersonType varchar(2) not null,
    FirstName nvarchar(50) not null,
    LastName nvarchar(50) not null,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Person.EmailAddress (
    BusinessEntityID int not null,
    EmailAddressID int not null identity(1,1),
    EmailAddress varchar(50) not null,
    constraint PK_EmailAddress primary key (BusinessEntityID, EmailAddressID)
);

create table HumanResources.Employee (
    BusinessEntityID int not null primary key,
    NationalIDNumber varchar(15) not null,
    JobTitle varchar(50) not null,
    HireDate date not null,
    OrganizationNode hierarchyid null,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Production.ProductCategory (
    ProductCategoryID int not null identity(1,1) primary key,
    Name varchar(50) not null,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Production.ProductSubcategory (
    ProductSubcategoryID int not null identity(1,1) primary key,
    ProductCategoryID int not null,
    Name varchar(50) not null,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Production.Product (
    ProductID int not null identity(1,1) primary key,
    Name varchar(50) not null,
    ProductNumber varchar(25) not null,
    StandardCost decimal(19,4) not null,
    ListPrice decimal(19,4) not null,
    ProductSubcategoryID int null,
    SellStartDate date not null,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Purchasing.Vendor (
    BusinessEntityID int not null identity(1,1) primary key,
    Name varchar(50) not null,
    CreditRating tinyint not null,
    ActiveFlag bit not null default 1,
    ModifiedDate datetime not null default getdate()
);

create table Purchasing.ProductVendor (
    ProductID int not null,
    BusinessEntityID int not null,
    AverageLeadTime int not null,
    StandardPrice decimal(19,4) not null,
    constraint PK_ProductVendor primary key (ProductID, BusinessEntityID)
);

create table Sales.SalesTerritory (
    TerritoryID int not null identity(1,1) primary key,
    Name varchar(50) not null,
    CountryRegionCode varchar(3) not null,
    [Group] varchar(50) not null,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Sales.Customer (
    CustomerID int not null identity(1,1) primary key,
    PersonID int null,
    TerritoryID int null,
    -- Real AdventureWorks: AccountNumber AS (isnull('AW'+RIGHT('0000000'+
    -- CAST(CustomerID AS varchar(7)),7), '')) persisted -- simplified here
    -- to drop the UDF-free zero-padding but keep the same genuine pattern
    -- (a persisted computed column derived from the row's own identity PK).
    AccountNumber as ('AW' + right('0000000' + cast(CustomerID as varchar(7)), 7)) persisted,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Sales.SalesOrderHeader (
    SalesOrderID int not null identity(1,1) primary key,
    OrderDate datetime not null,
    CustomerID int not null,
    TerritoryID int null,
    SubTotal decimal(19,4) not null default 0,
    TaxAmt decimal(19,4) not null default 0,
    Freight decimal(19,4) not null default 0,
    -- Verbatim real AdventureWorks computed column.
    TotalDue as (isnull(SubTotal + TaxAmt + Freight, 0)) persisted,
    rowguid uniqueidentifier not null default newid(),
    ModifiedDate datetime not null default getdate()
);

create table Sales.SalesOrderDetail (
    SalesOrderID int not null,
    SalesOrderDetailID int not null identity(1,1),
    ProductID int not null,
    OrderQty smallint not null,
    UnitPrice decimal(19,4) not null,
    UnitPriceDiscount decimal(19,4) not null default 0,
    -- Verbatim real AdventureWorks computed column.
    LineTotal as (isnull(UnitPrice * (1 - UnitPriceDiscount) * OrderQty, 0)) persisted,
    constraint PK_SalesOrderDetail primary key (SalesOrderID, SalesOrderDetailID)
);
go

-- Foreign keys, added after every table exists (real AdventureWorks
-- convention) — this is also what directly exercises schema-qualified
-- cross-schema table/column resolution end to end, not just within one
-- schema.
alter table Person.EmailAddress
    add constraint FK_EmailAddress_Person foreign key (BusinessEntityID) references Person.Person (BusinessEntityID);

alter table HumanResources.Employee
    add constraint FK_Employee_Person foreign key (BusinessEntityID) references Person.Person (BusinessEntityID);

alter table Production.ProductSubcategory
    add constraint FK_ProductSubcategory_ProductCategory foreign key (ProductCategoryID) references Production.ProductCategory (ProductCategoryID);

alter table Production.Product
    add constraint FK_Product_ProductSubcategory foreign key (ProductSubcategoryID) references Production.ProductSubcategory (ProductSubcategoryID);

alter table Purchasing.ProductVendor
    add constraint FK_ProductVendor_Product foreign key (ProductID) references Production.Product (ProductID);
alter table Purchasing.ProductVendor
    add constraint FK_ProductVendor_Vendor foreign key (BusinessEntityID) references Purchasing.Vendor (BusinessEntityID);

alter table Sales.Customer
    add constraint FK_Customer_Person foreign key (PersonID) references Person.Person (BusinessEntityID);
alter table Sales.Customer
    add constraint FK_Customer_SalesTerritory foreign key (TerritoryID) references Sales.SalesTerritory (TerritoryID);

alter table Sales.SalesOrderHeader
    add constraint FK_SalesOrderHeader_Customer foreign key (CustomerID) references Sales.Customer (CustomerID);
alter table Sales.SalesOrderHeader
    add constraint FK_SalesOrderHeader_SalesTerritory foreign key (TerritoryID) references Sales.SalesTerritory (TerritoryID);

alter table Sales.SalesOrderDetail
    add constraint FK_SalesOrderDetail_SalesOrderHeader foreign key (SalesOrderID) references Sales.SalesOrderHeader (SalesOrderID);
alter table Sales.SalesOrderDetail
    add constraint FK_SalesOrderDetail_Product foreign key (ProductID) references Production.Product (ProductID);
go

create view Sales.vSalesOrderDetailExtended as
select
    d.SalesOrderID,
    d.SalesOrderDetailID,
    p.Name as ProductName,
    d.OrderQty,
    d.UnitPrice,
    d.LineTotal
from Sales.SalesOrderDetail d
join Production.Product p on p.ProductID = d.ProductID;
go

-- Seed data (small but exercises every FK, both schemas' worth of nullable
-- FKs, and enough rows to make row-count/aggregate assertions meaningful).

set identity_insert Person.Person on;
insert into Person.Person (BusinessEntityID, PersonType, FirstName, LastName) values
    (1, 'EM', 'Ken', 'Sanchez'),
    (2, 'EM', 'Terri', 'Duffy'),
    (3, 'EM', 'Roberto', 'Tamburello'),
    (4, 'SC', 'Gustavo', 'Achong'),
    (5, 'SC', 'Catherine', 'Abel');
set identity_insert Person.Person off;

insert into Person.EmailAddress (BusinessEntityID, EmailAddress) values
    (1, 'ken0@adventure-works.com'),
    (2, 'terri0@adventure-works.com'),
    (3, 'roberto0@adventure-works.com'),
    (4, 'gustavo0@adventure-works.com'),
    (5, 'catherine0@adventure-works.com');

-- Round 14 audit (§2.11): BusinessEntityID 4 (Gustavo, already in Person.Person
-- above) is deliberately given a NULL OrganizationNode -- the first live NULL
-- for this hierarchyid/unmapped-type column anywhere in the sample matrix, so
-- AdventureWorksLiteQueriesTests can exercise the NULL half of AUD-R13-01's
-- "object?" + IsDBNull-guard fix end-to-end against a real query result, not
-- just synthetic reader data (see NullableUnmappedColumn_PropertyIsNullableObject_
-- AndReaderGuardsIsDBNull in tests/JauntyQ.Generator.Tests/UnmappedColumnTypeTests.cs,
-- which proves the same shape at the generator level only).
insert into HumanResources.Employee (BusinessEntityID, NationalIDNumber, JobTitle, HireDate, OrganizationNode) values
    (1, '295847284', 'Chief Executive Officer', '2003-02-15', hierarchyid::GetRoot()),
    (2, '245797967', 'Vice President of Engineering', '2003-02-15', hierarchyid::Parse('/1/')),
    (3, '509647174', 'Engineering Manager', '2003-02-15', hierarchyid::Parse('/1/1/')),
    (4, '112233445', 'Records Clerk', '2005-06-01', null);

set identity_insert Production.ProductCategory on;
insert into Production.ProductCategory (ProductCategoryID, Name) values
    (1, 'Bikes'),
    (2, 'Components'),
    (3, 'Clothing');
set identity_insert Production.ProductCategory off;

set identity_insert Production.ProductSubcategory on;
insert into Production.ProductSubcategory (ProductSubcategoryID, ProductCategoryID, Name) values
    (1, 1, 'Mountain Bikes'),
    (2, 1, 'Road Bikes'),
    (3, 2, 'Handlebars'),
    (4, 3, 'Jerseys');
set identity_insert Production.ProductSubcategory off;

set identity_insert Production.Product on;
insert into Production.Product (ProductID, Name, ProductNumber, StandardCost, ListPrice, ProductSubcategoryID, SellStartDate) values
    (1, 'Mountain-100 Black, 42', 'BK-M82B-42', 1912.1544, 3374.99, 1, '2011-05-31'),
    (2, 'Mountain-200 Silver, 38', 'BK-M68S-38', 1265.6195, 2294.99, 1, '2011-05-31'),
    (3, 'Road-150 Red, 44', 'BK-R93R-44', 2171.2942, 3578.27, 2, '2011-05-31'),
    (4, 'Road-650 Black, 62', 'BK-R89B-62', 486.7076, 782.99, 2, '2011-05-31'),
    (5, 'HL Mountain Handlebars', 'HB-M918', 43.0625, 120.27, 3, '2011-05-31'),
    (6, 'LL Road Handlebars', 'HB-R409', 13.0863, 36.29, 3, '2011-05-31'),
    (7, 'Long-Sleeve Logo Jersey, M', 'LJ-0192-M', 38.4923, 49.99, 4, '2011-05-31'),
    (8, 'Classic Vest, S', 'VE-C304-S', 7.4544, 63.5, 4, '2011-05-31');
set identity_insert Production.Product off;

set identity_insert Purchasing.Vendor on;
insert into Purchasing.Vendor (BusinessEntityID, Name, CreditRating, ActiveFlag) values
    (1, 'Litware, Inc.', 1, 1),
    (2, 'Proseware, Inc.', 2, 1),
    (3, 'Fabrikam, Inc.', 3, 0);
set identity_insert Purchasing.Vendor off;

insert into Purchasing.ProductVendor (ProductID, BusinessEntityID, AverageLeadTime, StandardPrice) values
    (1, 1, 14, 1735.20),
    (2, 1, 14, 1150.75),
    (3, 2, 21, 1980.00),
    (5, 2, 7, 38.15),
    (6, 3, 10, 11.50),
    (7, 3, 5, 34.00);

set identity_insert Sales.SalesTerritory on;
insert into Sales.SalesTerritory (TerritoryID, Name, CountryRegionCode, [Group]) values
    (1, 'Northwest', 'US', 'North America'),
    (2, 'Southwest', 'US', 'North America'),
    (3, 'Canada', 'CA', 'North America'),
    (4, 'France', 'FR', 'Europe');
set identity_insert Sales.SalesTerritory off;

set identity_insert Sales.Customer on;
insert into Sales.Customer (CustomerID, PersonID, TerritoryID) values
    (1, 4, 1),
    (2, 5, 1),
    (3, null, 2),
    (4, null, 3),
    (5, null, null);
set identity_insert Sales.Customer off;

set identity_insert Sales.SalesOrderHeader on;
insert into Sales.SalesOrderHeader (SalesOrderID, OrderDate, CustomerID, TerritoryID, SubTotal, TaxAmt, Freight) values
    (1, '2023-06-01', 1, 1, 3374.99, 269.99, 84.37),
    (2, '2023-06-03', 2, 1, 2294.99, 183.60, 57.37),
    (3, '2023-06-10', 3, 2, 3578.27, 286.26, 89.46),
    (4, '2023-07-01', 1, 1, 156.56, 12.52, 3.91),
    (5, '2023-07-15', 4, 3, 782.99, 62.64, 19.57),
    (6, '2023-08-02', 5, null, 157.13, 12.57, 3.93);
set identity_insert Sales.SalesOrderHeader off;

set identity_insert Sales.SalesOrderDetail on;
insert into Sales.SalesOrderDetail (SalesOrderDetailID, SalesOrderID, ProductID, OrderQty, UnitPrice, UnitPriceDiscount) values
    (1, 1, 1, 1, 3374.99, 0),
    (2, 2, 2, 1, 2294.99, 0),
    (3, 3, 3, 1, 3578.27, 0),
    (4, 4, 5, 1, 120.27, 0),
    (5, 4, 6, 1, 36.29, 0),
    (6, 5, 4, 1, 782.99, 0),
    (7, 6, 7, 2, 49.99, 0),
    (8, 6, 8, 1, 63.5, 0.10);
set identity_insert Sales.SalesOrderDetail off;
