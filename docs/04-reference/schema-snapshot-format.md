# Schema snapshot format (`.schema.json`)

The snapshot is the committed contract between your database and the generator.
`jauntyq schema pull` writes it; the generator reads it at build time and never
touches a database. This page documents the on-disk JSON structure.

## Serialization

- **System.Text.Json**, written indented (`WriteIndented = true`).
- Property names are **camelCase**; deserialization is case-insensitive.
- Optional fields are **omitted when null** (`maxLength`, `precision`, `scale`,
  `isUnicode`, and the sequence `minValue`/`maxValue`/`currentValue`).
- `bool` facets that default to `false` (`isNullable`, `isPrimaryKey`,
  `isIdentity`, `isRowVersion`) are always written.
- Enums are written as strings (procedure-parameter `direction`).

There is no explicit format-version field; the shape is versioned by the
tool/generator pair. Snapshots pulled with an older CLI still load, fields
added later (index metadata, value-safety facets) simply read as absent until
you re-pull.

## Top-level object

```json
{
  "dialect": "sqlserver",
  "tables": { "<name>": { /* TableSchema */ } },
  "foreignKeys": [ { /* ForeignKeySchema */ } ],
  "procedures": { "<name>": { /* ProcedureSchema */ } },
  "sequences": { "<name>": { /* SequenceSchema */ } }
}
```

| Key | Type | Notes |
|---|---|---|
| `dialect` | string | One of `sqlserver`, `postgres`, `mysql`, `sqlite`. Drives dialect-specific SQL synthesis. |
| `tables` | object | Tables keyed by name. |
| `foreignKeys` | array | Flat list of FK relationships. |
| `procedures` | object | Stored procedures keyed by name. Empty for SQLite. |
| `sequences` | object | Sequence objects keyed by name. Populated only for SQL Server and PostgreSQL; empty otherwise. |

## Table

```json
{
  "name": "Products",
  "columns": { "<name>": { /* ColumnSchema */ } },
  "indexes": [ { /* IndexSchema */ } ]
}
```

| Key | Type | Notes |
|---|---|---|
| `name` | string | Table name. |
| `columns` | object | Columns keyed by name. |
| `indexes` | array | Indexes, key columns in key order. Absent/empty for snapshots pulled before index capture; the `JNT8004` unindexed-filter check only sees indexes when present. |

## Column

```json
{
  "name": "ProductName",
  "dbType": "varchar",
  "isNullable": false,
  "isPrimaryKey": false,
  "isIdentity": false,
  "maxLength": 40,
  "precision": null,
  "scale": null,
  "isUnicode": false,
  "isRowVersion": false,
  "isComputed": false
}
```

| Key | Type | Notes |
|---|---|---|
| `name` | string | Column name. |
| `dbType` | string | Database type name (e.g. `int`, `varchar`, `decimal`, `uniqueidentifier`). |
| `isNullable` | bool | Column allows NULL. |
| `isPrimaryKey` | bool | Part of the primary key. |
| `isIdentity` | bool | Auto-increment / identity column. |
| `maxLength` | int? | String/binary length; `-1` = unbounded (`varchar(max)`, `text`, `bytea`). Omitted when not applicable. |
| `precision` | int? | Numeric total digits. Omitted when not applicable. |
| `scale` | int? | Numeric fractional digits. Omitted when not applicable. |
| `isUnicode` | bool? | `true` for Unicode text (`nvarchar`/`nchar`), `false` for single-byte, omitted for non-text. |
| `isRowVersion` | bool | Database-maintained concurrency token (SQL Server `rowversion`/`timestamp`). Kept as an explicit flag because SQL Server reports `rowversion` under the type name `timestamp`. |
| `isComputed` | bool | Computed/generated column (SQL Server `AS ...` [PERSISTED], PostgreSQL `GENERATED ALWAYS AS (...) STORED`, MySQL `GENERATED ALWAYS AS (...)`). The database rejects an INSERT/UPDATE targeting it, so auto-CRUD and BulkInsert exclude it from their column/value lists the same way they exclude `isIdentity`/`isRowVersion` columns. |

The `maxLength`, `precision`, `scale`, and `isUnicode` facets feed the
value-safety layer (compile-time literal checks, client-side write guards, and
stable-plan parameter sizing).

## Index

```json
{ "name": "IX_Products_CategoryId", "columns": ["CategoryId"], "isUnique": false }
```

