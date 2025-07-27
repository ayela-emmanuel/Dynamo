using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DynamoOrm
{
    public static class DynamoSchemaBuilder
    {
        public static string GenerateCreateTableSql<T>() where T : DynamoEntity
        {
            var type = typeof(T);
            var tableName = type.GetCustomAttribute<TableAttribute>()?.Name ?? type.Name;
            var sb = new StringBuilder();

            sb.AppendLine($"CREATE TABLE IF NOT EXISTS `{tableName}` (");

            var props = type.GetProperties()
                .Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null);

            var columnDefs = props.Select(p =>
            {
                string columnName = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
                string sqlType = GetSqlType(p);

                string isPrimaryKey = p.GetCustomAttribute<IdAttribute>() != null ? " PRIMARY KEY" : "";
                return $"  `{columnName}` {sqlType}{isPrimaryKey}";
            });

            sb.AppendLine(string.Join(",\n", columnDefs));
            sb.AppendLine(");");

            return sb.ToString();
        }
        public static string GenerateCreateTableSql(Type type)
        {
            if (!typeof(DynamoEntity).IsAssignableFrom(type))
                throw new ArgumentException($"Type {type.Name} does not inherit from DynamoEntity.");

            var tableName = type.GetCustomAttribute<TableAttribute>()?.Name ?? type.Name;
            var sb = new StringBuilder();

            sb.AppendLine($"CREATE TABLE IF NOT EXISTS `{tableName}` (");

            var props = type.GetProperties()
                .Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null);

            var columnDefs = props.Select(p =>
            {
                string columnName = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
                string sqlType = GetSqlType(p);
                string isPrimaryKey = p.GetCustomAttribute<IdAttribute>() != null ? " PRIMARY KEY" : "";
                return $"  `{columnName}` {sqlType}{isPrimaryKey}";
            });

            sb.AppendLine(string.Join(",\n", columnDefs));
            sb.AppendLine(");");

            return sb.ToString();
        }
        public static string GetSqlType(PropertyInfo prop)
        {
            var dbTypeAttr = prop.GetCustomAttribute<DbTypeAttribute>();
            if (dbTypeAttr != null)
                return dbTypeAttr.SqlType;

            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            return type == typeof(Guid) ? "CHAR(36)"
                 : type == typeof(string) ? "VARCHAR(255)"
                 : type == typeof(int) ? "INT"
                 : type == typeof(long) ? "BIGINT"
                 : type == typeof(bool) ? "TINYINT(1)"
                 : type == typeof(DateTime) ? "DATETIME"
                 : type == typeof(double) ? "DOUBLE"
                 : type == typeof(decimal) ? "DECIMAL(18,2)"
                 : "TEXT"; // fallback
        }

        public static string GetChangeLogTableSql()
        {
            return @"
            CREATE TABLE IF NOT EXISTS `dynamo_schema_change_log` (
            `Id` CHAR(36) PRIMARY KEY,
            `TableName` VARCHAR(255) NOT NULL,
            `ColumnName` VARCHAR(255),
            `ChangeType` ENUM('AddColumn', 'ModifyColumn') NOT NULL,
            `OldType` VARCHAR(255),
            `NewType` VARCHAR(255),
            `Timestamp` DATETIME DEFAULT CURRENT_TIMESTAMP
            );";
        }
    }
}
