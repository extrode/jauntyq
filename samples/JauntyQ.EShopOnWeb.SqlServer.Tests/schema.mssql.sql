-- eShopOnWeb data-layer port (torture test, Part 1). Seven tables covering
-- Catalog/Basket/Order aggregates from dotnet-architecture/eShopOnWeb's
-- ApplicationCore, with EF Core's owned-type value objects (Address on
-- Order, CatalogItemOrdered on OrderItem) flattened onto their owning
-- table's columns -- see docs/torture-test-log.md "Part 1 kickoff scope
-- decisions" for why (JauntyQ has no owned-type/nested-object mapping).
-- Seed data (5 brands, 4 types, 12 catalog items) is copied verbatim from
-- the source app's CatalogContextSeed.cs.
--
-- order_date declared DATETIME2 storing the UTC instant only (per Part 1
-- scope decision). Pagination uses OFFSET/FETCH from the start (never
-- TOP n), sidestepping the known SELECT TOP n parser gap proactively.

CREATE TABLE catalog_brand (
    id     INT IDENTITY(1,1) PRIMARY KEY,
    brand  VARCHAR(255) NOT NULL
);

CREATE TABLE catalog_type (
    id    INT IDENTITY(1,1) PRIMARY KEY,
    type  VARCHAR(255) NOT NULL
);

CREATE TABLE catalog_item (
    id                 INT IDENTITY(1,1) PRIMARY KEY,
    catalog_type_id    INT NOT NULL REFERENCES catalog_type(id),
    catalog_brand_id   INT NOT NULL REFERENCES catalog_brand(id),
    description        VARCHAR(MAX) NOT NULL,
    name               VARCHAR(255) NOT NULL,
    price              DECIMAL(18,2) NOT NULL,
    picture_uri        VARCHAR(255) NOT NULL
);

CREATE TABLE basket (
    id        INT IDENTITY(1,1) PRIMARY KEY,
    buyer_id  VARCHAR(255) NOT NULL
);

CREATE TABLE basket_item (
    id                INT IDENTITY(1,1) PRIMARY KEY,
    basket_id         INT NOT NULL REFERENCES basket(id),
    catalog_item_id   INT NOT NULL REFERENCES catalog_item(id),
    unit_price        DECIMAL(18,2) NOT NULL,
    quantity          INT NOT NULL
);

-- Table named `orders`, not `order` (reserved word).
CREATE TABLE orders (
    id                INT IDENTITY(1,1) PRIMARY KEY,
    buyer_id          VARCHAR(255) NOT NULL,
    order_date        DATETIME2(6) NOT NULL,
    ship_to_street    VARCHAR(255) NOT NULL,
    ship_to_city      VARCHAR(255) NOT NULL,
    ship_to_state     VARCHAR(255) NOT NULL,
    ship_to_country   VARCHAR(255) NOT NULL,
    ship_to_zipcode   VARCHAR(255) NOT NULL
);

CREATE TABLE order_item (
    id                        INT IDENTITY(1,1) PRIMARY KEY,
    order_id                  INT NOT NULL REFERENCES orders(id),
    unit_price                DECIMAL(18,2) NOT NULL,
    units                     INT NOT NULL,
    ordered_catalog_item_id   INT NOT NULL,
    ordered_product_name      VARCHAR(255) NOT NULL,
    ordered_picture_uri       VARCHAR(255) NOT NULL
);

SET IDENTITY_INSERT catalog_brand ON;
INSERT INTO catalog_brand (id, brand) VALUES
    (1, 'Azure'),
    (2, '.NET'),
    (3, 'Visual Studio'),
    (4, 'SQL Server'),
    (5, 'Other');
SET IDENTITY_INSERT catalog_brand OFF;

SET IDENTITY_INSERT catalog_type ON;
INSERT INTO catalog_type (id, type) VALUES
    (1, 'Mug'),
    (2, 'T-Shirt'),
    (3, 'Sheet'),
    (4, 'USB Memory Stick');
SET IDENTITY_INSERT catalog_type OFF;

INSERT INTO catalog_item (catalog_type_id, catalog_brand_id, description, name, price, picture_uri) VALUES
    (2, 2, '.NET Bot Black Sweatshirt', '.NET Bot Black Sweatshirt', 19.5, 'http://catalogbaseurltobereplaced/images/products/1.png'),
    (1, 2, '.NET Black & White Mug', '.NET Black & White Mug', 8.50, 'http://catalogbaseurltobereplaced/images/products/2.png'),
    (2, 5, 'Prism White T-Shirt', 'Prism White T-Shirt', 12, 'http://catalogbaseurltobereplaced/images/products/3.png'),
    (2, 2, '.NET Foundation Sweatshirt', '.NET Foundation Sweatshirt', 12, 'http://catalogbaseurltobereplaced/images/products/4.png'),
    (3, 5, 'Roslyn Red Sheet', 'Roslyn Red Sheet', 8.5, 'http://catalogbaseurltobereplaced/images/products/5.png'),
    (2, 2, '.NET Blue Sweatshirt', '.NET Blue Sweatshirt', 12, 'http://catalogbaseurltobereplaced/images/products/6.png'),
    (2, 5, 'Roslyn Red T-Shirt', 'Roslyn Red T-Shirt', 12, 'http://catalogbaseurltobereplaced/images/products/7.png'),
    (2, 5, 'Kudu Purple Sweatshirt', 'Kudu Purple Sweatshirt', 8.5, 'http://catalogbaseurltobereplaced/images/products/8.png'),
    (1, 5, 'Cup<T> White Mug', 'Cup<T> White Mug', 12, 'http://catalogbaseurltobereplaced/images/products/9.png'),
    (3, 2, '.NET Foundation Sheet', '.NET Foundation Sheet', 12, 'http://catalogbaseurltobereplaced/images/products/10.png'),
    (3, 2, 'Cup<T> Sheet', 'Cup<T> Sheet', 8.5, 'http://catalogbaseurltobereplaced/images/products/11.png'),
    (2, 5, 'Prism White TShirt', 'Prism White TShirt', 12, 'http://catalogbaseurltobereplaced/images/products/12.png');
