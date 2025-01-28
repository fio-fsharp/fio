(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private FIO.Tests

#nowarn "0988"

open System.Threading
open NUnit.Framework

open FIO.Core
open FIO.Runtime

[<TestFixture>]
type RuntimeTests() =

    let successResult (result: Result<'R, 'E>, expected: 'R) =
        match result with
        | Ok result -> result
        | Error error ->
            Assert.Fail($"Result contained Error ({error}) when it was expected to contain Ok ({expected})")
            failwith "Test failed"

    let failureResult (result: Result<'R, 'E>, expected: 'E) =
        match result with
        | Ok result ->
            Assert.Fail($"Result contained Ok ({result}) when it was expected to contain Error ({expected})")
            failwith "Test failed"
        | Error error -> error

    static member GenerateRuntimes() =
        seq {
            yield TestCaseData(Native.Runtime())
            yield TestCaseData(Intermediate.Runtime())
            yield TestCaseData(Advanced.Runtime())
        }

    [<TestCaseSource("GenerateRuntimes")>]
    member this.SucceedFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Jinsei x Boku"
        let effect = !+ expected

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.FailFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Niche Syndrome"
        let effect = !- expected

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.StopFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = ()
        let effect = !+ ()

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.SendMessageFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Beam of Light"
        let channel = Channel()
        let effect = expected --> channel

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(channel.Count(), Is.EqualTo(1))
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ReceiveMessageFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Zeitakubyo"
        let channel = Channel()

        let effect =
            expected --> channel >>= fun _ ->
            !--> channel >>= fun result ->
            !+ result

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(channel.Count(), Is.EqualTo(0))
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ConcurrentlyAndAwaitSucceedFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "ONE OK ROCK"

        let effect =
            !~> !+ expected >>= fun fiber ->
            !<~ fiber >>= fun result ->
            !+ result

        // Act
        let fiber = runtime.Run effect
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ConcurrentlyAndAwaitFailFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Kanjou Effect"

        let effect =
            !~> !- expected >>= fun fiber ->
            !<~ fiber >>= fun result ->
            !+ result

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.SequenceSuccessErrorFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Sleep Token"

        let effect =
            !+ 42 >>= fun _ ->
            !- expected >>= fun _ ->
            !+ "will not succeed"

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    // TODO: Hmm, are we sure about this behavior?
    [<TestCaseSource("GenerateRuntimes")>]
    member this.SequenceErrorSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = "Bad Omens"

        let effect = 
            !- 42 >>=? fun _ ->
            !+ "will not succeed" >>= fun _ ->
            !- expected

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeDoubleSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let spiritbox = "Spiritbox"
        let imminence = "Imminence"
        let expected = (spiritbox, imminence)
        let effect = !+ spiritbox <*> !+ imminence

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeDoubleFailureFunctionTest(runtime: FIORuntime) =
        // Arrange
        let julieta = "Julieta"
        let groza = "Groza"
        let expected = julieta
        let effect = !- julieta <*> !- groza

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeSuccessFailureFunctionTest(runtime: FIORuntime) =
        // Arrange
        let ambitions = "Ambitions"
        let eyeOfTheStorm = "Eye of the Storm"
        let expected = eyeOfTheStorm
        let effect = !+ ambitions <*> !- eyeOfTheStorm

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeFailureSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let bombsAway = "Bombs Away"
        let takingOff = "Taking Off"
        let expected = bombsAway
        let effect = !- bombsAway <*> !+ takingOff

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeUnitDoubleSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let expected = ()
        let effect = !+ "I won't be there" <!> !+ "and neither will I"

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeUnitDoubleFailureFunctionTest(runtime: FIORuntime) =
        // Arrange
        let lostInTonight = "Lost in Tonight"
        let ghost = "and yet, I will not be there"
        let expected = lostInTonight
        let effect = !- expected <!> !- ghost

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeUnitSuccessFailureFunctionTest(runtime: FIORuntime) =
        // Arrange
        let startAgain = "Start Again"
        let ghost = "I am a ghost, boo"
        let expected = startAgain
        let effect = !+ ghost <!> !- expected

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ParallelizeUnitFailureSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let oneWayTicket = "One Way Ticket"
        let ghost = "Boo, boo..."
        let expected = oneWayTicket
        let effect = !- expected <!> !+ ghost

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ZipDoubleSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let standOutFitIn = "Stand Out Fit In"
        let worstInMe = "Worst In Me"
        let expected = (standOutFitIn, worstInMe)
        let effect = !+ standOutFitIn <^> !+ worstInMe

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ZipDoubleFailureFunctionTest(runtime: FIORuntime) =
        // Arrange
        let itWasntEasy = "It Wasn't Easy"
        let lettingGo = "Letting Go"
        let expected = itWasntEasy
        let effect = !- itWasntEasy <^> !- lettingGo

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ZipSuccessFailureFunctionTest(runtime: FIORuntime) =
        // Arrange
        let theLastTime = "The Last Time"
        let cantWait = "Cant Wait"
        let expected = cantWait
        let effect = !+ theLastTime <^> !- cantWait

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.ZipFailureSuccessFunctionTest(runtime: FIORuntime) =
        // Arrange
        let wastedNights = "Wasted Nights"
        let growOldDieYoung = "Grow Old Die Young"
        let expected = wastedNights
        let effect = !- wastedNights <^> !+ growOldDieYoung

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.RaceLeftSucceedsFunctionTest(runtime: FIORuntime) =
        // Arrange
        let thirtyFiveXXXV = "35xxxv"
        let iAmSoSlow = "Really slow..."
        let expected = thirtyFiveXXXV

        let leftEffect = !+ thirtyFiveXXXV

        let rightEffect =
            fio {
                do! !+ Thread.Sleep(1000)
                return iAmSoSlow
            }

        let effect = leftEffect <%> rightEffect

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.RaceRightSucceedsFunctionTest(runtime: FIORuntime) =
        // Arrange
        let nowIAmSlow = "Now I am slow..."
        let nicheSyndrome = "Niche Syndrome"
        let expected = nicheSyndrome

        let leftEffect =
            fio {
                do! !+ Thread.Sleep(1000)
                return nowIAmSlow
            }

        let rightEffect = !+ nicheSyndrome

        let effect = leftEffect <%> rightEffect

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.True)
        Assert.That(result.IsError, Is.False)
        Assert.That(successResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.RaceLeftFailsFunctionTest(runtime: FIORuntime) =
        // Arrange
        let kanjouEffect = "Kanjou Effect"
        let livingDolls = "Living Dolls"
        let expected = kanjouEffect

        let leftEffect = !- kanjouEffect

        let rightEffect =
            fio {
                do! !+ Thread.Sleep(1000)
                return livingDolls
            }

        let effect = leftEffect <%> rightEffect

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))

    [<TestCaseSource("GenerateRuntimes")>]
    member this.RaceRightFailsFunctionTest(runtime: FIORuntime) =
        // Arrange
        let nope = "Nope"
        let reflection = "Reflection"
        let expected = reflection

        let leftEffect =
            fio {
                do! !+ Thread.Sleep(1000)
                return nope
            }

        let rightEffect = !- reflection

        let effect = leftEffect <%> rightEffect

        // Act
        let fiber = runtime.Run(effect)
        let result = fiber.AwaitResult()

        // Assert
        Assert.That(result.IsOk, Is.False)
        Assert.That(result.IsError, Is.True)
        Assert.That(failureResult(result, expected), Is.EqualTo(expected))
