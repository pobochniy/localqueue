# Local command queue

Libraries to enqueue and handle local commands

## Install

```shell
dotnet add package AsuTrack.LocalQueue.MySql       # MySQL storage
dotnet add package AsuTrack.LocalQueue.PostgreSql  # PostgreSQL storage
dotnet add package AsuTrack.LocalQueue.OpenTelemetry  # metrics, optional
```

Storage packages bring `AsuTrack.LocalQueue` with them.

## Use with MySQL storage

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
);
```

Register Command queue

```cs
services
    .AddLocalCommandQueue(c =>
        c.UseMySqlStorage(
            new MySqlCommandQueueStorageOptions
            {
                TableName = <tablename>,
            })
    );
```

Register Command worker

```cs
services
    .AddSingleton<MenuService>()
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
            options.Converters.Add(new LegacyUUIdConverter()); // your own JsonConverter
        });
    });
```

Inject `ICommandsQueue` in your code to enqueue commands in the same transaction as your data

```cs
public class Service
{
    private readonly ICommandsQueue _commandsQueue;

    public Service(ICommandsQueue commandsQueue)
    {
        _commandsQueue = commandsQueue;
    }

    public async Task DoWork(CancellationToken ct)
    {
        var command = new ResizeImageCommand();

        await using var connection = new MySqlConnection(<connectionstring>);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // ... your own changes in the same transaction

        await _commandsQueue.Enqueue(connection, transaction, new[] { command }, ct);

        await transaction.CommitAsync(ct);
    }
}
```

Pass `processingStartTime` (UTC) to `Enqueue` to postpone the processing of the commands.

## Use with PostgreSQL storage

Ensure table for command storage exists in your database

```sql
create table <name> (
   id uuid not null primary key,
   data jsonb not null,
   command_type varchar(250) not null,
   created_at_utc timestamptz not null,
   locked_till_utc timestamptz,
   try_count int not null default 0
);
create index on <name> (command_type, created_at_utc);
```

The table name may be qualified by the schema (`schema.table`), otherwise the table is looked up by the `search_path`
of the connection (`public` by default). Unquoted names are folded to lower case as PostgreSQL does.

Register Command queue and worker the same way as with MySQL, by `UsePostgreSqlStorage` of the `AsuTrack.LocalQueue.PostgreSql`
package, and enqueue commands with an `NpgsqlConnection` and `NpgsqlTransaction`

```cs
services
    .AddLocalCommandQueue(c =>
        c.UsePostgreSqlStorage(new PostgreSqlCommandQueueStorageOptions
        {
            TableName = <tablename>,
        }))
    .AddLocalCommandQueueWorker(c =>
    {
        c.UsePostgreSqlStorage(new PostgreSqlLocalCommandsQueueWorkerStorageOptions
        {
            ConnectionString = <connectionstring>,
            TableName = <tablename>,
        });
        c.AddCommandHandler<ResizeImageCommand, ResizeImageHandler>();
    });
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

Keep `InvisibilityTimeout` above the processing time of the worker's commands, otherwise another instance
prefetches the command again and processes it in parallel; a handler that may run longer extends the lock itself
(see [Long commands](#long-commands)). A worker without handlers is a configuration error and fails on
registration. Give each command type to one worker per table: a type registered in two workers is processed by both,
the same way as by two instances.

Commands with no handler in any worker of the application are not prefetched and stay in the table: the storage
summary logs a warning with their types when they appear (the table may be shared with another application that
handles them), they are counted in the `local-command-queue-length` gauge as well. Custom storages
(`ILocalCommandsQueueWorkerStorage`) get the command types to prefetch in `Prefetch` and must filter by them;
`Delete` and `MakeInvisibleFor` get `TryCount` of the prefetch and must change the command in one statement only
while it has the same `TryCount`, returning whether it was changed.

The storage summary is collected once for the workers of the application with equal storages (`Equals`): MySQL
storages are equal for the same server, port, database and table, PostgreSQL storages for the same host, port,
database, schema and table (`DataSourceName` is `database.schema.table`). Storages that are not equal are collected
separately, the summaries of the storages with the same `DataSourceName` (the same table on different servers) are
summed up in the gauges.

## Long commands

A handler that runs for minutes in steps, each of them unpredictable (calls to an external model, for example),
extends the lock between the steps instead of setting `InvisibilityTimeout` for the worst case. Inject
`ICommandLease` into the handler: it is the lock on the command the handler is processing

```cs
public class GenerateReportHandler(ICommandLease lease, ReportBuilder builder) : ICommandHandler<GenerateReportCommand>
{
    public async Task Handle(GenerateReportCommand command, CancellationToken ct)
    {
        foreach (var step in builder.Steps(command))
        {
            await lease.Extend(TimeSpan.FromMinutes(10), ct); // invisible for 10 more minutes from now
            await step.Run(ct);
        }
    }
}
```

`Extend` keeps the command invisible for at least the given time from now and never shortens the lock: when it
already ends later, nothing is changed. When the lock ends earlier, it is taken for one and a half of the given
time, so extending before every step writes to the database once per a half of that time, however short the steps
are. A command left by a crashed instance comes back within one and a half of the extension, not after the longest
possible processing time. Retries inside one delivery count the extended lock as well; if all of them fail, the
command comes back when the extended lock is over, unless `RetryAfterFailure` hides it for another time.

The lease follows the command processed in the current async flow, so it may be injected into a handler of any
lifetime, a singleton included. Outside of a worker (the handler is called directly) there is no command to lock:
`Extend` logs a warning and does nothing; mock `ICommandLease` in the tests of the handler.

Extend the lock before it is over and with a margin: once it is over, another instance may prefetch the command
and process it in parallel. The storage tells the instances apart by `TryCount`, which every prefetch increments:
the lock is extended, the command is deleted or hidden after a failure only while it is still held by the
prefetch of this instance. A lock that is over but not taken by anyone is extended as usual. When another instance
has taken the command, `Extend` throws `CommandLeaseExpiredException`: the worker stops the processing without
retries and without deleting the command, the handler only has to let the exception go (wrapped into another
exception as well). `Extend` takes a positive duration of at most `ICommandLease.MaxDuration` (14 days) and throws
after the handler has returned, from work it has left running. The instance counts the lock by its own monotonic
clock from before each write to the database, so the clocks of the instance and of the database don't have to be
synchronized.

## Upgrading to 3.0

- Custom storages: `ILocalCommandsQueueWorkerStorage.Delete` and `MakeInvisibleFor` take `TryCount` of the prefetch
  and return `bool`, see [Workers for different command types](#workers-for-different-command-types).
- OpenTelemetry: the instruments are registered by `AddLocalCommandQueueInstrumentation`, the old method with
  the company prefix is removed.

## Use with OpenTelemetry

Register instruments by the `AsuTrack.LocalQueue.OpenTelemetry` package

```cs
services
    .AddOpenTelemetry()
    .WithMetrics(c => c
        .AddLocalCommandQueueInstrumentation()
        .AddPrometheusExporter());
```
