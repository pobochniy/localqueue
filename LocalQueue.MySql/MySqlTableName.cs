using System.Text;

namespace LocalQueue.MySql;

internal static class MySqlTableName
{
    /// <summary>
    /// Split a table name into the schema and the table: `db`.`table`, db.table or table
    /// </summary>
    public static string[] Split(string tableName)
    {
        var parts = new List<string>();
        var part = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < tableName.Length)
        {
            var c = tableName[i++];
            if (c == '`' && quoted && i < tableName.Length && tableName[i] == '`')
            {
                // `` is an escaped backtick in a quoted name
                part.Append(c);
                i++;
            }
            else if (c == '`')
            {
                quoted = !quoted;
            }
            else if (c == '.' && !quoted)
            {
                parts.Add(part.ToString());
                part.Clear();
            }
            else if (!quoted && char.IsWhiteSpace(c))
            {
                // spaces are allowed around the dot, but only quoted names can contain them
            }
            else
            {
                part.Append(c);
            }
        }

        parts.Add(part.ToString());
        return parts.ToArray();
    }
}
