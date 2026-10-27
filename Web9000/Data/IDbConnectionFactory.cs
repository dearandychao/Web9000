using Microsoft.Data.SqlClient;
using System.Data;

namespace Web9000.Data
{
    public interface IDbConnectionFactory
    {
        IDbConnection GetWeb9000Connection();
        IDbConnection GetGDTekConnection();
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public DbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection GetWeb9000Connection()
        {
            return new SqlConnection(_configuration.GetConnectionString("Web9000"));
        }

        public IDbConnection GetGDTekConnection()
        {
            return new SqlConnection(_configuration.GetConnectionString("GDTek"));
        }
    }
}