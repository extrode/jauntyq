-- MySQL Northwind subset: schema + seed. Applied by MySqlFixture to a
-- Testcontainers-managed MySQL instance. AUTO_INCREMENT columns are identity
-- keys; the generated MySQL Insert returns them via SELECT last_insert_id(),
-- and upsert uses ON DUPLICATE KEY UPDATE -- both distinct from the other
-- dialects, which is exactly why this coverage matters.

CREATE TABLE categories (
    category_id   INT AUTO_INCREMENT PRIMARY KEY,
    category_name VARCHAR(30) NOT NULL,
    description   TEXT NULL
);

CREATE TABLE suppliers (
    supplier_id  INT AUTO_INCREMENT PRIMARY KEY,
    company_name VARCHAR(40) NOT NULL,
    city         VARCHAR(15) NULL
);

CREATE TABLE products (
    product_id    INT AUTO_INCREMENT PRIMARY KEY,
    product_name  VARCHAR(40) NOT NULL,
    supplier_id   INT NULL,
    category_id   INT NULL,
    unit_price    DECIMAL(10,2) NULL,
    discontinued  TINYINT NOT NULL DEFAULT 0
);

CREATE TABLE shippers (
    shipper_id   INT AUTO_INCREMENT PRIMARY KEY,
    company_name VARCHAR(40) NOT NULL,
    phone        VARCHAR(24) NULL
);

CREATE TABLE region (
    region_id          INT PRIMARY KEY,
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
    ('Chai', 1, 1, 18.00, 0),
    ('Chang', 1, 1, 19.00, 0),
    ('Aniseed Syrup', 1, 2, 10.00, 0),
    ('Cajun Seasoning', 2, 2, 22.00, 0);

INSERT INTO shippers (company_name, phone) VALUES
    ('Speedy Express', '(503) 555-9831'),
    ('United Package', '(503) 555-3199');

INSERT INTO region (region_id, region_description) VALUES
    (1, 'Eastern'),
    (2, 'Western');
