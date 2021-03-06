﻿//-----------------------------------------------------------------------
// Firebird database connection wrapper
//-----------------------------------------------------------------------

namespace APSIM.Shared.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using FirebirdSql.Data.FirebirdClient;

    /// <summary>
    /// A wrapper for a Firebird database connection
    /// </summary>
    [Serializable]
    public class Firebird : IDatabaseConnection
    {
        /// <summary>
        /// Firebird connection properties 
        /// </summary>
        private FbConnection fbDBConnection = new FbConnection();

        private DataSet fbDBDataSet = new DataSet();

        //Set the ServerType to 1 for connect to the embedded server
        private FbServerType fbDBServerType = FbServerType.Embedded;

        /// <summary>Property to return true if the database is open.</summary>
        /// <value><c>true</c> if this instance is open; otherwise, <c>false</c>.</value>
        public bool IsOpen { get { return fbDBConnection.State == ConnectionState.Open; } }

        /// <summary>Property to return true if the database is readonly.</summary>
        public bool IsReadOnly { get; private set; }

        /// <summary>Opens or creates Firebird database with the specified path</summary>
        /// <param name="path">Path to Firebird database</param>
        /// <param name="readOnly">if set to <c>true</c> [read only].</param>
        /// <exception cref="FirebirdException"></exception>
        public void OpenDatabase(string path, bool readOnly)
        {
            if (!readOnly)
            {
                FbConnection.CreateDatabase(GetConnectionString(path, "localhost", "SYSDBA", "masterkey"));
            }
            OpenSQLConnection(path, "localhost", "SYSDBA", "masterkey");

            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Build a connection string
        /// </summary>
        /// <param name="dbpath"></param>
        /// <param name="source"></param>
        /// <param name="user"></param>
        /// <param name="pass"></param>
        /// <returns></returns>
        protected string GetConnectionString(string dbpath, string source, string user, string pass)
        {
            FbConnectionStringBuilder cs = new FbConnectionStringBuilder();

            // If Not fbDBServerType = FbServerType.Embedded Then
            cs.DataSource = source;
            cs.Password = pass;
            cs.UserID = user;
            cs.Port = 3050;
            // End If

            cs.Pooling = false;
            cs.Database = dbpath;
            cs.Charset = "UNICODE_FSS";
            cs.ConnectionLifeTime = 30;
            cs.ServerType = fbDBServerType;

            fbDBDataSet.Locale = CultureInfo.InvariantCulture;
            string connstr = cs.ToString();

            if (cs != null)
            {
                cs = null;
            }
            return connstr;
        }

        /// <summary>Closes the Firebird database</summary>
        public void CloseDatabase()
        {
            if (fbDBConnection.State == ConnectionState.Open)
            {
                fbDBConnection.Close();
            }
        }

        /// <summary>
        /// Open the Firebird SQL connection
        /// </summary>
        /// <param name="dbpath">Path to database</param>
        /// <param name="source">localhost or server name</param>
        /// <param name="user">db user name</param>
        /// <param name="pass">db password</param>
        /// <returns>True if opened</returns>
        private bool OpenSQLConnection(string dbpath, string source, string user, string pass)
        {
            try
            {
                if (fbDBConnection.State == ConnectionState.Closed)
                {
                    fbDBDataSet.Locale = CultureInfo.InvariantCulture;
                    fbDBConnection.ConnectionString = GetConnectionString(dbpath, source, user, pass);
                    fbDBConnection.Open();
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new FirebirdException("Cannot open database connection To " + dbpath + "!\r\n" + ex.Message);
            }
        }

        /// <summary>Executes a query that returns no results</summary>
        /// <param name="query">SQL query to execute</param>
        public void ExecuteNonQuery(string query)
        {
            if (IsOpen)
            {
                query = AdjustQuotedFields(query);
                FbCommand myCmd = new FbCommand(query, fbDBConnection);
                myCmd.CommandType = CommandType.Text;

                try
                {
                    myCmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    throw new FirebirdException("Cannot execute the SQL statement\r\n " + query + "\r\n" + ex.Message);
                }
                finally
                {
                    if (myCmd != null)
                    {
                        myCmd.Dispose();
                        myCmd = null;
                    }
                }
            }
            else
                throw new FirebirdException("Firebird database is not open.");
        }

        /// <summary>
        /// Column class
        /// </summary>
        private class Column
        {
            ////public string name;
            public Type dataType;
            public List<object> values = new List<object>();

            public void addIntValue(int value)
            {
                if (dataType == null)
                    dataType = typeof(int);
                values.Add(value);
            }

            public void addDoubleValue(double value)
            {
                if (dataType == null || dataType == typeof(int))
                    dataType = typeof(double);
                values.Add(value);
            }
            public void addByteArrayValue(byte[] value)
            {
                if (dataType == null || dataType == typeof(byte[]))
                    dataType = typeof(byte[]);
                values.Add(value);
            }
            public void addTextValue(string value)
            {
                DateTime date;
                if (DateTime.TryParseExact(value, "yyyy-MM-dd hh:mm:ss", null, System.Globalization.DateTimeStyles.None, out date))
                {
                    if (dataType == null)
                        dataType = typeof(DateTime);
                    values.Add(date);
                }
                else
                {
                    dataType = typeof(string);
                    values.Add(value);
                }
            }

            /// <summary>
            /// 
            /// </summary>
            public void addNull()
            {
                values.Add(null);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="rowIndex"></param>
            /// <returns></returns>
            internal object GetValue(int rowIndex)
            {
                if (rowIndex >= values.Count)
                    throw new Exception("Not enough values found when creating DataTable from Firebird query.");
                if (values[rowIndex] == null)
                    return DBNull.Value;
                else if (dataType == typeof(int))
                    return Convert.ToInt32(values[rowIndex]);
                else if (dataType == typeof(double))
                    return Convert.ToDouble(values[rowIndex], System.Globalization.CultureInfo.InvariantCulture);
                else if (dataType == typeof(DateTime))
                    return Convert.ToDateTime(values[rowIndex]);
                else if (dataType == typeof(byte[]))
                    return values[rowIndex];
                else
                {
                    if (values[rowIndex].GetType() == typeof(DateTime))
                        return Convert.ToDateTime(values[rowIndex]).ToString("yyyy-MM-dd hh:mm:ss");
                    return values[rowIndex].ToString();
                }
            }
        }

        /// <summary>
        /// Executes a query and stores the results in a DataTable
        /// </summary>
        /// <param name="query">SQL query to execute</param>
        /// <returns>DataTable of results</returns>
        public System.Data.DataTable ExecuteQuery(string query)
        {
            DataTable dt = null;

            if (IsOpen)
            {
                query = AdjustQuotedFields(query);
                dt = new DataTable();
                FbCommand myCmd = new FbCommand(query, fbDBConnection);
                myCmd.CommandType = CommandType.Text;

                try
                {
                    myCmd.ExecuteNonQuery();
                    FbDataAdapter da = new FbDataAdapter(myCmd);
                    da.Fill(dt);
                }
                catch (Exception ex)
                {
                    throw new FirebirdException("Cannot execute the SQL statement \r\n" + query + "\r\n" + ex.Message);
                }
                finally
                {
                    if (myCmd != null)
                    {
                        myCmd.Dispose();
                        myCmd = null;
                    }
                }
            }
            else
                throw new FirebirdException("Firebird database is not open.");

            return dt;
        }

        /// <summary>
        /// Executes a query and return a single integer value to caller. Returns -1 if not found.
        /// </summary>
        /// <param name="query">The query.</param>
        /// <param name="ColumnNumber">The column number.</param>
        /// <returns>The integer for the column (0-n) for the first row</returns>
        public int ExecuteQueryReturnInt(string query, int ColumnNumber)
        {
            if (!IsOpen)
                throw new FirebirdException("Firebird database is not open.");

            int ReturnValue = -1;
            DataTable data = ExecuteQuery(query);
            if (data != null)
            {
                DataRow dr = data.Rows[0];
                ReturnValue = Convert.ToInt32(dr[ColumnNumber]);
            }

            return ReturnValue;
        }

        /// <summary>Bind all parameters values to the specified query and execute the query.</summary>
        /// <param name="transaction">The Firebird transaction</param>
        /// <param name="query">The query.</param>
        /// <param name="values">The values.</param>
        public void BindParametersAndRunQuery(FbTransaction transaction, string query, object[] values)
        {
            using (FbCommand cmd = fbDBConnection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = query;
                cmd.CommandType = CommandType.Text;
                for (int i = 0; i < values.Length; i++)
                {
                    if (Convert.IsDBNull(values[i]) || values[i] == null)
                    {
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Text).Value = string.Empty;
                    }
                    // Enums have an underlying type of Int32, but we want to store
                    // their string representation, not their integer value
                    else if (values[i].GetType().IsEnum)
                    {
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Text).Value = values[i].ToString();
                    }
                    else if (values[i].GetType() == typeof(DateTime))
                    {
                        DateTime d = (DateTime)values[i];
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Text).Value = d.ToString("dd.MM.yyyy, hh:mm:ss.000");
                    }
                    else if (values[i].GetType() == typeof(int))
                    {
                        int integer = (int)values[i];
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Integer).Value = integer;
                    }
                    else if (values[i].GetType() == typeof(float))
                    {
                        float f = (float)values[i];
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Float).Value = f;
                    }
                    else if (values[i].GetType() == typeof(double))
                    {
                        double d = (double)values[i];
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Double).Value = d;
                    }
                    else if (values[i].GetType() == typeof(byte[]))
                    {
                        byte[] bytes = values[i] as byte[];
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Binary).Value = bytes;
                    }
                    else
                        cmd.Parameters.Add("@" + ((i + 1).ToString()), FbDbType.Text).Value = values[i] as string;
                }
                cmd.Prepare();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Return a list of column names.</summary>
        /// <param name="tableName">Name of the table.</param>
        /// <returns>A list of column names in column order (uppercase)</returns>
        public List<string> GetColumnNames(string tableName)
        {
            List<string> columnNames = new List<string>();

            if (IsOpen)
            {
                string sql = "select rdb$field_name from rdb$relation_fields ";
                sql += "where rdb$relation_name = '" + tableName.ToUpper() + "' ";
                sql += "order by rdb$field_position; ";

                DataTable dt = ExecuteQuery(sql);
                foreach (DataRow dr in dt.Rows)
                {
                    columnNames.Add((string)dr[0]);
                }
            }
            return columnNames;
        }

        /// <summary>Return a list of column names for the specified table</summary>
        /// <param name="tableName">The table name to get columns from.</param>
        public List<string> GetTableColumns(string tableName)
        {
            return GetColumnNames(tableName);
        }

        /// <summary>Return a list of table names</summary>
        /// <returns>A list of table names in sorted order (upper case)</returns>
        public List<string> GetTableNames()
        {
            List<string> tableNames = new List<string>();
            if (IsOpen)
            {
                string sql = "SELECT rdb$relation_name ";
                sql += "from rdb$relations ";
                sql += "where rdb$view_blr is null ";
                sql += "and(rdb$system_flag is null or rdb$system_flag = 0) ";
                sql += "order by rdb$relation_name;";

                DataTable dt = ExecuteQuery(sql);
                foreach (DataRow dr in dt.Rows)
                {
                    tableNames.Add((string)dr[0]);
                }
            }
            return tableNames;
        }

        /// <summary>Does the specified table exist?</summary>
        /// <param name="tableName">The table name to look for</param>
        public bool TableExists(string tableName)
        {
            List<string> tableNames = GetTableNames();
            return tableNames.Contains(tableName);
        }

        /// <summary>
        /// Drop (remove) columns from a table.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="colsToRemove"></param>
        public void DropColumns(string tableName, IEnumerable<string> colsToRemove)
        {
            List<string> updatedTableColumns = GetTableColumns(tableName);
            IEnumerable<string> columnsToRemove = colsToRemove.ToList();

            // Remove the columns we don't want anymore from the table's list of columns
            updatedTableColumns.RemoveAll(column => columnsToRemove.Contains(column));

            string columnsSeperated = null;
            foreach (string columnName in updatedTableColumns)
            {
                if (columnsSeperated != null)
                    columnsSeperated += ",";
                columnsSeperated += "\"" + columnName + "\"";
            }
            if (updatedTableColumns.Count > 0)
            {
                ExecuteNonQuery("BEGIN");

                // Rename old table
                ExecuteNonQuery("ALTER TABLE \"" + tableName + "\" RENAME TO \"" + tableName + "_old\"");

                // Creating the new table based on old table
                ExecuteNonQuery("CREATE TABLE \"" + tableName + "\" AS SELECT " + columnsSeperated + " FROM \"" + tableName + "_old\"");

                // Drop old table
                ExecuteNonQuery("DROP TABLE \"" + tableName + "_old\"");

                ExecuteNonQuery("END");
            }
        }

        /// <summary>
        /// Checks if the field exists
        /// </summary>
        /// <param name="table"></param>
        /// <param name="fieldname"></param>
        /// <returns>True if the field exists in the database</returns>
        public bool FieldExists(string table, string fieldname)
        {
            string sql = "SELECT COUNT(f.rdb$relation_name) ";
            sql += "from rdb$relation_fields f ";
            sql += "join rdb$relations r on f.rdb$relation_name = r.rdb$relation_name ";
            sql += "and f.rdb$relation_name = '" + table.ToUpper() + "' ";
            sql += "and f.rdb$field_name = '" + fieldname.ToUpper() + "' ";
            sql += "and r.rdb$view_blr is null ";
            sql += "and(r.rdb$system_flag is null or r.rdb$system_flag = 0);";

            DataTable dt = ExecuteQuery(sql);
            return (Convert.ToInt32(dt.Rows[0][0]) > 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="columnNames"></param>
        /// <returns></returns>
        public string CreateInsertSQL(string tableName, List<string> columnNames)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append("INSERT INTO ");
            sql.Append(tableName);
            sql.Append('(');

            for (int i = 0; i < columnNames.Count; i++)
            {
                if (i > 0)
                    sql.Append(',');
                sql.Append("\"");
                sql.Append(columnNames[i]);
                sql.Append("\"");
            }
            sql.Append(") VALUES (");

            for (int i = 0; i < columnNames.Count; i++)
            {
                if (i > 0)
                    sql.Append(',');
                sql.Append('@' + ((i + 1).ToString()));
            }

            sql.Append(')');

            return sql.ToString();
        }

        /// <summary>
        /// Insert a range of rows
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="columnNames"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public int InsertRows(string tableName, List<string> columnNames, List<object[]> values)
        {
            FbTransaction myTransaction = fbDBConnection.BeginTransaction();

            try
            {
                // Create an insert query
                string sql = CreateInsertSQL(tableName, columnNames); 
                for (int rowIndex = 0; rowIndex < values.Count; rowIndex++)
                    BindParametersAndRunQuery(myTransaction, sql, values[rowIndex]);
            }
            catch
            {
                throw new FirebirdException("Cannot insert rows");
            }

            lock (lockThis)
            {
                myTransaction.Commit();
            }
            return 0;
        }

        private object lockThis = new object();

        /// <summary>Convert .NET type into an Firebird type</summary>
        public string GetDBDataTypeName(object value)
        {
            // Convert the value we found above into an Firebird data type string and return it.
            Type type = null;
            if (value == null)
                return null;
            else
                type = value.GetType();

            if (type == null)
                return "INTEGER";
            else if (type.ToString() == "System.DateTime")
                return "TIMESTAMP";
            else if (type.ToString() == "System.Int32")
                return "INTEGER";
            else if (type.ToString() == "System.Single")
                return "FLOAT";
            else if (type.ToString() == "System.Double")
                return "DOUBLE PRECISION";
            else
                return "VARCHAR(50)";
        }

        /// <summary>Create the new table</summary>
        public void CreateTable(string tableName, List<string> colNames, List<string> colTypes)
        {
            StringBuilder sql = new StringBuilder();

            for (int c = 0; c < colNames.Count; c++)
            {
                if (sql.Length > 0)
                    sql.Append(',');

                sql.Append("\"");
                sql.Append(colNames[c]);
                sql.Append("\" ");
                if (colTypes[c] == null)
                    sql.Append("INTEGER");
                else
                    sql.Append(colTypes[c]);
            }

            sql.Insert(0, "CREATE TABLE " + tableName + " (");
            sql.Append(')');
            ExecuteNonQuery(sql.ToString());
        }

        /// <summary>
        /// Change any [] fields in the sql that may be remnants of other sql
        /// </summary>
        /// <param name="sql">The source SQL</param>
        /// <returns>Correctly quoted SQL for Firebird</returns>
        private string AdjustQuotedFields(string sql)
        {
            return sql.Replace("[", "\"").Replace("]", "\"");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public string AsSQLString(DateTime value)
        {
            // "dd.MM.yyyy, hh:mm:ss.000"
            return string.Format("'{0, 1}.{1, 1}.{2, 1:d4}, {3, 1}:{4, 1}:{5, 1}.000'", value.Day, value.Month, value.Year, value.Hour, value.Minute, value.Second);
        }

        /// <summary>A class representing an exception thrown by this library.</summary>
        [Serializable]
        public class FirebirdException : Exception
        {
            /// <summary>Initializes a new instance of the <see cref="FirebirdException"/> class.</summary>
            /// <param name="message">The message that describes the error.</param>
            public FirebirdException(string message) :
                base(message)
            {

            }
        }
    }
}
