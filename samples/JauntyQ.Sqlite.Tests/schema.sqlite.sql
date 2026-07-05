-- SQLite Northwind subset: schema + seed data.
-- Run by SqliteFixture against a fresh in-process database each test run.
-- INTEGER PRIMARY KEY is the SQLite rowid alias (auto-increment), matching
-- isIdentity=true in the committed schema snapshot.

CREATE TABLE Categories (
    CategoryId   INTEGER PRIMARY KEY,
    CategoryName TEXT NOT NULL,
    Description  TEXT NULL
);

CREATE TABLE Suppliers (
    SupplierId  INTEGER PRIMARY KEY,
    CompanyName TEXT NOT NULL,
    City        TEXT NULL
);

CREATE TABLE Products (
    ProductId    INTEGER PRIMARY KEY,
    ProductName  TEXT NOT NULL,
    SupplierId   INTEGER NULL,
    CategoryId   INTEGER NULL,
    UnitPrice    NUMERIC NULL,
    Discontinued INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Shippers (
    ShipperId   INTEGER PRIMARY KEY,
    CompanyName TEXT NOT NULL,
    Phone       TEXT NULL
);

CREATE TABLE Region (
    RegionId          INTEGER PRIMARY KEY,
    RegionDescription TEXT NOT NULL
);

INSERT INTO Categories (CategoryName, Description) VALUES
    ('Beverages', 'Soft drinks, coffees, teas'),
    ('Condiments', 'Sweet and savory sauces'),
    ('Produce', 'Dried fruit and bean curd');

INSERT INTO Suppliers (CompanyName, City) VALUES
    ('Exotic Liquids', 'London'),
    ('New Orleans Cajun Delights', 'New Orleans');

INSERT INTO Products (ProductName, SupplierId, CategoryId, UnitPrice, Discontinued) VALUES
    ('Chai', 1, 1, 18.00, 0),
    ('Chang', 1, 1, 19.00, 0),
    ('Aniseed Syrup', 1, 2, 10.00, 0),
    ('Cajun Seasoning', 2, 2, 22.00, 0);

INSERT INTO Shippers (CompanyName, Phone) VALUES
    ('Speedy Express', '(503) 555-9831'),
    ('United Package', '(503) 555-3199');

INSERT INTO Region (RegionId, RegionDescription) VALUES
    (1, 'Eastern'),
    (2, 'Western');
