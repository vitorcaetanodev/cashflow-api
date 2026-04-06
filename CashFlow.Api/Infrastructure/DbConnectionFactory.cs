using Npgsql;
using System.Data;

namespace CashFlow.Api.Infrastructure;

public class DbConnectionFactory
{
   private readonly string _connectionString;

   public DbConnectionFactory(IConfiguration config)
   {
       _connectionString = config.GetConnectionString("Default");
   }

   public IDbConnection Create() => new NpgsqlConnection(_connectionString);
}
