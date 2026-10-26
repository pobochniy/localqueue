using LocalQueue.MySql;

namespace LocalQueue.Tests.Storage;

public class MySqlTableNameTests
{
    [TestCase("cmds", new[] { "cmds" })]
    [TestCase("db.cmds", new[] { "db", "cmds" })]
    [TestCase(" db . cmds ", new[] { "db", "cmds" })]
    [TestCase("`db`.`cmds`", new[] { "db", "cmds" })]
    [TestCase("`db.cmds`", new[] { "db.cmds" })]
    [TestCase("`db`.` cmds `", new[] { "db", " cmds " })]
    [TestCase("`cm``ds`", new[] { "cm`ds" })]
    public void SplitsTableName(string tableName, string[] expected)
    {
        Assert.That(MySqlTableName.Split(tableName), Is.EqualTo(expected));
    }
}
