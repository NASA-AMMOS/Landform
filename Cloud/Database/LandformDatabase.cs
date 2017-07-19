using MySql.Data.Entity;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Migrations;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    /// <summary>
    /// Helper class for connecting to the landform database with Entity Framwork
    /// 
    /// IMPORTANT NOTE!  For any EntityFramework stuff to work with mysql you must do 
    /// the following magic encantation on your app.config files
    /// https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework60.html
    /// 
    /// </summary>
    public class LandformDatabase
    {
        string connectionString;
        
        static LandformDatabase()
        {
            DbConfiguration.SetConfiguration(new MySqlEFConfiguration());
        }

        /// <summary>
        /// Construct this object and create the connection string used to connect to the database
        /// Currenlty only works with MS Sql server 
        /// </summary>
        /// <param name="server">Url of database server</param>
        /// <param name="databaseName">Name of database to connect to</param>
        /// <param name="user">DB username</param>
        /// <param name="password">DB password</param>
        public LandformDatabase(string server, uint port, string databaseName, string user, string password)
        {
            MySqlConnectionStringBuilder sqlBuilder = new MySqlConnectionStringBuilder();
            sqlBuilder.Server = server;
            sqlBuilder.Port = port;
            sqlBuilder.Database = databaseName;
            sqlBuilder.UserID = user;
            sqlBuilder.Password = password;
            this.connectionString = sqlBuilder.ConnectionString;
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
