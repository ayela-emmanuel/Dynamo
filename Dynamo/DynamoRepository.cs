using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DynamoOrm
{
    public class DynamoRepository<T> where T : DynamoEntity, new()
    {
        private readonly DynamoContext _context;

        public DynamoRepository(DynamoContext context)
        {
            _context = context;
        }

        public async Task InsertAsync(T entity)
        {
            entity.EnsureId();
            entity.RunOnStore();

            var type = typeof(T);
            var table = entity.GetTableName();
            var props = type.GetProperties().Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null && 
            (p.GetCustomAttribute<DbTypeAttribute>() != null || p.GetCustomAttribute<ColumnAttribute>() != null || p.CanWrite));

            var columns = props.Select(p => p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name).ToList();
            var paramNames = props.Select(p => "@" + p.Name).ToList();

            var sql = $"INSERT INTO `{table}` ({string.Join(",", columns)}) VALUES ({string.Join(",", paramNames)})";

            using var conn = _context.GetConnection();
            await conn.ExecuteAsync(sql, entity);
        }

        public async Task UpdateAsync(T entity)
        {
            entity.RunOnStore();

            var type = typeof(T);
            var table = entity.GetTableName();
            var props = type.GetProperties().Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null && p.GetCustomAttribute<DbTypeAttribute>() != null || p.GetCustomAttribute<ColumnAttribute>() != null || p.CanWrite);
            var idProp = entity.GetIdProperty();

            var setClause = props.Where(p => p != idProp)
                                 .Select(p =>
                                     $"{(p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name)} = @{p.Name}");

            var sql = $"UPDATE `{table}` SET {string.Join(",", setClause)} WHERE {idProp.Name} = @{idProp.Name}";

            using var conn = _context.GetConnection();
            await conn.ExecuteAsync(sql, entity);
        }

        public async Task<T> GetByIdAsync(Guid id)
        {
            var entity = new T();
            var table = entity.GetTableName();
            var idProp = entity.GetIdProperty();
            var sql = $"SELECT * FROM `{table}` WHERE {idProp.Name} = @Id";

            using var conn = _context.GetConnection();
            var result = await conn.QuerySingleOrDefaultAsync<T>(sql, new { Id = id });

            result?.RunOnRetrieve();
            return result;
        }

        public async Task<List<T>> GetAllAsync()
        {
            var entity = new T();
            var table = entity.GetTableName();
            var sql = $"SELECT * FROM `{table}`";

            using var conn = _context.GetConnection();
            var results = (await conn.QueryAsync<T>(sql)).ToList();
            results.ForEach(e => e.RunOnRetrieve());
            return results;
        }

        public async Task InsertAsync(T entity, IDbTransaction tx = null)
        {
            entity.EnsureId();
            entity.RunOnStore();
            var (sql, param) = BuildInsert(entity);

            using var conn = _context.GetConnection();
            await conn.ExecuteAsync(sql, param, tx);
        }

        public async Task UpdateAsync(T entity, IDbTransaction tx = null)
        {
            entity.RunOnStore();
            var (sql, param) = BuildUpdate(entity);
            using var conn = _context.GetConnection();
            await conn.ExecuteAsync(sql, param, tx);
        }

        public async Task InsertOrUpdateAsync(T entity)
        {
            var exists = await GetByIdAsync((Guid)entity.GetIdValue());
            if (exists != null)
                await UpdateAsync(entity);
            else
                await InsertAsync(entity);
        }

        public async Task DeleteAsync(Guid id)
        {
            var entity = new T();
            var table = entity.GetTableName();
            var idCol = entity.GetIdProperty().Name;

            var sql = $"DELETE FROM `{table}` WHERE `{idCol}` = @Id";
            using var conn = _context.GetConnection();
            await conn.ExecuteAsync(sql, new { Id = id });
        }

        public async Task<T> GetByIdAsync(Guid id, bool forUpdate = false)
        {
            var entity = new T();
            var table = entity.GetTableName();
            var idProp = entity.GetIdProperty();
            var sql = $"SELECT * FROM `{table}` WHERE {idProp.Name} = @Id";

            if (forUpdate)
                sql += " FOR UPDATE";

            using var conn = _context.GetConnection();
            var result = await conn.QuerySingleOrDefaultAsync<T>(sql, new { Id = id });

            result?.RunOnRetrieve();
            return result;
        }

        public async Task<List<T>> WhereAsync(string whereSql, object param = null)
        {
            var entity = new T();
            var table = entity.GetTableName();
            var sql = $"SELECT * FROM `{table}` WHERE {whereSql}";

            using var conn = _context.GetConnection();
            var result = (await conn.QueryAsync<T>(sql, param)).ToList();
            result.ForEach(e => e.RunOnRetrieve());
            return result;
        }

        public async Task<T> FindAsync(string column, object value)
        {
            var entity = new T();
            var table = entity.GetTableName();
            var sql = $"SELECT * FROM `{table}` WHERE `{column}` = @Value";

            using var conn = _context.GetConnection();
            var result = await conn.QuerySingleOrDefaultAsync<T>(sql, new { Value = value });
            result?.RunOnRetrieve();
            return result;
        }

        public IDbTransaction BeginTransaction()
        {
            var conn = _context.GetConnection();
            return conn.BeginTransaction();
        }

        public async Task WithTransaction(Func<IDbTransaction, Task> action)
        {
            using var conn = _context.GetConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                await action(tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private (string sql, object param) BuildInsert(T entity)
        {
            var type = typeof(T);
            var table = entity.GetTableName();
            var props = type.GetProperties().Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null);

            var columns = props.Select(p => p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name).ToList();
            var paramNames = props.Select(p => "@" + p.Name).ToList();

            var sql = $"INSERT INTO `{table}` ({string.Join(",", columns)}) VALUES ({string.Join(",", paramNames)})";
            return (sql, entity);
        }

        private (string sql, object param) BuildUpdate(T entity)
        {
            var type = typeof(T);
            var table = entity.GetTableName();
            var props = type.GetProperties().Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null);
            var idProp = entity.GetIdProperty();

            var setClause = props.Where(p => p != idProp)
                                 .Select(p => $"{(p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name)} = @{p.Name}");

            var sql = $"UPDATE `{table}` SET {string.Join(",", setClause)} WHERE {idProp.Name} = @{idProp.Name}";
            return (sql, entity);
        }

        public async Task<(List<T> Records, int Total)> PaginateAsync(
        string whereSql = null,
        object param = null,
        string orderBy = "Id",
        int page = 1,
        int pageSize = 10)
        {
            var entity = new T();
            var table = entity.GetTableName();

            var offset = (page - 1) * pageSize;
            var whereClause = string.IsNullOrWhiteSpace(whereSql) ? "1=1" : whereSql;

            var sql = $@"
            SELECT * FROM `{table}` 
            WHERE {whereClause}
            ORDER BY {orderBy}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM `{table}` WHERE {whereClause};
            ";

            using var conn = _context.GetConnection();
            using var multi = await conn.QueryMultipleAsync(sql, new
            {
                PageSize = pageSize,
                Offset = offset,
                param
            });

            var records = (await multi.ReadAsync<T>()).ToList();
            records.ForEach(e => e.RunOnRetrieve());
            var total = await multi.ReadSingleAsync<int>();

            return (records, total);
        }

        private object GetSerializableData(T entity)
        {
            var props = typeof(T).GetProperties()
                                 .Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null);

            var dict = new Dictionary<string, object>();
            foreach (var prop in props)
            {
                dict[prop.Name] = prop.GetValue(entity);
            }
            return dict;
        }

    }

}
