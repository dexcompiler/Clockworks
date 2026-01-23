module Clockworks.PropertyTests.TimeoutsProperties

open System
open FsCheck.Xunit
open Clockworks

/// Property: Positive timeouts cancel only after the due time
[<Property>]
let ``Timeout cancels after due time`` (timeoutMs: uint16) (partialMs: uint16) =
    let safeTimeoutMs = int64 (timeoutMs % 10000us) + 1L
    let safePartialMs = int64 partialMs % safeTimeoutMs

    let timeProvider = new SimulatedTimeProvider()
    use cts = Timeouts.CreateTimeout(timeProvider, TimeSpan.FromMilliseconds(float safeTimeoutMs))

    timeProvider.Advance(TimeSpan.FromMilliseconds(float safePartialMs))
    let notCanceledYet = not cts.IsCancellationRequested

    timeProvider.Advance(TimeSpan.FromMilliseconds(float (safeTimeoutMs - safePartialMs)))
    let canceledAfter = cts.IsCancellationRequested

    notCanceledYet && canceledAfter

/// Property: Non-positive timeouts cancel immediately
[<Property>]
let ``Non-positive timeout cancels immediately`` (timeoutMs: int64) =
    let safeTimeoutMs = -(abs timeoutMs % 10000L)
    let timeProvider = new SimulatedTimeProvider()
    use cts = Timeouts.CreateTimeout(timeProvider, TimeSpan.FromMilliseconds(float safeTimeoutMs))
    cts.IsCancellationRequested
