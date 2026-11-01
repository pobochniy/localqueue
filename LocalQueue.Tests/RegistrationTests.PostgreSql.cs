using System.Reflection;
using LocalQueue.PostgreSql;
using LocalQueue.Queue;
using LocalQueue.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalQueue.Tests;

public partial class RegistrationTests
{
    [Test]
    public void CannotRegisterPostgreSqlWorkerWithMalformedConnectionString()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<LocalCommandHandler>();

        Assert.Throws<ArgumentException>(() => serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UsePostgreSqlStorage(o => o.ConnectionString = "malformed");
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
        }));
    }

    [Test]
    public void CanConfigurePostgreSqlStorageFromIConfiguration()
    {
        var options = new PostgreSqlCommandQueueStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddPostgreSqlCommandStorageOptions(options, prefix: "PostgreSqlLocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
            c.UsePostgreSqlStorage(configuration.GetSection("PostgreSqlLocalCommandsQueue")));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigurePostgreSqlStorageFromAction()
    {
        var options = new PostgreSqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UsePostgreSqlStorage(o =>
            {
                o.TableName = options.TableName;
                o.InsertBatchSize = options.InsertBatchSize;
                o.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigurePostgreSqlStorageFromOptionInstance()
    {
        var options = new PostgreSqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c => c.UsePostgreSqlStorage(options));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigurePostgreSqlCommandQueueFromIConfiguration()
    {
        var options = new PostgreSqlCommandQueueStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddPostgreSqlCommandStorageOptions(options, prefix: "PostgreSqlLocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UsePostgreSqlStorage(configuration.GetSection("PostgreSqlLocalCommandsQueue"));
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigurePostgreSqlCommandQueueFromAction()
    {
        var options = new PostgreSqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UsePostgreSqlStorage(o =>
            {
                o.TableName = options.TableName;
                o.InsertBatchSize = options.InsertBatchSize;
                o.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            });
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigurePostgreSqlCommandQueueFromInstance()
    {
        var options = new PostgreSqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UsePostgreSqlStorage(options);
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigurePostgreSqlCommandQueueWorkerFromIConfiguration()
    {
        var workerStorageOptions = new PostgreSqlLocalCommandsQueueWorkerStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddPostgreSqlCommandWorkerStorageOptions(workerStorageOptions, prefix: "PostgreSqlLocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UsePostgreSqlStorage(configuration.GetSection("PostgreSqlLocalCommandsQueue"));
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(workerStorageOptions);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }

    [Test]
    public void CanConfigurePostgreSqlCommandQueueWorkerFromAction()
    {
        var options = new PostgreSqlLocalCommandsQueueWorkerStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UsePostgreSqlStorage(o =>
            {
                o.ConnectionString = options.ConnectionString;
                o.TableName = options.TableName;
                o.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            });
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }

    [Test]
    public void CanConfigurePostgreSqlCommandQueueWorkerFromInstance()
    {
        var options = new PostgreSqlLocalCommandsQueueWorkerStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<LocalCommandHandler>();

        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UsePostgreSqlStorage(options);
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertPostgreSqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }

    private class PostgreSqlCommandQueueStorageOptionsFake : PostgreSqlCommandQueueStorageOptions
    {
        public PostgreSqlCommandQueueStorageOptionsFake()
        {
            TableName = $"table_{TestContext.CurrentContext.Test.Name}";
            InsertBatchSize = Random.Shared.Not(100);
            CommandTimeoutSeconds = Random.Shared.Not(5, InsertBatchSize);
        }
    }

    private class PostgreSqlLocalCommandsQueueWorkerStorageOptionsFake : PostgreSqlLocalCommandsQueueWorkerStorageOptions
    {
        public PostgreSqlLocalCommandsQueueWorkerStorageOptionsFake()
        {
            ConnectionString = $"Host=server;Database={TestContext.CurrentContext.Test.Name}";
            TableName = $"table_{TestContext.CurrentContext.Test.Name}";
            CommandTimeoutSeconds = Random.Shared.Not(5);
        }
    }
}

public static class PostgreSqlServiceProviderAssertionExtension
{
    public static void AssertPostgreSqlCommandStorageOptionsAreSame(this IServiceProvider serviceProvider,
        PostgreSqlLocalCommandsQueueWorkerStorageOptions options)
    {
        var rawCommandHandler = serviceProvider.GetCommandHandler();
        var storageFactory = (Func<IServiceProvider, ILocalCommandsQueueWorkerStorage>)rawCommandHandler
            .GetType()
            .GetField("_storageFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(rawCommandHandler)!;

        var storage = (PostgreSqlLocalCommandsQueueWorkerStorage)storageFactory(serviceProvider);

        var optionsField = storage
            .GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(PostgreSqlLocalCommandsQueueWorkerStorageOptions));

        var registeredOptions = (PostgreSqlLocalCommandsQueueWorkerStorageOptions)optionsField.GetValue(storage)!;

        Assert.Multiple(() =>
        {
            Assert.That(registeredOptions.ConnectionString, Is.EqualTo(options.ConnectionString));
            Assert.That(registeredOptions.TableName, Is.EqualTo(options.TableName));
            Assert.That(registeredOptions.CommandTimeoutSeconds, Is.EqualTo(options.CommandTimeoutSeconds));
        });
    }

    public static void AssertPostgreSqlCommandStorageOptionsAreSame(this IServiceProvider serviceProvider,
        PostgreSqlCommandQueueStorageOptions options)
    {
        var commandsQueue = serviceProvider.GetRequiredService<ICommandsQueue>();
        var storage = commandsQueue
            .GetType()
            .GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(commandsQueue)!;
        var optionsField = storage
            .GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(PostgreSqlCommandQueueStorageOptions));

        var registeredOptions = (PostgreSqlCommandQueueStorageOptions)optionsField.GetValue(storage)!;

        Assert.Multiple(() =>
        {
            Assert.That(registeredOptions.TableName, Is.EqualTo(options.TableName));
            Assert.That(registeredOptions.CommandTimeoutSeconds, Is.EqualTo(options.CommandTimeoutSeconds));
            Assert.That(registeredOptions.InsertBatchSize, Is.EqualTo(options.InsertBatchSize));
        });
    }
}

public static class PostgreSqlConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddPostgreSqlCommandStorageOptions(this IConfigurationBuilder builder,
        PostgreSqlCommandQueueStorageOptions options,
        string? prefix = null)
    {
        var myConfiguration = new Dictionary<string, string?>
        {
            { "TableName".WithPrefix(prefix), options.TableName },
            { "InsertBatchSize".WithPrefix(prefix), options.InsertBatchSize.ToString() },
            { "CommandTimeoutSeconds".WithPrefix(prefix), options.CommandTimeoutSeconds.ToString() },
        };

        return builder.AddInMemoryCollection(myConfiguration);
    }

    public static IConfigurationBuilder AddPostgreSqlCommandWorkerStorageOptions(this IConfigurationBuilder builder,
        PostgreSqlLocalCommandsQueueWorkerStorageOptions options,
        string? prefix = null)
    {
        var myConfiguration = new Dictionary<string, string?>
        {
            { "TableName".WithPrefix(prefix), options.TableName },
            { "ConnectionString".WithPrefix(prefix), options.ConnectionString },
            { "CommandTimeoutSeconds".WithPrefix(prefix), options.CommandTimeoutSeconds.ToString() },
        };

        return builder.AddInMemoryCollection(myConfiguration);
    }
}
