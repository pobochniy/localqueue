// using System.Data;
// using System.Data.Common;
//
// namespace LocalQueue.PostgreSql;
//
// internal static class MySqlConnectionExtensions
// {
//     // ReSharper disable once UnusedMethodReturnValue.Global
//     public static async Task<int> ExecuteNonQueryAsync(this MySqlConnection connection,
//         string sql,
//         IEnumerable<MySqlParameter> parameters,
//         CancellationToken ct,
//         MySqlTransaction? transaction = null,
//         int commandTimeout = 5)
//     {
//         await using var command = BuildCommand(connection, sql, parameters, transaction, commandTimeout);
//         return await command.ExecuteNonQueryAsync(ct);
//     }
//
//     private static MySqlCommand BuildCommand(MySqlConnection connection,
//         string sql,
//         IEnumerable<MySqlParameter> parameters,
//         MySqlTransaction? transaction,
//         int commandTimeout)
//     {
//         var command = connection.CreateCommand();
//         command.CommandText = sql;
//         command.CommandTimeout = commandTimeout;
//         command.Transaction = transaction;
//
//         foreach (var parameter in parameters)
//         {
//             command.Parameters.Add(parameter);
//         }
//
//         return command;
//     }
//
//     public static byte[] GetBytes(this DbDataReader reader, string name)
//     {
//         if (reader.IsDBNull(name)) return Array.Empty<byte>();
//
//         using var stream = reader.GetStream(name);
//         using var memoryStream = new MemoryStream();
//
//         stream.CopyTo(memoryStream);
//         return memoryStream.ToArray();
//     }
// }