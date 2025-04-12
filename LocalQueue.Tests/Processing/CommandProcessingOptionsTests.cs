using LocalQueue.Processing;

namespace LocalQueue.Tests.Processing;

[TestFixture]
public class CommandProcessingOptionsTests
{
    [Test]
    public void PrefetchCountShouldHaveDefaultValueEquals10()
    {
        var sut = new CommandProcessingOptions();

        Assert.That(sut.PrefetchCount, Is.EqualTo(10));
    }

    [Test]
    public void MaxParallelismShouldHaveDefaultValueEquals10()
    {
        var sut = new CommandProcessingOptions();

        Assert.That(sut.WorkersCount, Is.EqualTo(10));
    }

    [Test]
    public void InvisibilityTimeoutShouldHaveDefaultValueEquals10Seconds()
    {
        var sut = new CommandProcessingOptions();

        Assert.That(sut.InvisibilityTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void IdleTimeoutShouldHaveDefaultValueEquals3Seconds()
    {
        var sut = new CommandProcessingOptions();

        Assert.That(sut.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }
}