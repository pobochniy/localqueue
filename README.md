# Local command queue

Libraries to enqueue and handle local commands

## Use with MySql storage 

Ensure table for command storage exists in your database

```mysql
create table `<name>` (
   Id binary(16) not null primary key ,
   `Data` json NOT NULL,
   CommandType varchar(250) not null,
   CreatedAtUtc datetime not null,
   LockedTillUtc datetime,
   TryCount int not null default(0),
   index `CommandType_CreatedAtUtc_IDX` (`CommandType`, `CreatedAtUtc`)
)
```
Register Command queue

```cs
services
    .AddLocalCommandQueue(c =>
        c.UseMySqlStorage(
            new MySqlCommandQueueStorageOptions
            {
                TableName = <tablename>;
            })
    );
```

Миграция с версии 0.0.5

Вместо отдельной регистрации репозитория используйте при конфигурации AddLocalCommandQueue метод UseMySqlStorage.

Устаревшая регистрация (версия 0.0.5)
```cs
    servicesCollection.AddMySqlCommandsStorage((sp, c) =>
    {
        c.ConnectionString = <connectionstring>;
        c.TableName = <tablename>;
    });
    servicesCollection.AddLocalCommandQueue();
```

Register Command worker

```cs
services
    .AddSingletone<MenuService>()
    .AddScoped<ResizeImageHandler>() // scoped for example
    .AddLocalCommandQueueWorker(c =>
    {
        c.UseMySqlStorage(new MySqlLocalCommandsQueueWorkerStorageOptions
        {
            ConnectionString = <connectionstring>,
            TableName = <tablename>,
        });
        c.PrefetchCount = 10;
        c.AddCommandHandler<ResizeImageCommand, ResizeImageHandler>();
        c.AddCommandHandler<UpdateMenuCommand, MenuService>(service => service.Process);
        c.ConfigureSerializer(options =>
        {
            options.Converters.Add(new LegacyUUIdConverter());
        });
    });
```

Миграция с версии 0.0.5

Вместо отдельной регистрации репозитория используйте при конфигурации AddLocalCommandQueueWorker метод UseMySqlStorage.

Устаревшая регистрация (версия 0.0.5)
```cs
    services
    .AddSingletone<MenuService>()
    .AddScoped<ResizeImageHandler>() // scoped for example
    .AddMySqlCommandsStorage((sp, c) =>
    {
        c.ConnectionString = <connectionstring>;
        c.TableName = <tablename>;
    })
    .AddLocalCommandQueueWorker(c =>
    {
        c.PrefetchCount = 10;
        c.AddCommandHandler<ResizeImageCommand, ResizeImageHandler>();
        c.AddCommandHandler<UpdateMenuCommand, MenuService>(service => service.Process);
        c.ConfigureSerializer(options =>
        {
            options.Converters.Add(new LegacyUUIdConverter());
        });
    });
```

Inject `ICommandsQueue` in your code to enqueue commands
```cs
public class Service {
    private ICommandsQueue _commandsQueue;
    
    public Service(ICommandsQueue commandsQueue) {
        _commandsQueue = commandsQueue;
    }
    
    public Task DoWork() {
        ...
        var command = new LocalCommand();

        await using var connection = new MySqlConnection(_database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync();

        await commandsQueue.Enqueue(connection, transaction, new [] { command }, CancellationToken.None);

        await transaction.CommitAsync();
        ...
    }
}
```

## Retries

Every command type gets its own retry policy. Without one a failing command is tried 3 times with 500ms
between tries, after which the worker lets go of it: the `InvisibilityTimeout` lock taken at prefetch expires
and a later prefetch picks the command up again.

Pass `RetryPolicyOptions` to `AddCommandHandler` for fixed timings

```cs
c.AddCommandHandler<ResizeImageCommand, ResizeImageHandler>(new RetryPolicyOptions
{
    MaxRetryCount = 3,
    BackoffInterval = TimeSpan.FromMilliseconds(500),
    RetryAfterFailure = TimeSpan.FromMinutes(1),
});
```

