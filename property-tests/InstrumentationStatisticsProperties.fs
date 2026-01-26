module Clockworks.PropertyTests.InstrumentationStatisticsProperties

open System
open System.Threading
open FsCheck.Xunit
open Clockworks
open Clockworks.Instrumentation

/// Property: Advance calls track call count and ticks.
[<Property>]
let ``Advance statistics track calls and ticks`` (advances: uint16 list) =
    let safeAdvances =
        advances
        |> List.map (fun ms -> int64 (ms % 1000us) + 1L)
        |> List.truncate 20

    let advancesToUse = if List.isEmpty safeAdvances then [ 1L ] else safeAdvances

    let timeProvider = new SimulatedTimeProvider()
    let stats = timeProvider.Statistics
    stats.Reset()

    for ms in advancesToUse do
        timeProvider.Advance(TimeSpan.FromMilliseconds(float ms))

    let expectedCalls = int64 advancesToUse.Length
    let expectedTicks =
        advancesToUse
        |> List.sumBy (fun ms -> TimeSpan.FromMilliseconds(float ms).Ticks)

    stats.AdvanceCalls = expectedCalls && stats.AdvanceTicks = expectedTicks

/// Property: Creating timers updates queue statistics deterministically.
[<Property>]
let ``Timer creation updates queue statistics`` (count: byte) =
    let safeCount = int count % 20 + 1
    let timeProvider = new SimulatedTimeProvider()
    let stats = timeProvider.Statistics
    stats.Reset()

    let timers =
        [ for _ in 1..safeCount ->
            timeProvider.CreateTimer((fun _ -> ()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan) ]

    let ok =
        stats.TimersCreated = int64 safeCount
        && stats.MaxQueueLength = int64 safeCount
        && stats.QueueEnqueues = int64 safeCount

    timers |> List.iter (fun timer -> timer.Dispose())
    ok

/// Property: Timer Change operations are recorded in statistics.
[<Property>]
let ``Timer Change increments statistics`` (count: byte) =
    let safeCount = int count % 5 + 1
    let timeProvider = new SimulatedTimeProvider()
    let stats = timeProvider.Statistics
    stats.Reset()

    let timer =
        timeProvider.CreateTimer((fun _ -> ()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan)

    for _ in 1..safeCount do
        timer.Change(TimeSpan.FromMilliseconds(1.0), Timeout.InfiniteTimeSpan) |> ignore

    let ok = stats.TimerChanges = int64 safeCount
    timer.Dispose()
    ok

/// Property: One-shot timers update fired and disposed counters when due.
[<Property>]
let ``One-shot timers update fired and disposed statistics`` (count: byte) =
    let safeCount = int count % 20 + 1
    let timeProvider = new SimulatedTimeProvider()
    let stats = timeProvider.Statistics
    stats.Reset()

    let timers =
        [ for _ in 1..safeCount ->
            timeProvider.CreateTimer((fun _ -> ()), null, TimeSpan.FromMilliseconds(1.0), Timeout.InfiniteTimeSpan) ]

    timeProvider.Advance(TimeSpan.FromMilliseconds(1.0))

    let ok =
        stats.CallbacksFired = int64 safeCount
        && stats.TimersDisposed = int64 safeCount

    timers |> List.iter (fun timer -> timer.Dispose())
    ok

/// Property: Periodic timers record reschedules when firing.
[<Property>]
let ``Periodic timer reschedule is recorded`` () =
    let timeProvider = new SimulatedTimeProvider()
    let stats = timeProvider.Statistics
    stats.Reset()

    use _timer =
        timeProvider.CreateTimer((fun _ -> ()), null, TimeSpan.FromMilliseconds(1.0), TimeSpan.FromMilliseconds(5.0))

    timeProvider.Advance(TimeSpan.FromMilliseconds(1.0))

    stats.CallbacksFired = 1L && stats.PeriodicReschedules = 1L

/// Property: Positive timeouts update statistics after firing.
[<Property>]
let ``Timeout statistics update after positive timeout`` (timeoutMs: uint16) =
    let safeTimeoutMs = int64 (timeoutMs % 5000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    let stats = new TimeoutStatistics()
    stats.Reset()

    let cts =
        Timeouts.CreateTimeout(timeProvider, TimeSpan.FromMilliseconds(float safeTimeoutMs), stats)

    let createdOk = stats.Created = 1L && stats.Fired = 0L && stats.Disposed = 0L

    timeProvider.Advance(TimeSpan.FromMilliseconds(float safeTimeoutMs))

    let firedOk = stats.Created = 1L && stats.Fired = 1L && stats.Disposed = 1L

    cts.Dispose()
    createdOk && firedOk

/// Property: Non-positive timeouts are counted as fired and disposed immediately.
[<Property>]
let ``Timeout statistics update immediately for non-positive timeout`` (timeoutMs: int16) =
    let nonPositiveMs = -(abs (int64 timeoutMs) % 5000L)
    let timeProvider = new SimulatedTimeProvider()
    let stats = new TimeoutStatistics()
    stats.Reset()

    let cts =
        Timeouts.CreateTimeout(timeProvider, TimeSpan.FromMilliseconds(float nonPositiveMs), stats)

    stats.Created = 1L
    && stats.Fired = 1L
    && stats.Disposed = 1L
    && cts.IsCancellationRequested

/// Property: Disposing a timeout handle before it fires records a dispose without a fire.
[<Property>]
let ``Timeout handle disposal updates statistics without firing`` (timeoutMs: uint16) =
    let safeTimeoutMs = int64 (timeoutMs % 5000us) + 1L
    let timeProvider = new SimulatedTimeProvider()
    let stats = new TimeoutStatistics()
    stats.Reset()

    let handle =
        Timeouts.CreateTimeoutHandle(timeProvider, TimeSpan.FromMilliseconds(float safeTimeoutMs), stats)

    handle.Dispose()

    stats.Created = 1L && stats.Fired = 0L && stats.Disposed = 1L