| Key | Type | Notes |
|---|---|---|
| `name` | string | Index name. |
| `columns` | array of string | Key columns in key order. Only the leading column makes a filter seekable. For an index with `hasExpressionKeyPart`, this is only the real-column subset of the key, in relative order, possibly empty. |
| `isUnique` | bool | Whether the index enforces uniqueness. |
| `hasPrefixKeyPart` | bool | Any key part is a MySQL/MariaDB column prefix (`INFORMATION_SCHEMA.STATISTICS.SUB_PART`), e.g. `UNIQUE (email(5))`. `columns` still lists the full column names, but uniqueness is enforced over the truncated prefix only, never treat the index as equivalent to (or redundant against) the full-column index of the same columns. Absent/false in snapshots that predate the capture (2026-07-30). |
| `hasExpressionKeyPart` | bool | Any key part is an expression (MySQL functional, PostgreSQL/SQLite expression index). The model cannot carry the expression, so `columns` must never be read as the full key: no seekability, coverage, or uniqueness conclusion may be drawn from it. A flagged unique index still proves a competing unique constraint exists, the reason it is captured at all. Absent/false in snapshots that predate the capture, which omitted such indexes entirely. |

Tables *defined in* migration or DDL files do not carry secondary
unique-index metadata (inline UNIQUE constraints and `CREATE [UNIQUE] INDEX`
statements are not simulated). Indexes captured in a pulled snapshot survive
migration simulation unchanged.

## Foreign key

```json
{ "fromTable": "Products", "fromColumn": "CategoryId", "toTable": "Categories", "toColumn": "CategoryId" }
```

Each entry drives one FK loader (`db.Products.GetByCategoryId(...)`).

## Procedure

```json
{
  "name": "GetProductsByCategory",
  "params": [
    { "name": "cat_id", "dbType": "int", "direction": "In", "isNullable": false }
  ],
  "results": [
    { "name": "product_id", "dbType": "int", "isNullable": false },
    { "name": "product_name", "dbType": "varchar", "isNullable": false, "maxLength": 40 }
  ]
}
```

| Key | Type | Notes |
|---|---|---|
| `name` | string | Procedure name. |
| `params` | array | Parameters in declaration order (see below). |
| `results` | array of Column | Columns of the first result set, in ordinal order. Empty when the procedure returns no rows. Reuses the column shape. |

### Procedure parameter

| Key | Type | Notes |
|---|---|---|
| `name` | string | Parameter name. |
| `dbType` | string | Database type name. |
| `direction` | string | `In`, `Out`, `InOut`, or `ReturnValue`. `ReturnValue` is accepted but never produced by `jauntyq schema pull`, none of the three extractors reads a procedure's return status, so it reaches the generator only from a hand-authored or externally produced snapshot. It generates the same shape `Out` does: an `out` parameter on the sync overload, a tuple element on the async one. |
| `isNullable` | bool | Whether NULL is allowed. |
| `maxLength` | int? | String/binary length; omitted when not applicable. |

Procedures are consumed by the [`-- @call`](directives.md#-call) directive to
emit typed `CommandType.StoredProcedure` calls.

## Sequence

```json
{ "name": "order_number", "startValue": 100, "increment": 5,
  "minValue": null, "maxValue": null, "currentValue": null }
```

| Key | Type | Notes |
|---|---|---|
| `name` | string | Sequence name. |
| `startValue` | long | Start value. |
| `increment` | long | Increment step. |
| `minValue` | long? | Minimum, omitted when not enforced. |
| `maxValue` | long? | Maximum, omitted when not enforced. |
| `currentValue` | long? | Last value at snapshot time; omitted when the catalog does not expose it. |

Present only for SQL Server (`sys.sequences`) and PostgreSQL. Each entry
generates a typed `db.Sequences.Next{Name}()` accessor, see the
[sequences guide](../03-guides/sequences.md).

## Minimal example

```json
{
  "dialect": "sqlserver",
  "tables": {
    "Products": {
      "name": "Products",
      "columns": {
        "ProductId":   { "name": "ProductId", "dbType": "int", "isNullable": false, "isPrimaryKey": true, "isIdentity": true },
        "ProductName": { "name": "ProductName", "dbType": "nvarchar", "isNullable": false, "maxLength": 40, "isUnicode": true }
      }
    }
  },
  "foreignKeys": []
}
```
