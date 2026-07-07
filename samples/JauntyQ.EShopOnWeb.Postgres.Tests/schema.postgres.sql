-- eShopOnWeb data-layer port (torture test, Part 1). Seven tables covering
-- Catalog/Basket/Order aggregates from dotnet-architecture/eShopOnWeb's
-- ApplicationCore, with EF Core's owned-type value objects (Address on
-- Order, CatalogItemOrdered on OrderItem) flattened onto their owning
-- table's columns -- see docs/torture-test-log.md "Part 1 kickoff scope
-- decisions" for why (JauntyQ has no owned-type/nested-object mapping).
-- Seed data (5 brands, 4 types, 12 catalog items) is copied verbatim from
-- the source app's CatalogContextSeed.cs.

CREATE TABLE catalog_brand (
    id     SERIAL PRIMARY KEY,
    brand  TEXT NOT NULL
);

CREATE TABLE catalog_type (
    id    SERIAL PRIMARY KEY,
    type  TEXT NOT NULL
);

CREATE TABLE catalog_item (
    id                 SERIAL PRIMARY KEY,
    catalog_type_id    INTEGER NOT NULL REFERENCES catalog_type(id),
    catalog_brand_id   INTEGER NOT NULL REFERENCES catalog_brand(id),
    description        TEXT NOT NULL,
    name               TEXT NOT NULL,
    price              NUMERIC(18,2) NOT NULL,
    picture_uri        TEXT NOT NULL
);

CREATE TABLE basket (
    id        SERIAL PRIMARY KEY,
    buyer_id  TEXT NOT NULL
);

CREATE TABLE basket_item (
    id                SERIAL PRIMARY KEY,
    basket_id         INTEGER NOT NULL REFERENCES basket(id),
    catalog_item_id   INTEGER NOT NULL REFERENCES catalog_item(id),
    unit_price        NUMERIC(18,2) NOT NULL,
    quantity          INTEGER NOT NULL
);

-- Table named `orders`, not `order` (reserved word).
CREATE TABLE orders (
    id                SERIAL PRIMARY KEY,
    buyer_id          TEXT NOT NULL,
    order_date        TIMESTAMPTZ NOT NULL,
    ship_to_street    TEXT NOT NULL,
    ship_to_city      TEXT NOT NULL,
    ship_to_state     TEXT NOT NULL,
    ship_to_country   TEXT NOT NULL,
    ship_to_zipcode   TEXT NOT NULL
);

CREATE TABLE order_item (
    id                        SERIAL PRIMARY KEY,
    order_id                  INTEGER NOT NULL REFERENCES orders(id),
    unit_price                NUMERIC(18,2) NOT NULL,
    units                     INTEGER NOT NULL,
    ordered_catalog_item_id   INTEGER NOT NULL,
    ordered_product_name      TEXT NOT NULL,
    ordered_picture_uri       TEXT NOT NULL
);

INSERT INTO catalog_brand (id, brand) VALUES
    (1, 'Azure'),
    (2, '.NET'),
    (3, 'Visual Studio'),
    (4, 'SQL Server'),
    (5, 'Other');

INSERT INTO catalog_type (id, type) VALUES
    (1, 'Mug'),
    (2, 'T-Shirt'),
    (3, 'Sheet'),
    (4, 'USB Memory Stick');

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
