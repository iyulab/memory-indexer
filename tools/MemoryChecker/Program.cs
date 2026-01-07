using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : "twenty_questions.db";

if (!File.Exists(dbPath))
{
    Console.WriteLine($"Error: Database not found at {dbPath}");
    return 1;
}

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Get total count
using var totalCmd = connection.CreateCommand();
totalCmd.CommandText = "SELECT COUNT(*) FROM memories";
var total = Convert.ToInt32(totalCmd.ExecuteScalar());

// Get count by type
using var typeCmd = connection.CreateCommand();
typeCmd.CommandText = "SELECT Type, COUNT(*) as Count FROM memories GROUP BY Type ORDER BY Type";
using var reader = typeCmd.ExecuteReader();

Console.WriteLine($"\n=== Memory Storage Analysis ===");
Console.WriteLine($"Total Memories: {total}");
Console.WriteLine($"\nBy Type:");

while (reader.Read())
{
    var type = reader.GetInt32(0);
    var count = reader.GetInt32(1);
    var typeName = type switch
    {
        0 => "Episodic",
        1 => "Semantic",
        2 => "Procedural",
        _ => $"Unknown({type})"
    };
    var percentage = 100.0 * count / total;
    Console.WriteLine($"  {typeName,-12} {count,3} ({percentage:F1}%)");
}

Console.WriteLine($"\nExpected: 84 conversation memories");
Console.WriteLine($"Retention: {total}/84 ({100.0 * total / 84:F1}%)");

return 0;
