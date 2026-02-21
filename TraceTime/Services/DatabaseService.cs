using Microsoft.Data.Sqlite;
using System.IO;
using TraceTime.Models;

namespace TraceTime.Services
{
    internal class DatabaseService
    {
        private static string dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TraceTime",
            "ActivityLog.db"
        );
        public static void Initialize()
        {
            string? folder = Path.GetDirectoryName(dbPath);

            if (!string.IsNullOrEmpty(folder))
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
            }

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();

                command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Activity (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AppName TEXT,
                WindowTitle TEXT,
                Timestamp DATETIME,
                Duration INTEGER,
                IsPrimary INTEGER DEFAULT 1
            )";
                command.ExecuteNonQuery();

                try
                {
                    command.CommandText = "ALTER TABLE Activity ADD COLUMN IsPrimary INTEGER DEFAULT 1;";
                    command.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("Pomyślnie dodano kolumnę IsPrimary.");
                }
                catch (SqliteException ex)
                {
                    System.Diagnostics.Debug.WriteLine("Kolumna IsPrimary już istnieje lub wystąpił błąd: " + ex.Message);
                }
            }
        }

        public static void LogActivity(string appName, string windowTitle, bool isPrimary)
        {
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();

                int? targetId = null;
                using (var selectCmd = connection.CreateCommand())
                {
                    selectCmd.CommandText = @"
                SELECT Id FROM Activity 
                WHERE AppName = $app 
                  AND WindowTitle = $title 
                  AND IsPrimary = $isPrimary 
                ORDER BY Id DESC LIMIT 1";

                    selectCmd.Parameters.AddWithValue("$app", appName);
                    selectCmd.Parameters.AddWithValue("$title", windowTitle);
                    selectCmd.Parameters.AddWithValue("$isPrimary", isPrimary ? 1 : 0);

                    var result = selectCmd.ExecuteScalar();
                    if (result != null) targetId = Convert.ToInt32(result);
                }

                if (targetId.HasValue)
                {
                    using (var updateCommand = connection.CreateCommand())
                    {
                        updateCommand.CommandText = "UPDATE Activity SET Duration = Duration + 1 WHERE Id = $id";
                        updateCommand.Parameters.AddWithValue("$id", targetId.Value);
                        updateCommand.ExecuteNonQuery();
                    }
                }
            }
        }
        public static int InsertNewActivity(ActivityRecord record)
        {
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                INSERT INTO Activity (AppName, WindowTitle, Timestamp, Duration, IsPrimary) 
                VALUES ($app, $title, datetime('now', 'localtime'), 1, $isPrimary);
                SELECT last_insert_rowid();";

                    command.Parameters.AddWithValue("$app", record.AppName);
                    command.Parameters.AddWithValue("$title", record.WindowTitle);
                    command.Parameters.AddWithValue("$isPrimary", record.IsPrimary ? 1 : 0);

                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        public static List<ActivityRecord> GetStats(string range)
        {
            var stats = new List<ActivityRecord>();
            string dateCondition = range switch
            {
                "Dzisiaj" => "date(Timestamp, '-4 hours') = date('now', 'localtime', '-4 hours')",
                "Wczoraj" => "date(Timestamp, '-4 hours') = date('now', 'localtime', '-4 hours', '-1 day')",
                "Ostatnie 24h" => "Timestamp >= datetime('now', 'localtime', '-1 day')",
                "Tydzień" => "date(Timestamp, '-4 hours') >= date('now', 'localtime', '-4 hours', '-7 days')",
                "Ostatnie 30 dni" => "date(Timestamp, '-4 hours') >= date('now', 'localtime', '-4 hours', '-30 days')",
                "Cały czas" => "1=1",
                _ => "date(Timestamp, '-4 hours') = date('now', 'localtime', '-4 hours')"
            };

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"
                SELECT AppName, SUM(Duration) as Total, IsPrimary
                FROM Activity 
                WHERE {dateCondition}
                GROUP BY AppName, IsPrimary 
                ORDER BY Total DESC";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var record = new ActivityRecord
                            {
                                AppName = reader.GetString(0),
                                Duration = reader.GetInt32(1),
                                IsPrimary = reader.GetInt32(2) == 1,
                                WindowTitle = "",
                                Details = GetAppDetails(reader.GetString(0), dateCondition)
                            };
                            stats.Add(record);
                        }
                    }
                }
            }
            return stats;
        }

        private static List<ActivityRecord> GetAppDetails(string appName, string dateCondition)
        {
            var details = new List<ActivityRecord>();

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"
                    SELECT WindowTitle, SUM(Duration) as Total 
                    FROM Activity 
                    WHERE AppName = @app AND {dateCondition}
                    GROUP BY WindowTitle 
                    ORDER BY Total DESC";

                    command.Parameters.AddWithValue("@app", appName);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int duration = reader.GetInt32(1);
                            TimeSpan t = TimeSpan.FromSeconds(duration);

                            details.Add(new ActivityRecord
                            {
                                AppName = appName,
                                WindowTitle = reader.GetString(0),
                                Duration = duration,
                                Tag = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds)
                            });
                        }
                    }
                }
            }
            return details;
        }
        public static Dictionary<string, int> GetDailyTotals()
        {
            var totals = new Dictionary<string, int>();
            try
            {
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT date(Timestamp, '-4 hours') as Day, SUM(Duration) FROM Activity GROUP BY Day";
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (!reader.IsDBNull(0))
                                    totals[reader.GetString(0)] = reader.GetInt32(1);
                            }
                        }
                    }
                }
            }
            catch { }
            return totals;
        }

        public static int GetTotalTimeToday(bool includeBackground)
        {
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();

                if (includeBackground)
                {
                    command.CommandText = "SELECT SUM(Duration) FROM Activity WHERE date(Timestamp) = date('now', 'localtime')";
                }
                else
                {
                    command.CommandText = "SELECT SUM(Duration) FROM Activity WHERE date(Timestamp) = date('now', 'localtime') AND IsPrimary = 1";
                }

                var result = command.ExecuteScalar();
                return result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
        }
    }
}