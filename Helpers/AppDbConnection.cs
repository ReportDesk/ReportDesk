using System.Data;
using Npgsql;

namespace ReportSystem.Helpers
{
    public class AppDbConnection
    {
        private readonly IConfigurationService _config;

        public AppDbConnection(IConfigurationService config)
        {
            _config = config;
        }

        private string ConnectionDb => _config.GetConnectionString("SupabaseDb");

        public DataTable GetData(string query)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlDataAdapter adapter = new(query, conn);
            DataTable dataTable = new();
            adapter.Fill(dataTable);
            return dataTable;
        }

        public DataTable GetData(string query, List<string> stringParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand cmd = new(query, conn);

            int counter = 1;
            foreach (var parameter in stringParams)
                cmd.Parameters.AddWithValue("@string" + (counter++), parameter);

            using NpgsqlDataAdapter adapter = new(cmd);
            DataTable dataTable = new();
            adapter.Fill(dataTable);
            return dataTable;
        }

        public DataTable GetData(string query, List<long> intParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlDataAdapter adapter = new(query, conn);

            int counter = 1;
            foreach (var parameter in intParams)
            {
                adapter.SelectCommand.Parameters.AddWithValue("@integer" + (counter++), parameter);
            }

            DataTable dataTable = new DataTable();
            adapter.Fill(dataTable);
            conn.Close();
            return dataTable;
        }


        public DataTable GetData(string query, List<string> stringParams, List<DateTime> dateParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlDataAdapter adapter = new(query, conn);

            int counter = 1;
            foreach (var parameter in stringParams)
            {
                adapter.SelectCommand.Parameters.AddWithValue("@string" + (counter++), parameter);
            }

            counter = 1;
            foreach (var parameter in dateParams)
            {
                adapter.SelectCommand.Parameters.AddWithValue("@date" + (counter++), parameter);
            }

            DataTable dataTable = new DataTable();
            adapter.Fill(dataTable);
            conn.Close();
            return dataTable;
        }

        public DataTable InsertOrUpdateData(string query, List<string> stringParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand sqlCommand = new(query, conn);
            sqlCommand.Prepare();

            int counter = 1;
            foreach (var parameter in stringParams)
            {
                sqlCommand.Parameters.AddWithValue("@string" + (counter++), parameter);
            }

            int result = sqlCommand.ExecuteNonQuery();
            if (result < 0)
            {
                Console.WriteLine("Error! Insert failed.");
                System.Diagnostics.Debug.WriteLine("Error! Insert failed.");
            }

            conn.Close();
            return null!;
        }

        public bool InsertOrUpdateDataBoolean(string query, List<string> stringParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand cmd = new(query, conn);
            int counter = 1;
            foreach (var p in stringParams)
                cmd.Parameters.AddWithValue("@string" + counter++, p);
            try
            {
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }

        public bool InsertOrUpdateDataBoolean(string query, List<long> intParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand cmd = new(query, conn);
            int counter = 1;
            foreach (var p in intParams)
                cmd.Parameters.AddWithValue("@string" + counter++, p);
            try
            {
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }
        public bool InsertOrUpdateDataBoolean(string query, List<string> stringParams, List<long> intParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand cmd = new(query, conn);
            int counter = 1;
            foreach (var p in intParams)
                cmd.Parameters.AddWithValue("@string" + counter++, p);

            counter = 1;
            foreach (var p in stringParams)
                cmd.Parameters.AddWithValue("@string" + counter++, p);

            try
            {
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }

        public DataTable InsertOrUpdateData(string query, List<string> stringParams, List<long> intParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand sqlCommand = new(query, conn);
            sqlCommand.Prepare();

            int counter = 1;
            foreach (var parameter in stringParams)
            {
                sqlCommand.Parameters.AddWithValue("@string" + (counter++), parameter);
            }

            counter = 1;
            foreach (var parameter in intParams)
            {
                sqlCommand.Parameters.AddWithValue("@integer" + (counter++), parameter);
            }

            int result = sqlCommand.ExecuteNonQuery();
            if (result < 0)
            {
                Console.WriteLine("Error! Insert failed.");
                System.Diagnostics.Debug.WriteLine("Error! Insert failed.");
            }

            conn.Close();
            return null!;
        }

        public DataTable InsertOrUpdateData(string query, List<string> stringParams, DateTime[] dateParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand sqlCommand = new(query, conn);
            sqlCommand.Prepare();

            int counter = 1;
            foreach (var parameter in stringParams)
            {
                sqlCommand.Parameters.AddWithValue("@string" + (counter++), parameter);
            }

            counter = 1;
            foreach (var parameter in dateParams)
            {
                sqlCommand.Parameters.AddWithValue("@date" + (counter++), parameter);
            }

            int result = sqlCommand.ExecuteNonQuery();
            if (result < 0)
            {
                Console.WriteLine("Error! Insert failed.");
                System.Diagnostics.Debug.WriteLine("Error! Insert failed.");
            }

            conn.Close();
            return null!;
        }


        public DataTable DeleteData(string query, string[] myParams)
        {
            using NpgsqlConnection conn = new(ConnectionDb);
            conn.Open();
            using NpgsqlCommand sqlCommand = new(query, conn);
            sqlCommand.Prepare();

            for (int i = 0; i < myParams.Length; i++)
            {
                sqlCommand.Parameters.AddWithValue("@string" + (i + 1), myParams[i]);
            }

            int result = sqlCommand.ExecuteNonQuery();
            if (result < 0)
            {
                Console.WriteLine("Error! Delete failed.");
                System.Diagnostics.Debug.WriteLine("Error! Delete failed.");
            }

            conn.Close();
            return null!;
        }

    }
}