The settings act on two levels. `MaxRetryCount` and `BackoffInterval` work inside one delivery — the worker
keeps the command in hand and tries it again after the interval. `RetryAfterFailure` applies once those tries
are spent: the command is hidden for that long and delivered again later. Left `null`, it is not rescheduled
explicitly and returns when its lock expires.

`MaxRetryCount` counts tries rather than retries on top of the first one, so `1` means a single try. Below `1`
the handler never runs and the command is still rescheduled as a failure.

Implement `ICommandRetryPolicy` to derive the timings from the delivery history instead

```cs
public class ExponentialRetryPolicy : ICommandRetryPolicy
{
    public int MaxRetryCount => 3;

    public TimeSpan BackoffFor(int retry) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, retry - 1));

    public TimeSpan? RedeliverAfter(int attempt) => TimeSpan.FromMinutes(attempt);
}
```

```cs
c.AddCommandHandler<ResizeImageCommand, ResizeImageHandler>(new ExponentialRetryPolicy());
```

`retry` is 1-based within the current delivery and starts over on the next one. `attempt` is `TryCount` from
the command row — it is 1 on the first delivery and keeps growing, so it survives restarts and lets a command
that keeps failing back off further every time.

## Workers for different command types

A worker prefetches only the command types it has handlers for, so several workers can share one table
without taking each other's commands. Register a worker per group of commands that must not wait for each
other — each one has its own `WorkersCount`, `PrefetchCount` and `InvisibilityTimeout`

```cs
services
    .AddLocalCommandQueueWorker(c =>
    {
        c.UseMySqlStorage(new MySqlLocalCommandsQueueWorkerStorageOptions
        {
            ConnectionString = <connectionstring>,
            TableName = <tablename>,
        });
        c.WorkersCount = 1;
        c.InvisibilityTimeout = TimeSpan.FromMinutes(30); // renders for minutes
        c.AddCommandHandler<RenderVideoCommand, RenderVideoHandler>();
    })
    .AddLocalCommandQueueWorker(c =>
    {
        c.UseMySqlStorage(new MySqlLocalCommandsQueueWorkerStorageOptions
        {
            ConnectionString = <connectionstring>,
            TableName = <tablename>,
        });
        c.WorkersCount = 4;
        c.InvisibilityTimeout = TimeSpan.FromMinutes(2);
        c.AddCommandHandler<ResizeImageCommand, ResizeImageHandler>();
    });
```

The lock is not extended while a command is processed: keep `InvisibilityTimeout` above the longest processing
time of the worker's commands, otherwise another instance prefetches the command again and processes it in
parallel. A worker without handlers is a configuration error and fails on registration. Give each command type
to one worker per table: a type registered in two workers is processed by both, the same way as by two instances.

Migration from 0.1.0 (a breaking change, released as 0.2.0): `ILocalCommandsQueueWorkerStorage.Prefetch` takes the
command types to prefetch, custom storages must filter by them and be rebuilt against 0.2.0. Commands with no handler
in any worker of the application are no longer prefetched and stay in the table: the storage summary logs a warning
with their types when they appear (the table may be shared with another application that handles them), they are
counted in the `local-command-queue-length` gauge as well. The MySQL storage filters by `CommandType`, add an index
for it to existing tables, otherwise the prefetch scans and locks the commands of other workers. The index on
`LockedTillUtc` is no longer used, drop it

```mysql
alter table `<name>` add index `CommandType_CreatedAtUtc_IDX` (`CommandType`, `CreatedAtUtc`);
alter table `<name>` drop index `<name>_LockedTillUtc_IDX`;
```

The storage summary is collected once for the workers of the application with equal storages (`Equals`): MySQL
storages are equal for the same server, port, database and table. Storages that are not equal are collected
separately, the summaries of the storages with the same `DataSourceName` (the same table on different servers) are
summed up in the gauges.

## Use with OpenTelemetry

Register instruments by LocalQueue.OpenTelemetry package
```cs
services
    .AddOpenTelemetry()
    .WithMetrics(c => 
        c.AddDodoLocalCommandQueueInstrumentation()
        c.AddPrometheusExporter());
```
