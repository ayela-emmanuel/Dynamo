using MySqlConnector;
using System;
using System.Data;

namespace DynamoOrm
{
    public class DynamoContext : IDisposable
    {
        private readonly string _connectionString;
        private MySqlConnection _connection;

        public DynamoContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection GetConnection()
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection?.Dispose(); 
                _connection = new MySqlConnection(_connectionString);
                _connection.Open();
            }

            return _connection;
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
