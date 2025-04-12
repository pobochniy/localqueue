using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddLocalCommandQueue();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertLocalCommandQueueRegistered();
    }
    
    [Test]
    public void ShouldRegisterJsonSerializerAsDefault_WhenRegisterCommandQueue()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var serializer = serviceProvider.GetRequiredService<ICommandSerializer>();

        Assert.That(serializer, Is.TypeOf<JsonCommandSerializer>());
    }

    [Test]
    public void CanAddConverterToSerializerOptions_WhenRegisterCommandQueue()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueue(c =>
        {
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

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddLocalCommandQueueWorker(c => 
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>()
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandRegistered<LocalCommand>();
    }

    [Test]
    public void CanRegisterHandlingAction()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddLocalCommandQueueWorker(c => 
            c.AddCommandHandler<LocalCommand, Mediator>(h => h.Send)
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandRegistered<LocalCommand>();
    }

    [Test]
    public void CanRegisterHandlingFunction()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddLocalCommandQueueWorker(c =>
            c.AddCommandHandler<LocalCommand>((sp, command, ct) =>
                sp.GetRequiredService<Mediator>().Send(command, ct))
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandRegistered<LocalCommand>();
    }

    [Test]
    public void ShouldRegisterJsonSerializerAsDefault()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueueWorker(c =>
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>()
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var deserializer = serviceProvider.GetRequiredService<ICommandDeserializer>();
        var serializer = serviceProvider.GetRequiredService<ICommandSerializer>();

        Assert.That(deserializer, Is.TypeOf<JsonCommandSerializer>());
        Assert.That(serializer, Is.TypeOf<JsonCommandSerializer>());
    }

    [Test]
    public void CanAddConverterToSerializerOptions()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
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

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddLocalCommandQueueWorker(configuration.GetSection("LocalCommandsQueue"),
            c => c.AddCommandHandler<LocalCommand, LocalCommandHandler>()
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

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddLocalCommandQueueWorker(c =>
        {
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

        serviceCollection.TryAddSingleton(Mock.Of<ICommandsStorage>());
        serviceCollection.AddLocalCommandQueueWorker(options, c =>
        {
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandProcessingOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMysqlStorageFromIConfiguration()
    {
        var options = new CommandStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddCommandStorageOptions(options, prefix: "MySqlLocalCommandsQueue")
            .Build();
        
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddMySqlCommandsStorage(configuration.GetSection("MySqlLocalCommandsQueue"));

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMysqlStorageFromAction()
    {
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddMySqlCommandsStorage((_, c) =>
        {
            c.ConnectionString = options.ConnectionString;
            c.TableName = options.TableName;
            c.InsertBatchSize = options.InsertBatchSize;
            c.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMysqlStorageFromInstance()
    {
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddMySqlCommandsStorage(options);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
    }

    [Test]
    public void CanConfigureMySqlCommandQueueFromIConfiguration()
    {
        var options = new CommandStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddCommandStorageOptions(options, prefix: "MySqlLocalCommandsQueue")
            .Build();
        
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddMySqlCommandQueue(configuration.GetSection("MySqlLocalCommandsQueue"), queueConfigure: c =>
        {
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueFromAction()
    {
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddMySqlCommandQueue(storageConfigure: (_, c) =>
            {
                c.ConnectionString = options.ConnectionString;
                c.TableName = options.TableName;
                c.InsertBatchSize = options.InsertBatchSize;
                c.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            },
            queueConfigure: c =>
            {
                c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueFromInstance()
    {
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddMySqlCommandQueue(options, queueConfigure: c =>
        {
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerFromIConfiguration()
    {
        var options = new CommandStorageOptionsFake();
        var configuration = new ConfigurationBuilder()
            .AddCommandStorageOptions(options, prefix: "MySqlLocalCommandsQueue")
            .Build();
        
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddMySqlCommandQueueWorker(configuration.GetSection("MySqlLocalCommandsQueue"), workerConfigure: c =>
        {
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerFromAction()
    {
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddMySqlCommandQueueWorker(storageConfigure: (_, c) =>
            {
                c.ConnectionString = options.ConnectionString;
                c.TableName = options.TableName;
                c.InsertBatchSize = options.InsertBatchSize;
                c.CommandTimeoutSeconds = options.CommandTimeoutSeconds;
            },
            workerConfigure: c =>
            {
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
                c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerFromInstance()
    {
        var configurator = new LocalCommandQueueWorkerConfiguratorFake();
        configurator.AddCommandHandler<LocalCommand, LocalCommandHandler>();
        configurator.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddMySqlCommandQueueWorker(options, configurator);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
    }

    [Test]
    public void CanConfigureMySqlCommandQueueWorkerAsExample()
    {
        var options = new CommandStorageOptionsFake();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddSingleton<LocalCommandHandler>();
        serviceCollection.AddMySqlCommandQueueWorker(options, workerConfigure: c =>
        {
            c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            c.ConfigureSerializer(o => { o.Converters.Add(new LocalStringConverter()); });
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.AssertCommandStorageOptionsAreSame(options);
        serviceProvider.AssertCommandRegistered<LocalCommand>();
        serviceProvider.AssertICommandSerializerContainsConverter<LocalStringConverter>();
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
            IdleTimeout = TimeSpan.FromSeconds(Random.Shared.Not(3, PrefetchCount, WorkersCount, InvisibilityTimeout.Seconds));
        }
    }

    private class LocalCommandQueueWorkerConfiguratorFake : LocalCommandQueueWorkerConfigurator
    {
        public LocalCommandQueueWorkerConfiguratorFake()
        {
            PrefetchCount = Random.Shared.Not(10);
            WorkersCount = Random.Shared.Not(10, PrefetchCount);
            InvisibilityTimeout = TimeSpan.FromSeconds(Random.Shared.Not(10, PrefetchCount, WorkersCount));
            IdleTimeout = TimeSpan.FromSeconds(Random.Shared.Not(3, PrefetchCount, WorkersCount, InvisibilityTimeout.Seconds));
        }
    }

    private class CommandStorageOptionsFake : CommandStorageOptions
    {
        public CommandStorageOptionsFake()
        {
            ConnectionString = $"connectionString_{TestContext.CurrentContext.Test.Name}";
            TableName = $"table_{TestContext.CurrentContext.Test.Name}";
            InsertBatchSize = Random.Shared.Not(100);
            CommandTimeoutSeconds = Random.Shared.Not(5, InsertBatchSize);
        }
    }
}

public static class ServiceProviderAssertionExtension
{
    public static void AssertCommandRegistered<TCommand>(this ServiceProvider serviceProvider)
    {
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var commandHandler = serviceProvider.GetService<ICommandHandler<TCommand>>();
        Assert.That(commandHandler, Is.Not.Null);
        
        var commandProcessorHostedService =
            hostedServices.OfType<CommandProcessorHostedService>().SingleOrDefault();
        var fetchCommandHostedService =
            hostedServices.OfType<FetchCommandHostedService>().SingleOrDefault();
        Assert.That(commandProcessorHostedService, Is.Not.Null);
        Assert.That(fetchCommandHostedService, Is.Not.Null);
    }

    public static void AssertCommandProcessingOptionsAreSame(this IServiceProvider serviceProvider, CommandProcessingOptions options)
    {
        var registeredOptions = serviceProvider.GetRequiredService<CommandProcessingOptions>();
        
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

    public static void AssertCommandStorageOptionsAreSame(this IServiceProvider serviceProvider, CommandStorageOptions options)
    {
        var storage = (MySqlCommandStorage) serviceProvider.GetRequiredService<ICommandsStorage>();
        var optionsField = storage
            .GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(CommandStorageOptions));

        var registeredOptions = (CommandStorageOptions) optionsField.GetValue(storage)!;
        
        Assert.Multiple(() =>
        {
            Assert.That(registeredOptions.ConnectionString, Is.EqualTo(options.ConnectionString));
            Assert.That(registeredOptions.TableName, Is.EqualTo(options.TableName));
            Assert.That(registeredOptions.InsertBatchSize, Is.EqualTo(options.InsertBatchSize));
            Assert.That(registeredOptions.CommandTimeoutSeconds, Is.EqualTo(options.CommandTimeoutSeconds));
        });
    }

    public static void AssertICommandSerializerContainsConverter<T>(this IServiceProvider serviceProvider)
    {
        var serializer = serviceProvider.GetRequiredService<ICommandSerializer>();
        var serializerOptions = serializer.GetOptions();
        Assert.That(serializerOptions.Converters.Any(c => c.GetType() == typeof(T)), Is.True);
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
    
    public static IConfigurationBuilder AddCommandStorageOptions(this IConfigurationBuilder builder,
        CommandStorageOptions options,
        string? prefix = null)
    {
        var myConfiguration = new Dictionary<string, string>
        {
            { "ConnectionString".WithPrefix(prefix), options.ConnectionString },
            { "TableName".WithPrefix(prefix), options.TableName },
            { "InsertBatchSize".WithPrefix(prefix), options.InsertBatchSize.ToString() },
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
