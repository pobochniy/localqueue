using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalQueue.Diagnostics;
using LocalQueue.MySql;
using LocalQueue.Processing;
using LocalQueue.Queue;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace LocalQueue.Tests;

public class RegistrationTests
{
    [Test]
    public void CanRegisterCommandQueue()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueStorage>()));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertLocalCommandQueueRegistered();
    }

    [Test]
    public void CanAddConverterToSerializerOptions_WhenRegisterCommandQueue()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UseStorage(Mock.Of<ILocalCommandsQueueStorage>());
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanRegisterCommandHandler()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.TryAddSingleton(Mock.Of<ILocalCommandsQueueWorkerStorage>());
        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<LocalCommand, LocalCommandHandler>()
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandRegistered<LocalCommand>();
    }

    [Test]
    public void CannotRegisterWorkerWithoutCommandHandlers()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        Assert.Throws<InvalidOperationException>(() => serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())));
    }

    [Test]
    public void CannotRegisterMySqlWorkerWithMalformedConnectionString()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<LocalCommandHandler>();

        Assert.Throws<ArgumentException>(() => serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseMySqlStorage(o => o.ConnectionString = "malformed");
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
        }));
    }

    [Test]
    public async Task EachWorkerFetchesOnlyCommandsItHasHandlersFor()
    {
        var localCommandStorage = new PrefetchRecordingStorage();
        var otherLocalCommandStorage = new PrefetchRecordingStorage();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection
            .AddLocalCommandQueueWorker(c => c
                .UseStorage(localCommandStorage.Storage)
                .AddCommandHandler<LocalCommand, LocalCommandHandler>())
            .AddLocalCommandQueueWorker(c => c
                .UseStorage(otherLocalCommandStorage.Storage)
                .AddCommandHandler<OtherLocalCommand, OtherLocalCommandHandler>());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        var fetching = serviceProvider.GetServices<CommandFetcher>()
            .Select(fetcher => fetcher.Run(cts.Token))
            .ToArray();

        var localCommandTypes = await localCommandStorage.FetchedCommandTypes.WaitAsync(TimeSpan.FromSeconds(5));
        var otherLocalCommandTypes = await otherLocalCommandStorage.FetchedCommandTypes.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(() => Task.WhenAll(fetching));

        Assert.That(localCommandTypes, Is.EquivalentTo(new[] { typeof(LocalCommand).FullName }));
        Assert.That(otherLocalCommandTypes, Is.EquivalentTo(new[] { typeof(OtherLocalCommand).FullName }));
    }

    private sealed class PrefetchRecordingStorage
    {
        private readonly TaskCompletionSource<IReadOnlyCollection<string>> _fetchedCommandTypes =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PrefetchRecordingStorage()
        {
            var storage = new Mock<ILocalCommandsQueueWorkerStorage>();
            storage
                .Setup(s => s.Prefetch(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .Callback<int, TimeSpan, IReadOnlyCollection<string>, CancellationToken>((_, _, commandTypes, _) =>
                    _fetchedCommandTypes.TrySetResult(commandTypes))
                .ReturnsAsync(Array.Empty<CommandRecord>());
            Storage = storage.Object;
        }

        public ILocalCommandsQueueWorkerStorage Storage { get; }

        public Task<IReadOnlyCollection<string>> FetchedCommandTypes => _fetchedCommandTypes.Task;
    }

    [Test]
    public void CanRegisterHandlingAction()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<LocalCommand, Mediator>(h => h.Send)
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandRegistered<LocalCommand>();
    }

    [Test]
    public void CanRegisterHandlingFunction()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<LocalCommand>((sp, command, ct) =>
                sp.GetRequiredService<Mediator>().Send(command, ct))
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandRegistered<LocalCommand>();
    }

    [Test]
    public void CanAddConverterToSerializerOptions()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>());
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureWorkerFromIConfiguration()
    {
        var options = new CommandProcessingOptions
        {
            PrefetchCount = 15,
            WorkersCount = 20,
            InvisibilityTimeout = TimeSpan.FromSeconds(25),
            IdleTimeout = TimeSpan.FromSeconds(5),
        };

        var configuration = new ConfigurationBuilder()
            .AddCommandProcessingOptions(options, prefix: "LocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.TryAddSingleton(Mock.Of<ILocalCommandsQueueStorage>());
        serviceCollection.AddLocalCommandQueueWorker(configuration.GetSection("LocalCommandsQueue"),
            c => c
                .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
                .AddCommandHandler<LocalCommand, LocalCommandHandler>()
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandProcessingOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureWorkerFromAction()
    {
        var options = new CommandProcessingOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.TryAddSingleton(Mock.Of<ILocalCommandsQueueStorage>());
        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>());
            c.PrefetchCount = options.PrefetchCount;
            c.WorkersCount = options.WorkersCount;
            c.InvisibilityTimeout = options.InvisibilityTimeout;
            c.IdleTimeout = options.IdleTimeout;

            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandProcessingOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureWorkerFromInstance()
    {
        var options = new LocalCommandQueueWorkerConfiguratorFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.TryAddSingleton(Mock.Of<ILocalCommandsQueueStorage>());
        serviceCollection.AddLocalCommandQueueWorker(options, c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<LocalCommand, LocalCommandHandler>());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandProcessingOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMysqlStorageFromIConfiguration()
    {
        var options = new MySqlCommandQueueStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddMySqlCommandStorageOptions(options, prefix: "MySqlLocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
            c.UseMySqlStorage(configuration.GetSection("MySqlLocalCommandsQueue")));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMysqlStorageFromAction()
    {
        var options = new MySqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UseMySqlStorage(o =>
            {
                o.TableName = options.TableName;
                o.InsertBatchSize = options.InsertBatchSize;
                o.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMysqlStorageFromOptionInstance()
    {
        var options = new MySqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c => c.UseMySqlStorage(options));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMySqlCommandQueueFromIConfiguration()
    {
        var options = new MySqlCommandQueueStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddMySqlCommandStorageOptions(options, prefix: "MySqlLocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UseMySqlStorage(configuration.GetSection("MySqlLocalCommandsQueue"));
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueFromAction()
    {
        var options = new MySqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UseMySqlStorage(o =>
            {
                o.TableName = options.TableName;
                o.InsertBatchSize = options.InsertBatchSize;
                o.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            });
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueFromInstance()
    {
        var options = new MySqlCommandQueueStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
            c.UseMySqlStorage(options);
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerFromIConfiguration()
    {
        var workerStorageOptions = new MySqlLocalCommandsQueueWorkerStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddMySqlCommandWorkerStorageOptions(workerStorageOptions, prefix: "MySqlLocalCommandsQueue")
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseMySqlStorage(configuration.GetSection("MySqlLocalCommandsQueue"));
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(workerStorageOptions);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerFromAction()
    {
        var options = new MySqlLocalCommandsQueueWorkerStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseMySqlStorage(o =>
            {
                o.ConnectionString = options.ConnectionString;
                o.TableName = options.TableName;
                o.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            });
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerFromInstance()
    {
        var options = new MySqlLocalCommandsQueueWorkerStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<LocalCommandHandler>();

        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseMySqlStorage(options);
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertMySqlCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverterForWorker<LocalStringConverter>();
    }
    
    
    [Test]
    public void StorageSummaryCollectorShouldHaveCommandTypesForMetrics()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<LocalCommandHandler>();

        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>(x => x.DataSourceName == "datasource"));
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertServiceHasCommandType<LocalCommand>();
    }

    private record LocalCommand(Guid Id);

    private class LocalCommandHandler : ICommandHandler<LocalCommand>
    {
        public Task Handle(LocalCommand command, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private class LocalStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return string.Empty;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    private class Mediator
    {
#pragma warning disable S1172
#pragma warning disable S2325
        public Task Send<T>(T command, CancellationToken ct)
#pragma warning restore S2325
#pragma warning restore S1172
        {
            return Task.CompletedTask;
        }
    }

    private class CommandProcessingOptionsFake : CommandProcessingOptions
    {
        public CommandProcessingOptionsFake()
        {
            PrefetchCount = Random.Shared.Not(10);
            WorkersCount = Random.Shared.Not(10, PrefetchCount);
            InvisibilityTimeout = TimeSpan.FromSeconds(Random.Shared.Not(10, PrefetchCount, WorkersCount));
            IdleTimeout =
                TimeSpan.FromSeconds(Random.Shared.Not(3, PrefetchCount, WorkersCount, InvisibilityTimeout.Seconds));
        }
    }

    private class LocalCommandQueueWorkerConfiguratorFake : LocalCommandQueueWorkerConfigurator
    {
        public LocalCommandQueueWorkerConfiguratorFake()
        {
            PrefetchCount = Random.Shared.Not(10);
            WorkersCount = Random.Shared.Not(10, PrefetchCount);
            InvisibilityTimeout = TimeSpan.FromSeconds(Random.Shared.Not(10, PrefetchCount, WorkersCount));
            IdleTimeout =
                TimeSpan.FromSeconds(Random.Shared.Not(3, PrefetchCount, WorkersCount, InvisibilityTimeout.Seconds));
        }
    }

    private class MySqlCommandQueueStorageOptionsFake : MySqlCommandQueueStorageOptions
    {
        public MySqlCommandQueueStorageOptionsFake()
        {
            TableName = $"table_{TestContext.CurrentContext.Test.Name}";
            InsertBatchSize = Random.Shared.Not(100);
            CommandTimeoutSeconds = Random.Shared.Not(5, InsertBatchSize);
        }
    }

    private class MySqlLocalCommandsQueueWorkerStorageOptionsFake : MySqlLocalCommandsQueueWorkerStorageOptions
    {
        public MySqlLocalCommandsQueueWorkerStorageOptionsFake()
        {
            ConnectionString = $"Server=server;Database={TestContext.CurrentContext.Test.Name}";
            TableName = $"table_{TestContext.CurrentContext.Test.Name}";
            CommandTimeoutSeconds = Random.Shared.Not(5);
        }
    }
}

public static class ServiceProviderAssertionExtension
{
    public static void AssertCommandRegistered<TCommand>(this ServiceProvider serviceProvider)
    {
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToArray();
        var commandProcessorHostedService =
            hostedServices.OfType<CommandProcessorHostedService>().SingleOrDefault();
        var fetchCommandHostedService =
            hostedServices.OfType<FetchCommandHostedService>().SingleOrDefault();

        Assert.That(commandProcessorHostedService, Is.Not.Null);
        Assert.That(fetchCommandHostedService, Is.Not.Null);

        var handler = GetCommandHandler<TCommand>(serviceProvider);
        Assert.That(handler, Is.Not.Null);
    }

    private static IEnumerable<CommandProcessor> GetCommandProcessors(this IServiceProvider serviceProvider)
    {
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToArray();
        var commandProcessorHostedService =
            hostedServices.OfType<CommandProcessorHostedService>().Single();

        return (IEnumerable<CommandProcessor>)commandProcessorHostedService
            .GetType().GetField("_commandProcessors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(commandProcessorHostedService)!;
    }

    private static IRawCommandHandler? GetCommandHandler<TCommand>(this IServiceProvider serviceProvider)
    {
        var commandProcessors = GetCommandProcessors(serviceProvider);
        return commandProcessors.SelectMany(x =>
                (IEnumerable<IRawCommandHandler>)x.GetType()
                    .GetField("_commandHandlers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(x)!)
            .SingleOrDefault(x => x.CommandType == typeof(TCommand).FullName);
    }

    private static IRawCommandHandler GetCommandHandler(this IServiceProvider serviceProvider)
    {
        var commandProcessors = GetCommandProcessors(serviceProvider);
        var rawCommandHandlers = commandProcessors
            .SelectMany(x => (IEnumerable<IRawCommandHandler>)x
                .GetType()
                .GetField("_commandHandlers", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(x)!);
        return rawCommandHandlers.First();
    }

    public static void AssertCommandProcessingOptionsAreSame(this IServiceProvider serviceProvider,
        CommandProcessingOptions options)
    {
        var commandProcessors = GetCommandProcessors(serviceProvider);
        var commandProcessor = commandProcessors.First();

        var registeredOptions = (CommandProcessingOptions)commandProcessor
            .GetType()
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(commandProcessor)!;

        Assert.Multiple(() =>
        {
            Assert.That(registeredOptions.PrefetchCount, Is.EqualTo(options.PrefetchCount));
            Assert.That(registeredOptions.WorkersCount, Is.EqualTo(options.WorkersCount));
            Assert.That(registeredOptions.InvisibilityTimeout, Is.EqualTo(options.InvisibilityTimeout));
            Assert.That(registeredOptions.IdleTimeout, Is.EqualTo(options.IdleTimeout));
        });
    }

    public static void AssertLocalCommandQueueRegistered(this IServiceProvider serviceProvider)
    {
        var commandsQueue = serviceProvider.GetService<ICommandsQueue>();

        Assert.Multiple(() =>
        {
            Assert.That(commandsQueue, Is.Not.Null);
            Assert.That(commandsQueue, Is.TypeOf<CommandsQueue>());
        });
    }

    public static void AssertMySqlCommandStorageOptionsAreSame(this IServiceProvider serviceProvider,
        MySqlLocalCommandsQueueWorkerStorageOptions options)
    {
        var rawCommandHandler = GetCommandHandler(serviceProvider);
        var storageFactory = (Func<IServiceProvider, ILocalCommandsQueueWorkerStorage>)rawCommandHandler
            .GetType()
            .GetField("_storageFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(rawCommandHandler)!;

        var storage = (MySqlLocalCommandsQueueWorkerStorage)storageFactory(serviceProvider);

        var optionsField = storage
            .GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(MySqlLocalCommandsQueueWorkerStorageOptions));

        var registeredOptions = (MySqlLocalCommandsQueueWorkerStorageOptions)optionsField.GetValue(storage)!;

        Assert.Multiple(() =>
        {
            Assert.That(registeredOptions.ConnectionString, Is.EqualTo(options.ConnectionString));
            Assert.That(registeredOptions.TableName, Is.EqualTo(options.TableName));
            Assert.That(registeredOptions.CommandTimeoutSeconds, Is.EqualTo(options.CommandTimeoutSeconds));
        });
    }

    public static void AssertMySqlCommandStorageOptionsAreSame(this IServiceProvider serviceProvider,
        MySqlCommandQueueStorageOptions options)
    {
        var commandsQueue = serviceProvider.GetRequiredService<ICommandsQueue>();
        var storage = commandsQueue
            .GetType()
            .GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(commandsQueue)!;
        var optionsField = storage
            .GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(MySqlCommandQueueStorageOptions));

        var registeredOptions = (MySqlCommandQueueStorageOptions)optionsField.GetValue(storage)!;

        Assert.Multiple(() =>
        {
            Assert.That(registeredOptions.TableName, Is.EqualTo(options.TableName));
            Assert.That(registeredOptions.CommandTimeoutSeconds, Is.EqualTo(options.CommandTimeoutSeconds));
            Assert.That(registeredOptions.InsertBatchSize, Is.EqualTo(options.InsertBatchSize));
        });
    }

    public static void AssertICommandSerializerContainsConverter<T>(this IServiceProvider serviceProvider)
    {
        var commandQueue = serviceProvider.GetRequiredService<ICommandsQueue>();
        var serializer = (ICommandSerializer)commandQueue
            .GetType()
            .GetField("_serializer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(commandQueue)!;

        var serializerOptions = serializer.GetOptions();
        Assert.That(serializerOptions.Converters.Any(c => c.GetType() == typeof(T)), Is.True);
    }

    public static void AssertICommandSerializerContainsConverterForWorker<TConverter>(
        this IServiceProvider serviceProvider)
    {
        var rawCommandHandler = GetCommandHandler(serviceProvider);
        var serializer = (ICommandDeserializer) rawCommandHandler
            .GetType()
            .GetField("_serializer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(rawCommandHandler)!;

        var serializerOptions = serializer.GetOptions();
        Assert.That(serializerOptions.Converters.Any(c => c.GetType() == typeof(TConverter)), Is.True);
    }

    public static void AssertServiceHasCommandType<TCommand>(this IServiceProvider serviceProvider)
    {
        var commandTypes = serviceProvider.GetServices<StorageSummarySource>().Single().CommandTypes;
        var commandType = commandTypes.FirstOrDefault(t => t == typeof(TCommand).FullName!);
        Assert.That(commandType, Is.Not.Null);
    }

}

public static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddCommandProcessingOptions(this IConfigurationBuilder builder,
        CommandProcessingOptions options,
        string? prefix = null)
    {
        var myConfiguration = new Dictionary<string, string>
        {
            { "PrefetchCount".WithPrefix(prefix), options.PrefetchCount.ToString() },
            { "WorkersCount".WithPrefix(prefix), options.WorkersCount.ToString() },
            { "InvisibilityTimeout".WithPrefix(prefix), options.InvisibilityTimeout.ToString() },
            { "IdleTimeout".WithPrefix(prefix), options.IdleTimeout.ToString() },
        };

        return builder.AddInMemoryCollection(myConfiguration!);
    }

    public static IConfigurationBuilder AddMySqlCommandStorageOptions(this IConfigurationBuilder builder,
        MySqlCommandQueueStorageOptions options,
        string? prefix = null)
    {
        var myConfiguration = new Dictionary<string, string>
        {
            { "TableName".WithPrefix(prefix), options.TableName },
            { "InsertBatchSize".WithPrefix(prefix), options.InsertBatchSize.ToString() },
            { "CommandTimeoutSeconds".WithPrefix(prefix), options.CommandTimeoutSeconds.ToString() },
        };

        return builder.AddInMemoryCollection(myConfiguration!);
    }

    public static IConfigurationBuilder AddMySqlCommandWorkerStorageOptions(this IConfigurationBuilder builder,
        MySqlLocalCommandsQueueWorkerStorageOptions options,
        string? prefix = null)
    {
        var myConfiguration = new Dictionary<string, string>
        {
            { "TableName".WithPrefix(prefix), options.TableName },
            { "ConnectionString".WithPrefix(prefix), options.ConnectionString },
            { "CommandTimeoutSeconds".WithPrefix(prefix), options.CommandTimeoutSeconds.ToString() },
        };

        return builder.AddInMemoryCollection(myConfiguration!);
    }

    internal static JsonSerializerOptions GetOptions(this ICommandSerializer serializer)
    {
        var optionsField = typeof(JsonCommandSerializer)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(JsonSerializerOptions));

        return (JsonSerializerOptions)optionsField.GetValue(serializer)!;
    }
    internal static JsonSerializerOptions GetOptions(this ICommandDeserializer serializer)
    {
        var optionsField = typeof(JsonCommandSerializer)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(JsonSerializerOptions));

        return (JsonSerializerOptions)optionsField.GetValue(serializer)!;
    }

    private static string WithPrefix(this string key, string? prefix = null) => !string.IsNullOrEmpty(prefix)
        ? $"{prefix}:{key}"
        : key;
}

public static class RandomExtensions
{
    public static int Not(this Random random, params int[] values)
    {
        while (true)
        {
            var result = random.Next();
            if (!values.Contains(result)) return result;
        }
    }
}