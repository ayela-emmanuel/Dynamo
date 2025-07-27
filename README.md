# Dynamo ORM (MySQL + Dapper)

*A tiny, attribute‑driven micro‑ORM for MySQL that gives you:*

* **POCO entities** with `[Table]`, `[Id]`, `[Column]`, `[Ignore]`
* **Lifecycle hooks**: `[OnStore]` and `[OnRetrieve]`
* **Repository helpers**: CRUD, simple queries, `WHERE`, `FIND`, **pagination**, and **transactions**
* **Schema tools**: generate `CREATE TABLE` SQL and **synchronize schema** (add/modify columns) with optional **lockdown** and **change log**

> Target stack: **.NET**, **Dapper**, **MySqlConnector**

---

## Contents

* [Install](#install)
* [Quick start](#quick-start)
* [Entity model & attributes](#entity-model--attributes)
* [DynamoContext (connection)](#dynamocontext-connection)
* [Repository API](#repository-api)

  * [CRUD](#crud)
  * [Query helpers](#query-helpers)
  * [Pagination](#pagination)
  * [Transactions](#transactions)
* [Schema tools](#schema-tools)

  * [Create table SQL](#create-table-sql)
  * [Schema synchronizer](#schema-synchronizer)
* [SQL type mapping](#sql-type-mapping)
* [JSON / nested data](#json--nested-data)
* [Usage in ASP.NET Core](#usage-in-aspnet-core)
* [Caveats & notes](#caveats--notes)
* [Roadmap](#roadmap)
* [License](#license)

---

## Install

Add the two runtime dependencies:

```bash
dotnet add package Dapper
dotnet add package MySqlConnector
```

Then include the `DynamoOrm` sources (this repo) in your project.

---

## Quick start

```csharp
using DynamoOrm;

[Table("customers")]
public class Customer : DynamoEntity
{
    [Id] public Guid Id { get; set; }

    [Column("first_name")]
    public string FirstName { get; set; }

    [Column("last_name")]
    public string LastName { get; set; }

    public string Email { get; set; }

    [Ignore] // not persisted
    public string FullName => $"{FirstName} {LastName}";

    [OnStore]    void BeforeSave()    { /* e.g., normalize fields */ }
    [OnRetrieve] void AfterRetrieve() { /* e.g., hydrate caches  */ }
}

// Bootstrap
var ctx  = new DynamoContext("Server=...;Database=...;Uid=...;Pwd=...;");
var repo = new DynamoRepository<Customer>(ctx);

// Create
var c = new Customer { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com" };
await repo.InsertAsync(c);

// Read
var fromDb = await repo.GetByIdAsync(c.Id);

// Update
fromDb.Email = "ada.lovelace@example.com";
await repo.UpdateAsync(fromDb);

// Delete
await repo.DeleteAsync(c.Id);
```

---

## Entity model & attributes

All entities inherit from **`DynamoEntity`** (see `DynamoOrm.cs`) which provides:

* `EnsureId()` — assigns a new `Guid` to `[Id]` property if empty
* `RunOnStore()` — invokes methods marked with `[OnStore]`
* `RunOnRetrieve()` — invokes methods marked with `[OnRetrieve]`
* `GetTableName()` — `[Table("name")]` or class name
* `GetIdProperty()` / `GetIdValue()` — reflectively locates the `[Id]` property

**Supported attributes:**

* `[Table("table_name")]` – set table name
* `[Id]` – marks the primary key property (typically `Guid`)
* `[Column("column_name")]` – override column name
* `[DbType("SQL_TYPE")]` – force a SQL type (overrides default mapping)
* `[Ignore]` – exclude a property from persistence
* `[OnStore]` / `[OnRetrieve]` – lifecycle hook methods (no parameters)

---

## DynamoContext (connection)

`DynamoContext` holds a **single** `MySqlConnection` and returns it via `GetConnection()`; it (re)opens if needed.

```csharp
var ctx = new DynamoContext(connectionString);
using var conn = ctx.GetConnection(); // open
```

> **Tip:** Treat `DynamoContext` as **scoped** (e.g., per web request). It isn’t intended to be shared concurrently across threads.

---

## Repository API

Class: `DynamoRepository<T>` where `T : DynamoEntity, new()`.

### CRUD

```csharp
await repo.InsertAsync(entity);
await repo.UpdateAsync(entity);
await repo.InsertOrUpdateAsync(entity);
await repo.DeleteAsync(id);

var one = await repo.GetByIdAsync(id);         // by Guid
var oneForUpdate = await repo.GetByIdAsync(id, forUpdate: true); // adds `FOR UPDATE`
var all = await repo.GetAllAsync();
```

### Query helpers

```csharp
// Simple WHERE (parameterized values via anonymous object)
var customersInLagos = await repo.WhereAsync("City = @City", new { City = "Lagos" });

// Column = value
var byEmail = await repo.FindAsync("Email", "ada@example.com");
```

### Pagination

```csharp
var (records, total) = await repo.PaginateAsync(
    whereSql: "IsActive = 1",
    param: null,
    orderBy: "CreatedAt DESC",
    page: 2,
    pageSize: 20
);
```

Returns a `List<T>` and `Total` count (for building page metadata).

> **Note:** `orderBy` is injected verbatim. Use only **trusted** column names (e.g., constants/enums) to avoid SQL injection.

### Transactions

Two options:

```csharp
// 1) Manual transaction
using var tx = repo.BeginTransaction();
try
{
    await repo.InsertAsync(e1);
    await repo.UpdateAsync(e2);
    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}

// 2) Helper
await repo.WithTransaction(async tx =>
{
    await repo.InsertAsync(e1, tx);
    await repo.UpdateAsync(e2, tx);
});
```

> Use the overloads that accept `IDbTransaction` when performing multiple operations that must commit atomically.

---

## Schema tools

### Create table SQL

`DynamoSchemaBuilder.GenerateCreateTableSql<T>()` produces a MySQL `CREATE TABLE IF NOT EXISTS` statement based on your entity’s properties and attributes.

```csharp
string sql = DynamoSchemaBuilder.GenerateCreateTableSql<Customer>();
// Execute sql using MySqlConnector if you want manual control
```

`GetSqlType(PropertyInfo)` maps .NET types to SQL (see [SQL type mapping](#sql-type-mapping)) and respects `[DbType("...")]`.

A separate helper creates the change‑log table:

```csharp
string logSql = DynamoSchemaBuilder.GetChangeLogTableSql();
```

### Schema synchronizer

`DynamoSchemaSynchronizer` compares your entity definitions with the **actual** MySQL schema and will:

* **Create** tables (if missing)
* **Add** missing columns
* **Modify** column types that differ (if allowed)
* **Log** all changes to `dynamo_schema_change_log` (configurable)

```csharp
var sync = new DynamoSchemaSynchronizer(
    context,
    new SchemaSyncOptions
    {
        Lockdown = false, // execute ALTERs when false
        LogOnly  = true   // still log differences
    },
    typeof(Customer).Assembly // assemblies to scan; defaults to executing assembly
);
await sync.SyncSchemaAsync();
```

**Options**

* `Lockdown = true` → **no** schema changes are executed; differences are only logged.
* `LogOnly = true` → write to `dynamo_schema_change_log` regardless of lockdown.

> The synchronizer uses `INFORMATION_SCHEMA.COLUMNS` to resolve existing columns and types.

---

## SQL type mapping

Default mapping in `DynamoSchemaBuilder.GetSqlType`:

| .NET type    | MySQL type      |
| ------------ | --------------- |
| `Guid`       | `CHAR(36)`      |
| `string`     | `VARCHAR(255)`  |
| `int`        | `INT`           |
| `long`       | `BIGINT`        |
| `bool`       | `TINYINT(1)`    |
| `DateTime`   | `DATETIME`      |
| `double`     | `DOUBLE`        |
| `decimal`    | `DECIMAL(18,2)` |
| *(fallback)* | `TEXT`          |

Override with `[DbType("...")]` to use custom types (e.g., `JSON`, `VARCHAR(1024)`, `DATETIME(6)`, etc.).

---

## JSON / nested data

MySQL supports a native `JSON` column type. You can opt into it using `[DbType("JSON")]`. Two common patterns:

**A) Store as JSON string**

```csharp
public class Order : DynamoEntity
{
    [Id] public Guid Id { get; set; }

    [DbType("JSON")]
    public string Metadata { get; set; } // store serialized JSON
}
```

**B) Dual property with lifecycle hooks**

```csharp
public class Order : DynamoEntity
{
    [Id] public Guid Id { get; set; }

    [DbType("JSON")]
    [Column("metadata")]
    public string MetadataRaw { get; set; } // stored

    [Ignore]
    public Dictionary<string, object> Metadata { get; set; } // convenient shape

    [OnStore]
    void BeforeSave() => MetadataRaw = JsonSerializer.Serialize(Metadata);

    [OnRetrieve]
    void AfterLoad()
      => Metadata = string.IsNullOrEmpty(MetadataRaw)
         ? new()
         : JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataRaw);
}
```

---

## Usage in ASP.NET Core

Register **scoped** services:

```csharp
builder.Services.AddScoped(provider =>
    new DynamoContext(builder.Configuration.GetConnectionString("MySql")));

builder.Services.AddScoped(typeof(DynamoRepository<>));
builder.Services.AddScoped<ISchemaSynchronizer>(sp =>
    new DynamoSchemaSynchronizer(
        sp.GetRequiredService<DynamoContext>(),
        new SchemaSyncOptions { Lockdown = false, LogOnly = true },
        typeof(Program).Assembly));
```

Then inject and use:

```csharp
public class CustomersService
{
    private readonly DynamoRepository<Customer> _repo;
    private readonly ISchemaSynchronizer _sync;

    public CustomersService(DynamoRepository<Customer> repo, ISchemaSynchronizer sync)
    {
        _repo = repo;
        _sync = sync;
    }

    public async Task InitializeAsync() => await _sync.SyncSchemaAsync();
}
```

---

## Caveats & notes

* **Connection scope:** `DynamoContext` caches a single `MySqlConnection`. Prefer **scoped lifetime** and avoid sharing across threads. Dispose the context when the scope ends.
* **Transaction usage:** When performing multiple operations in a transaction, use the overloads that accept `IDbTransaction`. Ensure all operations share the same underlying connection.
* **OrderBy injection:** `PaginateAsync(orderBy: ...)` interpolates directly. Validate/whitelist your sort columns.
* **Schema sync safety:** `Lockdown = true` is recommended in production to **review** differences before enabling automatic `ALTER TABLE`.
* **Indexes, FKs, constraints:** Current schema tools handle **columns** (create/add/modify). Indexes/constraints are not managed yet—apply them with migrations or manual SQL.
* **Composite keys:** Not supported out of the box (single `[Id]` property expected).
* **Type coverage:** Use `[DbType]` to override mapping for types not listed in the default map (e.g., `JSON`, `DATETIME(6)`, `VARCHAR(1024)`).

---

## Roadmap

* Index/constraint generation (unique, FK, composite keys)
* Soft delete & audit helpers
* Concurrency control (rowversion / timestamp)
* Filter builder for strongly‑typed predicates
* Bulk operations
* Pluggable naming conventions (snake\_case, etc.)

---

## License

MIT (or your preferred OSS license). Add a `LICENSE` file at the repo root.

---

## File map

* `DynamoContext.cs` — MySQL connection management
* `DynamoOrm.cs` — attributes & `DynamoEntity` base (hooks, id, table name)
* `DynamoRepository.cs` — CRUD, queries, pagination, transactions
* `DynamoSchemaBuilder.cs` — `CREATE TABLE` SQL builder, type mapping, change log table
* `SchemaSynchronizer.cs` — assembly scanner, table/column add/modify, change logging with options

---

### Troubleshooting

* **“Command denied to user … ALTER TABLE …”**
  Your DB user lacks DDL privileges. Use `Lockdown = true` to log diffs and run DDL as a privileged user.
* **“There is already an open DataReader …”**
  Ensure you await reads/writes and avoid parallel operations on the same connection.
* **Transaction errors**
  Make sure all repository calls within a transaction use the same connection and pass the same `IDbTransaction`.

---

*Happy shipping!* 🚀
