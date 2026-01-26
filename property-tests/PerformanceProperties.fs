module Clockworks.PropertyTests.PerformanceProperties

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Xunit
open Clockworks

[<Fact>]
let ``Bulk UUID generation completes under one second`` () =
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider, overflowBehavior = CounterOverflowBehavior.Auto)

    for _ in 1..1000 do
        factory.NewGuid() |> ignore

    let count = 60000
    let sw = Stopwatch.StartNew()
    for _ in 1..count do
        factory.NewGuid() |> ignore
    sw.Stop()

    Assert.True(
        sw.Elapsed < TimeSpan.FromSeconds(1.0),
        $"Generated {count} UUIDs in {sw.Elapsed.TotalMilliseconds:F2}ms")

[<Fact>]
let ``Concurrent UUID generation completes under one second`` () =
    let timeProvider = new SimulatedTimeProvider()
    use factory = new UuidV7Factory(timeProvider, overflowBehavior = CounterOverflowBehavior.Auto)

    for _ in 1..1000 do
        factory.NewGuid() |> ignore

    let count = 50000
    let sw = Stopwatch.StartNew()
    Parallel.For(0, count, fun _ -> factory.NewGuid() |> ignore) |> ignore
    sw.Stop()

    Assert.True(
        sw.Elapsed < TimeSpan.FromSeconds(1.0),
        $"Generated {count} UUIDs in parallel in {sw.Elapsed.TotalMilliseconds:F2}ms")

[<Fact>]
let ``Advancing many timers completes under one second`` () =
    let timeProvider = new SimulatedTimeProvider()
    let timerCount = 10000

    let timers =
        Array.init timerCount (fun _ ->
            timeProvider.CreateTimer(
                (fun _ -> ()),
                null,
                TimeSpan.FromMilliseconds(1.0),
                Timeout.InfiniteTimeSpan))

    let sw = Stopwatch.StartNew()
    timeProvider.Advance(TimeSpan.FromMilliseconds(1.0))
    sw.Stop()

    timers |> Array.iter (fun timer -> timer.Dispose())

    Assert.True(
        sw.Elapsed < TimeSpan.FromSeconds(1.0),
        $"Advanced {timerCount} timers in {sw.Elapsed.TotalMilliseconds:F2}ms")
