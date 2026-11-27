# Beep.Scheduler - Generic Work Registration Scheduler

## Purpose
Provides a reusable scheduling framework for registering work items at configured intervals.
Supports multiple worker types (WebsiteCollector, BigDataProcess, etc.) and frequencies (daily, weekly, hourly).

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Beep.Scheduler Library                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────────┐         ┌──────────────────────┐    │
│  │   SchedulerService   │         │ IScheduleHandler     │    │
│  │  (Orchestrator)      │────────→ (Plugin Interface)    │    │
│  │                      │         └──────────────────────┘    │
│  │ - Polls Schedule DB  │                   ▲                  │
│  │ - Triggers handlers  │                   │                  │
│  │ - Logs execution     │          ┌────────┴────────┐         │
│  └──────────────────────┘          │                 │         │
│            │                       │                 │         │
│            ▼                  ┌─────────┐      ┌─────────┐    │
│  ┌──────────────────────┐   │Website- │      │BigData- │    │
│  │  Database Schema     │   │Collector│      │Process  │    │
│  │                      │   │Handler  │      │Handler  │    │
│  │ - Schedules          │   └─────────┘      └─────────┘    │
│  │ - ScheduleFrequency  │                                     │
│  │ - ExecutionLog       │         (Registered via DI)         │
│  └──────────────────────┘                                     │
│            ▲                                                   │
│            │                                                   │
│       [Scheduler]                                             │
│       Database                                                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

Consumer Projects:
┌─────────────────────────────────────┐  ┌──────────────────────┐
│ Beep.Collector             │  │ BigDataProcess       │
├─────────────────────────────────────┤  ├──────────────────────┤
│ Handlers/                           │  │ Handlers/            │
│  └─ WebsiteCollectorScheduler...    │  │  └─ BigDataScheduler │
│     Handler.cs                      │  │     Handler.cs       │
│                                     │  │                      │
│ DependencyInjection.cs              │  │ DependencyInjection. │
│  └─ RegisterScheduleHandler()       │  │  cs                  │
└─────────────────────────────────────┘  └──────────────────────┘
```

## Project Structure

```
src/Beep.Scheduler/
├── Beep.Scheduler.csproj
├── README.md (this file)
│
├── Resource/
│   └── 0.sql                           (Database schema)
│
├── Domain/
│   ├── ScheduleFrequency.cs            (Enum: Hourly, Daily, Weekly, Monthly, Yearly)
│   ├── ScheduleTimeZone.cs             (Resolves the TimeZone setting)
│   └── WorkerSchedule.cs               (Model)
│
├── Handlers/
│   ├── Interfaces/
│   │   └── IScheduleHandler.cs         (Interface for implementations)
│   └── ScheduleHandlerResult.cs
│
├── Services/
│   ├── Interfaces/
│   │   └── IScheduleService.cs
│   ├── ScheduleInfo.cs
│   ├── ScheduleManagementService.cs
│   └── SchedulerService.cs             (Queues due schedules as ServiceBus work items)
│
├── Workers/
│   ├── SchedulerWorker.cs              (Claims and runs schedule work items)
│   └── SchedulerWorkProcessor.cs
│
└── Infrastructure/
    ├── Interfaces/
    │   └── ISchedulerOption.cs
    ├── DependencyInjection.cs
    ├── SchedulerOption.cs              (The Scheduler settings)
    ├── SchedulerRepository.cs
    └── SchedulerWorkerOptions.cs
```

## How It Works

```
Scheduler Service Loop:
├─ Every 30 seconds:
│   ├─ Query Schedules table
│   ├─ For each schedule:
│   │  ├─ Check if it's due to run
│   │  ├─ If yes: Find matching handler
│   │  ├─ Execute handler.RegisterItemsAsync()
│   │  ├─ Log execution
│   │  └─ Update last run time
│   └─ Sleep 30 seconds
│
└─ Repeat until shutdown
```

## Usage Example

```csharp
// In Beep.Collector DependencyInjection.cs
services.RegisterScheduler(configuration);  // Register framework
services.RegisterScheduleHandler<WebsiteCollectorScheduleHandler>();

// Frame automatically handles the rest!
```

## Database Configuration

On startup, `ScheduleInitializationService` creates or updates the schedule from the `Scheduler` settings
(`IScheduleService.CreateOrUpdateScheduleAsync`). A schedule's times of day are rows of `ScheduleStartTimes`;
by hand:

```sql
-- Daily at 08:30, 12:00 and 16:00
DECLARE @id uniqueidentifier = NEWID();
INSERT INTO Scheduler.Schedules (Id, HandlerType, Frequency, IsActive)
VALUES (@id, 'WebsiteCollector', 'Daily', 1);
INSERT INTO Scheduler.ScheduleStartTimes (ScheduleId, StartTime)
VALUES (@id, '08:30:00'), (@id, '12:00:00'), (@id, '16:00:00');

