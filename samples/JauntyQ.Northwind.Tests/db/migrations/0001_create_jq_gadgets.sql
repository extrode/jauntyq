-- Pending migration: JauntyQ generates the typed JQGadgets API from this
-- file before the table exists in the database. Tier3LiveTests executes
-- this DDL against Northwind at test time and drops the table afterwards.
-- Keep in sync with Tier3LiveTests.MigrationDdl.
create table JQ_Gadgets (
    gadget_id int not null primary key identity(1,1),
    name nvarchar(40) not null,
    price decimal(10,2) null,
    row_version rowversion not null
)
