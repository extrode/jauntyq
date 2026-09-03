-- PostgreSQL Northwind subset: schema + seed. Applied by PostgresFixture to a
-- Testcontainers-managed postgres instance. serial columns are identity keys,
-- matching isIdentity=true in the committed schema snapshot.

CREATE TABLE categories (
    category_id   SERIAL PRIMARY KEY,
    category_name VARCHAR(30) NOT NULL,
    description   TEXT NULL
);

CREATE TABLE suppliers (
    supplier_id  SERIAL PRIMARY KEY,
    company_name VARCHAR(40) NOT NULL,
    city         VARCHAR(15) NULL
);

CREATE TABLE products (
    product_id    SERIAL PRIMARY KEY,
    product_name  VARCHAR(40) NOT NULL,
    supplier_id   INTEGER NULL,
    category_id   INTEGER NULL,
    unit_price    NUMERIC(10,2) NULL,
    discontinued  BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE shippers (
    shipper_id   SERIAL PRIMARY KEY,
    company_name VARCHAR(40) NOT NULL,
    phone        VARCHAR(24) NULL
);

CREATE TABLE region (
    region_id          INTEGER PRIMARY KEY,
    region_description VARCHAR(50) NOT NULL
);

INSERT INTO categories (category_name, description) VALUES
    ('Beverages', 'Soft drinks, coffees, teas'),
    ('Condiments', 'Sweet and savory sauces'),
    ('Produce', 'Dried fruit and bean curd');

INSERT INTO suppliers (company_name, city) VALUES
    ('Exotic Liquids', 'London'),
    ('New Orleans Cajun Delights', 'New Orleans');

INSERT INTO products (product_name, supplier_id, category_id, unit_price, discontinued) VALUES
    ('Chai', 1, 1, 18.00, false),
    ('Chang', 1, 1, 19.00, false),
    ('Aniseed Syrup', 1, 2, 10.00, false),
    ('Cajun Seasoning', 2, 2, 22.00, false);

INSERT INTO shippers (company_name, phone) VALUES
    ('Speedy Express', '(503) 555-9831'),
    ('United Package', '(503) 555-3199');

INSERT INTO region (region_id, region_description) VALUES
    (1, 'Eastern'),
    (2, 'Western');