-- Weekly schedule (Sundays at midnight)
SET @id = NEWID();
INSERT INTO Scheduler.Schedules (Id, HandlerType, Frequency, DayOfWeek, IsActive)
VALUES (@id, 'BigDataProcess', 'Weekly', 0, 1);
INSERT INTO Scheduler.ScheduleStartTimes (ScheduleId, StartTime) VALUES (@id, '00:00:00');

-- Monthly schedule (15th of each month at 09:00)
SET @id = NEWID();
INSERT INTO Scheduler.Schedules (Id, HandlerType, Frequency, DayOfMonth, IsActive)
VALUES (@id, 'ReportProcess', 'Monthly', 15, 1);
INSERT INTO Scheduler.ScheduleStartTimes (ScheduleId, StartTime) VALUES (@id, '09:00:00');

-- Yearly schedule (January 1st at midnight)
-- DayOfYear = 1 for Jan 1, = 32 for Feb 1, = 60 for Mar 1, etc.
SET @id = NEWID();
INSERT INTO Scheduler.Schedules (Id, HandlerType, Frequency, DayOfYear, IsActive)
VALUES (@id, 'AnnualReport', 'Yearly', 1, 1);
INSERT INTO Scheduler.ScheduleStartTimes (ScheduleId, StartTime) VALUES (@id, '00:00:00');

-- Hourly schedule (no start times)
INSERT INTO Scheduler.Schedules (HandlerType, Frequency, IsActive)
VALUES ('FrequentProcess', 'Hourly', 1);
```

## Schedule Configuration Reference

| Frequency | Required Fields | Optional Fields | Example |
|-----------|-----------------|-----------------|---------|
| **Daily** | StartTimes | - | Every day at 01:00; or at 08:30, 12:00 and 16:00 |
| **Hourly** | - (no StartTimes) | - | An hour after the previous run started |
| **Weekly** | StartTimes, DayOfWeek | - | Every Sunday (DayOfWeek=0) at midnight |
| **Monthly** | StartTimes, DayOfMonth | - | 15th of each month at 9:00 AM |
| **Yearly** | StartTimes, DayOfYear | - | January 1st (DayOfYear=1) at midnight |
| **Never** | - | - | Schedule disabled |

`StartTimes` and the day settings are read in the schedule's **time zone** (`TimeZone`: an IANA id
such as `Asia/Bangkok` or a Windows id such as `SE Asia Standard Time`; empty means the machine's
local time zone). Daylight saving is followed, and "once a day" means once per local day. The zone is
stored as its Windows id, which is what SQL Server's `AT TIME ZONE` takes; an id the server does
not know (`sys.time_zone_info`) is rejected when the schedule is saved. "Now" is the database clock.

A schedule runs once at or after each of its `StartTimes` (e.g. `["08:30", "12:00", "16:00"]`) on a matching
day: at the first poll after the time, and a run counts for the latest time that had passed when it started.
A worker that was down over one or more times catches up with one run when it starts, then waits for the next
time. Each handler has one schedule (`UQ_Schedules_HandlerType`).

`WorkerType` means the same as in every other section: the ServiceBus worker type
(`WorkItems.WorkerType`) of the work items, here the schedule runs. `HandlerType` is which
`IScheduleHandler` the schedule runs (its `HandlerType`); that handler then enqueues work for its own worker
type (`WebsiteCollector`). The worker does not start if no registered handler has that type.

```json
"Scheduler": {
  "WorkerType": "Scheduler",
  "HandlerType": "WebsiteCollector",
  "Frequency": "Weekly",
  "StartTimes": [ "01:00" ],
  "DayOfWeek": 1,
  "TimeZone": "Asia/Bangkok"
}
```

Several times a day:

```json
"Scheduler": {
  "HandlerType": "WebsiteCollector",
  "Frequency": "Daily",
  "StartTimes": [ "08:30", "12:00", "16:00" ],
  "TimeZone": "Asia/Bangkok"
}
```

Running a schedule is a ServiceBus work item (worker type `Scheduler:WorkerType`, default `Scheduler`; DomainType `WorkerSchedule`):

- `SchedulerService` polls `GetDueSchedules` (read-only) and enqueues a work item per due schedule.
  Every instance can poll: the ServiceBus skips a schedule that already has an unfinished work item.
- `SchedulerWorker` claims the work item, checks the schedule is still due (a late duplicate does
  nothing), and runs its `IScheduleHandler`. The lease is renewed while the handler runs, so long runs
  are fine; if the instance dies, the run is reclaimed when the lease expires.
- A failed run is retried by the ServiceBus with back-off (`SchedulerWorkerOptions.MaxAttempts`,
  default 3). The last failed attempt still sets `LastRunAt`, so the schedule waits for its next period.

### Day of Week Values (for Weekly)
- 0 = Sunday
- 1 = Monday
- 2 = Tuesday
- 3 = Wednesday
- 4 = Thursday
- 5 = Friday
- 6 = Saturday

### Day of Year Values (for Yearly)
- 1 = January 1st
- 32 = February 1st
- 60 = March 1st (non-leap year)
- 61 = March 1st (leap year)
- 365/366 = December 31st

## Next: See individual files for implementation details
