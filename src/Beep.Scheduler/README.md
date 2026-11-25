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
│  │ - WorkerSchedules    │   └─────────┘      └─────────┘    │
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
│ WebsiteCollector.Worker             │  │ BigDataProcess       │
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
│   ├── ScheduleFrequency.cs            (Enum: Daily, Weekly, Monthly)
│   └── WorkerSchedule.cs               (Model)
│
├── Handlers/
│   └── IScheduleHandler.cs             (Interface for implementations)
│
├── Services/
│   └── SchedulerService.cs             (Main orchestrator)
│
├── Infrastructure/
│   ├── DependencyInjection.cs
│   └── Database/
│       └── SchedulerDatabase.cs        (Query builder)
│
└── Models/
	├── ScheduleExecutionLog.cs
	└── ScheduleStatus.cs
```

## How It Works

```
Scheduler Service Loop:
├─ Every 30 seconds:
│   ├─ Query WorkerSchedules table
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
// In WebsiteCollector.Worker DependencyInjection.cs
services.RegisterScheduler(configuration);  // Register framework
services.RegisterScheduleHandler<WebsiteCollectorScheduleHandler>();

// Frame automatically handles the rest!
```

## Database Configuration

On startup, create schedules:

```sql
-- Daily schedule
INSERT INTO Scheduler.WorkerSchedules (WorkerType, Frequency, StartTime, IsActive)
VALUES ('WebsiteCollector', 'Daily', '01:00:00', 1);

-- Weekly schedule (Sundays at midnight)
INSERT INTO Scheduler.WorkerSchedules (WorkerType, Frequency, StartTime, DayOfWeek, IsActive)
VALUES ('BigDataProcess', 'Weekly', '00:00:00', 0, 1);

-- Monthly schedule (15th of each month)
INSERT INTO Scheduler.WorkerSchedules (WorkerType, Frequency, StartTime, DayOfMonth, IsActive)
VALUES ('ReportProcess', 'Monthly', '09:00:00', 15, 1);

-- Yearly schedule (January 1st at midnight)
-- DayOfYear = 1 for Jan 1, = 32 for Feb 1, = 60 for Mar 1, etc.
INSERT INTO Scheduler.WorkerSchedules (WorkerType, Frequency, StartTime, DayOfYear, IsActive)
VALUES ('AnnualReport', 'Yearly', '00:00:00', 1, 1);

-- Hourly schedule
INSERT INTO Scheduler.WorkerSchedules (WorkerType, Frequency, StartTime, IsActive)
VALUES ('FrequentProcess', 'Hourly', '00:00:00', 1);
```

## Schedule Configuration Reference

| Frequency | Required Fields | Optional Fields | Example |
|-----------|-----------------|-----------------|---------|
| **Daily** | StartTime | - | Every day at 1:00 AM |
| **Hourly** | - (StartTime is ignored) | - | An hour after the previous run started |
| **Weekly** | StartTime, DayOfWeek | - | Every Sunday (DayOfWeek=0) at midnight |
| **Monthly** | StartTime, DayOfMonth | - | 15th of each month at 9:00 AM |
| **Yearly** | StartTime, DayOfYear | - | January 1st (DayOfYear=1) at midnight |
| **Never** | - | - | Schedule disabled |

All times are **UTC** on the database clock. A schedule runs at the first poll at or after its
`StartTime` on a matching day, at most once that day (a worker that was down at `StartTime` catches
up when it starts). Each `WorkerType` has one schedule (`UQ_WorkerSchedules_WorkerType`).

With several worker instances, `ClaimDueSchedules` hands each due schedule to exactly one of them.
The claim lasts `claimLeaseSeconds` (`RegisterScheduler`, default 1 hour): if that instance dies
mid-run, another one runs the schedule once the claim expires. Set it longer than your slowest run.

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
