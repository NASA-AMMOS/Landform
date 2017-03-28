using System;
using System.Collections.Generic;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    /// <summary>
    /// Helper class for connecting to the landform database with Entity Framwork
    /// </summary>
    public class LandformDatabase
    {
        string connectionString;

        /// <summary>
        /// Construct this object and create the connection string used to connect to the database
        /// Currenlty only works with MS Sql server 
        /// </summary>
        /// <param name="location">"Url,port" of database server</param>
        /// <param name="databaseName">Name of database to connect to</param>
        /// <param name="user">DB username</param>
        /// <param name="password">DB password</param>
        public LandformDatabase(string location, string databaseName, string user, string password)
        {
            SqlConnectionStringBuilder sqlBuilder = new SqlConnectionStringBuilder();
            sqlBuilder.DataSource = location;      
            sqlBuilder.InitialCatalog = databaseName;
            sqlBuilder.UserID = user;
            sqlBuilder.Password = password;
            EntityConnectionStringBuilder entityBuilder = new EntityConnectionStringBuilder();
            entityBuilder.Provider = "System.Data.SqlClient";
            entityBuilder.ProviderConnectionString = sqlBuilder.ToString();
            this.connectionString = sqlBuilder.ConnectionString.ToString();
        }

        /// <summary>
        /// Creates a connection to the database and returns a context object
        /// This object should be wrapped in a "using" statement
        /// </summary>
        /// <returns>Context object, wrap it in a "using" statement</returns>
        public LandformDbContext CreateContext()
        {
            return new LandformDbContext(this.connectionString);
        }
    }
}
