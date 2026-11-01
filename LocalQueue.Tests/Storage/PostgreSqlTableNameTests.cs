using LocalQueue.PostgreSql;

namespace LocalQueue.Tests.Storage;

public class PostgreSqlTableNameTests
{
    [TestCase("cmds", new[] { "cmds" })]
    [TestCase("Cmds", new[] { "cmds" })]
    [TestCase("schema.cmds", new[] { "schema", "cmds" })]
    [TestCase(" schema . cmds ", new[] { "schema", "cmds" })]
    [TestCase("\"schema\".\"cmds\"", new[] { "schema", "cmds" })]
    [TestCase("\"Schema\".\"Cmds\"", new[] { "Schema", "Cmds" })]
    [TestCase("\"schema.cmds\"", new[] { "schema.cmds" })]
    [TestCase("\"schema\".\" cmds \"", new[] { "schema", " cmds " })]
    [TestCase("\"cm\"\"ds\"", new[] { "cm\"ds" })]
    [TestCase("db.schema.cmds", new[] { "db", "schema", "cmds" })]
    public void SplitsTableName(string tableName, string[] expected)
    {
        Assert.That(PostgreSqlTableName.Split(tableName), Is.EqualTo(expected));
    }
}
