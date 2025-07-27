using Dapper;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DynamoOrm
{
    public interface ISchemaSynchronizer
    {
        Task SyncSchemaAsync();
    }

    public class DynamoSchemaSynchronizer : ISchemaSynchronizer
    {
        private readonly DynamoContext _context;
        private readonly Assembly[] _assemblies;
        private readonly SchemaSyncOptions _options;

        public DynamoSchemaSynchronizer(DynamoContext context, SchemaSyncOptions options = null, params Assembly[] assembliesToScan)
        {
            _context = context;
            _options = options ?? new SchemaSyncOptions();
            _assemblies = assembliesToScan.Length > 0 ? assembliesToScan : new[] { Assembly.GetExecutingAssembly() };
        }

        public async Task SyncSchemaAsync()
        {
            var entityTypes = _assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(DynamoEntity).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var type in entityTypes)
            {
                await SyncEntitySchemaAsync(type);
            }

            // Optional: ensure the mutation log table exists
            using var conn = _context.GetConnection();
            await conn.ExecuteAsync(DynamoSchemaBuilder.GetChangeLogTableSql());
        }

        private async Task SyncEntitySchemaAsync(Type entityType)
        {
            var tableName = entityType.GetCustomAttribute<TableAttribute>()?.Name ?? entityType.Name;
            var conn = _context.GetConnection();

            var props = entityType.GetProperties()
                .Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null);

            // Get actual columns and their data types
            var actualColumns = (await conn.QueryAsync<(string COLUMN_NAME, string COLUMN_TYPE)>(
                @"SELECT COLUMN_NAME, COLUMN_TYPE
          FROM INFORMATION_SCHEMA.COLUMNS
          WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = @TableName", new { TableName = tableName }))
                .ToDictionary(c => c.COLUMN_NAME, c => c.COLUMN_TYPE, StringComparer.OrdinalIgnoreCase);

            if (!actualColumns.Any())
            {
                // Create table and skip logging for initial creation
                if (!_options.Lockdown)
                {
                    var createSql = DynamoSchemaBuilder.GenerateCreateTableSql(entityType);
                    await conn.ExecuteAsync(createSql);
                    return;
                }
                
            }

            foreach (var prop in props)
            {
                var columnName = prop.GetCustomAttribute<ColumnAttribute>()?.Name ?? prop.Name;
                var expectedType = DynamoSchemaBuilder.GetSqlType(prop);

                if (!actualColumns.ContainsKey(columnName))
                {
                    if (!_options.Lockdown)
                    {
                        var alter = $"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {expectedType};";
                        await conn.ExecuteAsync(alter);
                    }

                    if (_options.LogOnly || _options.Lockdown)
                    {
                        await LogSchemaChange(tableName, columnName, "AddColumn", null, expectedType);
                    }
                }
                else
                {
                    var actualType = actualColumns[columnName];

                    if (!string.Equals(actualType, expectedType, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_options.LogOnly || _options.Lockdown)
                        {
                            await LogSchemaChange(tableName, columnName, "ModifyColumn", actualType, expectedType);
                        }

                        if (!_options.Lockdown)
                        {
                            var modifySql = $"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` {expectedType};";
                            await conn.ExecuteAsync(modifySql);
                        }
                    }
                }
            }
        }

        private async Task LogSchemaChange(string table, string column, string changeType, string oldType, string newType)
        {
            var logSql = @"
INSERT INTO dynamo_schema_change_log (Id, TableName, ColumnName, ChangeType, OldType, NewType)
VALUES (@Id, @TableName, @ColumnName, @ChangeType, @OldType, @NewType);";

            await _context.GetConnection().ExecuteAsync(logSql, new
            {
                Id = Guid.NewGuid(),
                TableName = table,
                ColumnName = column,
                ChangeType = changeType,
                OldType = oldType,
                NewType = newType
            });
        }



    }

    public class SchemaSyncOptions
    {
        /// <summary>
        /// If true, schema changes (CREATE, ALTER) will not be executed — only logged.
        /// </summary>
        public bool Lockdown { get; set; } = false;

        /// <summary>
        /// If true, logs will still be recorded for schema differences even in lockdown.
        /// </summary>
        public bool LogOnly { get; set; } = true;
    }

}
