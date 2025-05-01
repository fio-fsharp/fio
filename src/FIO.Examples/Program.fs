(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private FIO.Examples

open FIO.Core
open FIO.Runtime.Intermediate
open FIO.Lib.IO
open FIO.Lib.Net.Sockets
open FIO.Lib.Net.WebSockets

open System
open System.IO
open System.Globalization

open System.Net
open System.Net.Sockets
open System.Net.WebSockets

let helloWorld1 () =
    let hello = FIO.Succeed "Hello world! 🪻"
    let fiber = Runtime().Run hello
    task {
        let! result = fiber.AwaitAsync()
        match result with
        | Ok result -> printfn $"Success: %s{result}"
        | Error error -> printfn $"Error: %A{error}"
    } |> ignore

let helloWorld2 () : unit =
    let hello: FIO<string, obj> = FIO.Succeed "Hello world! 🪻"
    let fiber: Fiber<string, obj> = Runtime().Run hello
    task {
        let! (result: Result<string, obj>) = fiber.AwaitAsync()
        match result with
        | Ok result -> printfn $"Success: %s{result}"
        | Error error -> printfn $"Error: %A{error}"
    } |> ignore

let helloWorld3 () : unit =
    let hello: FIO<obj, string> = FIO.Fail "Hello world! 🪻"
    let fiber: Fiber<obj, string> = Runtime().Run hello
    task {
        let! (result: Result<obj, string>) = fiber.AwaitAsync()
        match result with
        | Ok result -> printfn $"Success: %A{result}"
        | Error error -> printfn $"Error: %s{error}"
    } |> ignore

let helloWorld4 () =
    let hello = FIO.Succeed "Hello world! 🪻"
    let fiber = Runtime().Run hello
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let helloWorld5 () =
    let hello = !+ "Hello world! 🪻"
    let fiber = Runtime().Run hello
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let helloWorld6 () =
    let hello = !- "Hello world! 🪻"
    let fiber = Runtime().Run hello
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let concurrency1 () =
    let concurrent = (FIO.Succeed 42).Fork().Bind(_.Await())
    let fiber = Runtime().Run concurrent
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let concurrency2 () =
    let concurrent = !~> !+ 42 >>= fun fiber -> !~~> fiber
    let fiber = Runtime().Run concurrent
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let concurrency3 () =
    let taskA = !+ "Task A completed!"
    let taskB = !+ (200, "Task B OK")
    let concurrent = taskA <!> taskB
    let fiber = Runtime().Run concurrent
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let computationExpression1 () =
    let hello : FIO<string, obj> = fio {
        return "Hello world! 🪻"
    }
    let fiber = Runtime().Run hello
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let computationExpression2 () =
    let hello : FIO<obj, string> = fio {
        return! !- "Hello world! 🪻"
    }
    let fiber = Runtime().Run hello
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

let computationExpression3 () =
    let welcome = fio {
        do! FConsole.PrintLine "Hello! What is your name?"
        let! name = FConsole.ReadLine ()
        do! FConsole.PrintLine $"Hello, %s{name}! Welcome to FIO! 🪻💜"
    }
    let fiber = Runtime().Run welcome
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

type WelcomeApp() =
    inherit FIOApp<unit, exn>()

    override this.effect : FIO<unit, exn> = fio {
        do! FConsole.PrintLine "Hello! What is your name?"
        let! name = FConsole.ReadLine ()
        do! FConsole.PrintLine $"Hello, %s{name}! Welcome to FIO! 🪻💜"
    }

type EnterNumberApp() =
    inherit FIOApp<string, exn>()

    override this.effect = fio {
        do! FConsole.Print "Enter a number: "
        let! input = FConsole.ReadLine ()
        match! !<< (fun () -> Int32.TryParse input) with
        | true, number ->
            return $"You entered the number: %i{number}."
        | false, _ ->
            return! !- IOException("You entered an invalid number!")
    }

type TryCatchApp() =
    inherit FIOApp<string, int>()

    override this.effect = fio {
        try
            do! !- 1
            return "Successfully completed!"
        with errorCode ->
            return! !- errorCode
    }

type ForApp() =
    inherit FIOApp<unit, exn>()

    override this.effect = fio {
        for number in 1..10 do
            match! !<< (fun () -> number % 2 = 0) with
            | true -> do! FConsole.PrintLine $"%i{number} is even!"
            | false -> do! FConsole.PrintLine $"%i{number} is odd!"
    }

type GuessNumberApp() =
    inherit FIOApp<int, exn>()

    override this.effect = fio {
        let! numberToGuess = !<< (fun () -> Random().Next(1, 100))
        let mutable guess = -1

        while guess <> numberToGuess do
            do! FConsole.Print "Guess a number: "
            let! input = FConsole.ReadLine ()

            match! !<< (fun () -> Int32.TryParse input) with
            | true, parsedInput ->
                guess <- parsedInput
                if guess < numberToGuess then
                    do! FConsole.PrintLine "Too low! Try again."
                elif guess > numberToGuess then
                    do! FConsole.PrintLine "Too high! Try again."
                else
                    do! FConsole.PrintLine "Congratulations! You guessed the number!"
            | _ ->
                do! FConsole.PrintLine "Invalid input. Please enter a number."

        return guess
    }

type PingPongApp() =
    inherit FIOApp<unit, exn>()

    let pinger chan1 chan2 =
        "ping" --> chan1 >>= fun ping ->
        FConsole.PrintLine $"pinger sent: %s{ping}" >>= fun _ ->
        !--> chan2 >>= fun pong ->
        FConsole.PrintLine $"pinger received: %s{pong}" >>= fun _ ->
        !+ ()

    let ponger chan1 chan2 =
        !--> chan1 >>= fun ping ->
        FConsole.PrintLine $"ponger received: %s{ping}" >>= fun _ ->
        "pong" --> chan2 >>= fun pong ->
        FConsole.PrintLine $"ponger sent: %s{pong}" >>= fun _ ->
        !+ ()

    override this.effect =
        let chan1 = Channel<string>()
        let chan2 = Channel<string>()
        pinger chan1 chan2 <~> ponger chan1 chan2

type PingPongCEApp() =
    inherit FIOApp<unit, exn>()

    let pinger (chan1: Channel<string>) (chan2: Channel<string>) = fio {
        let! ping = chan1 <-- "ping"
        do! FConsole.PrintLine $"pinger sent: %s{ping}"
        let! pong = !<-- chan2
        do! FConsole.PrintLine $"pinger received: %s{pong}"
    }

    let ponger (chan1: Channel<string>) (chan2: Channel<string>) = fio {
        let! ping = !<-- chan1
        do! FConsole.PrintLine $"ponger received: %s{ping}"
        let! pong = chan2 <-- "pong"
        do! FConsole.PrintLine $"ponger sent: %s{pong}"
    }

    override this.effect = fio {
        let chan1 = Channel<string>()
        let chan2 = Channel<string>()
        return! pinger chan1 chan2 <~> ponger chan1 chan2
    }

type Message =
    | Ping
    | Pong

type PingPongMatchApp() =
    inherit FIOApp<unit, string>()

    let pinger (chan1: Channel<Message>) (chan2: Channel<Message>) = fio {
        let! ping = chan1 <-- Ping
        do! FConsole.PrintLine ($"pinger sent: %A{ping}", _.Message)
        match! !<-- chan2 with
        | Pong -> do! FConsole.PrintLine ($"pinger received: %A{Pong}", _.Message)
        | Ping -> return! !- $"pinger received %A{Ping} when %A{Pong} was expected!"  
    }

    let ponger (chan1: Channel<Message>) (chan2: Channel<Message>) = fio {
        match! !<-- chan1 with
        | Ping -> do! FConsole.PrintLine ($"ponger received: %A{Ping}", _.Message)
        | Pong -> return! !- $"ponger received %A{Pong} when %A{Ping} was expected!"
        let! sentMsg =
            match Random().Next(0, 2) with
            | 0 -> chan2 <-- Pong
            | _ -> chan2 <-- Ping
        do! FConsole.PrintLine ($"ponger sent: %A{sentMsg}", _.Message)
    }

    override this.effect = fio {
        let chan1 = Channel<Message>()
        let chan2 = Channel<Message>()
        return! pinger chan1 chan2 <~> ponger chan1 chan2
    }

type Error =
    | DbError of bool
    | WsError of int
    | GeneralError of string

// TODO: Some interesting stuff going on with the types here. Worth to investigate.
type ErrorHandlingApp() =
    inherit FIOApp<string * char, obj>()

    let readFromDatabase : FIO<string, bool> =
        if Random().Next(0, 2) = 0 then !+ "data" else !- false

    let awaitWebservice : FIO<char, int> =
        if Random().Next(0, 2) = 1 then !+ 'S' else !- 404

    let databaseResult : FIO<string, obj> =
        readFromDatabase >>=? fun error -> !- (DbError error)

    let webserviceResult : FIO<char, obj> =
        awaitWebservice >>=? fun error -> !- (WsError error)

    override this.effect =
        databaseResult <^> webserviceResult
        >>=? fun _ -> !+ ("default", 'D')

type AsyncErrorHandlingApp() =
    inherit FIOApp<string * int, Error>()

    let databaseReadTask : Async<string> = async {
        do printfn $"Reading from database..."
        if Random().Next(0, 2) = 0 then
            return "data"
        else 
            raise <| Exception "Database error!"
            return "error data"
    }

    let webserviceAwaitTask : Async<int> = async {
        do printfn $"Awaiting webservice..."
        if Random().Next(0, 2) = 0 then
            return 200
        else 
            raise <| Exception "Webservice error!"
            return 400
    }

    let databaseResult : FIO<string, Error> =
        FIO<string, exn>.AwaitAsync databaseReadTask
        >>=? fun exn -> !- (GeneralError exn.Message)

    let webserviceResult : FIO<int, Error> =
        FIO<int, exn>.AwaitAsync webserviceAwaitTask
        >>=? fun exn -> !- (GeneralError exn.Message)

    override this.effect = fio {
        return! databaseResult <!> webserviceResult
    }

type HighlyConcurrentApp() =
    inherit FIOApp<unit, exn>()

    let sender (chan: Channel<int>) id (rand: Random) = fio {
        let! msg = !+ rand.Next(100, 501)
        do! msg --!> chan
        do! FConsole.PrintLine $"Sender[%i{id}] sent: %i{msg}"
    }

    let rec receiver (chan: Channel<int>) count (max: int) = fio {
        if count = 0 then
            let! maxFibers = !+ max.ToString("N0", CultureInfo "en-US")
            do! FConsole.PrintLine $"Successfully received a message from all %s{maxFibers} fibers!"
        else
            let! msg = !<-- chan
            do! FConsole.PrintLine $"Receiver received: %i{msg}"
            return! receiver chan (count - 1) max
    }

    let rec create chan count acc rand = fio {
        if count = 0 then
            return! acc
        else
            let newAcc = sender chan count rand <~> acc
            return! create chan (count - 1) newAcc rand
    }

    override this.effect = fio {
        let fiberCount = 1000000
        let chan = Channel<int>()
        let rand = Random()
        let acc = sender chan fiberCount rand
                  <~> receiver chan fiberCount fiberCount
        return! create chan (fiberCount - 1) acc rand
    }

type SocketApp(ip: string, port: int) =
    inherit FIOApp<unit, exn>()

    let server (ip: string) (port: int) =
    
        let sendAscii (socket: FSocket<int, string, exn>) = fio {
            while true do
                let! msg = socket.Receive()
                do! FConsole.PrintLine $"Server received: %s{msg}"
                let! ascii =
                    if msg.Length > 0 then
                        !<< (fun () -> msg.Chars 0) >>= fun c ->
                        !+ (int c)
                    else
                        !+ -1
                do! socket.Send ascii
        }

        let handleClient (socket: FSocket<int, string, exn>) = fio {
            let! remoteEndPoint = socket.RemoteEndPoint()
            let! endPoint = !<< (fun () -> remoteEndPoint.ToString())
            do! FConsole.PrintLine $"Client connected from %s{endPoint}"
            do! !!~> sendAscii(socket)
        }
        
        fio {
            let! listener = !<< (fun () ->
                new TcpListener(IPAddress.Parse ip, port))
            do! !<< (fun () -> listener.Start())
            do! FConsole.PrintLine $"Server listening on %s{ip}:%i{port}..."
            while true do
                let! internalSocket = !<< (fun () -> listener.AcceptSocket())
                let! socket = FSocket.Create<int, string, exn> internalSocket
                do! handleClient socket
        }

    let client (ip: string) (port: int) =

        let send (socket: FSocket<string, int, exn>) = fio {
            while true do
                do! FConsole.Print "Enter a message: "
                let! msg = FConsole.ReadLine ()
                do! socket.Send msg
        }

        let receive (socket: FSocket<string, int, exn>) = fio {
            while true do
                let! msg = socket.Receive()
                do! FConsole.PrintLine $"Client received: %i{msg}"
        }
    
        fio {
            do! FConsole.PrintLine $"Connecting to %s{ip}:%i{port}..."
            let! internalSocket = !<< (fun () ->
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            let! socket = FSocket<string, int, exn>.Create(internalSocket, ip, port)
            do! FConsole.PrintLine $"Connected to %s{ip}:%i{port}"
            do! send socket <~> receive socket
        }

    override this.effect = fio {
        do! server ip port <~> client ip port
    }

type WebSocketApp(serverUrl, clientUrl) =
    inherit FIOApp<unit, exn>()

    let server url =

        let sendAscii (socket: FWebSocket<int, string, exn>) = fio {
            let! state = socket.State()
            while state = WebSocketState.Open do
                let! msg = socket.Receive()
                do! FConsole.PrintLine $"Server received: %s{msg}"
                let! ascii =
                    if msg.Length > 0 then
                        !<< (fun () -> msg.Chars 0) >>= fun c ->
                        !+ (int c)
                    else
                        !+ -1
                do! socket.Send ascii
        }

        let handleClient (socket: FWebSocket<int, string, exn>) = fio {
            let! remoteEndPoint = socket.RemoteEndPoint()
            let! endPoint = !<< (fun () -> remoteEndPoint.ToString())
            do! FConsole.PrintLine $"Client connected from %s{endPoint}"
            do! !!~> sendAscii(socket)
        }
    
        fio {
            let! serverSocket = FServerWebSocket.Create<int, string, exn>()
            do! serverSocket.Start url
            do! FConsole.PrintLine $"Server listening on %s{url}..."
            while true do
                let! socket = serverSocket.Accept()
                do! handleClient socket
        }

    let client url =

        let send (clientSocket: FClientWebSocket<string, int, exn>) = fio {
            while true do
                do! FConsole.Print "Enter a message: "
                let! msg = FConsole.ReadLine ()
                do! clientSocket.Send msg
        }

        let receive (clientSocket: FClientWebSocket<string, int, exn>) = fio {
            while true do
                let! msg = clientSocket.Receive()
                do! FConsole.PrintLine $"Client received: %i{msg}"
        }

        fio {
            let! clientSocket = FClientWebSocket.Create<string, int, exn>()
            do! clientSocket.Connect url
            do! send clientSocket <~> receive clientSocket
        }

    override this.effect = fio {
        do! server serverUrl <~> client clientUrl
    }
    
// let tt = task {
//     let x = 1
//     printfn "Started"
//     do! System.Threading.Tasks.Task.Delay(100) // Wait for 1 second (1000 milliseconds)
//     let z = 2
//     printfn "Doing some work..."
//     do! System.Threading.Tasks.Task.Delay(1000)xwww
//     let y = 3
//     printfn "Done!"
//     printfn "Finished!"
//     return x + z + y
// }
//
// // TODO: Create more examples with the FromTask functions.
// let test () =
//     let eff = (FIO<int, exn>.FromGenericTask tt).Bind(fun fiber ->
//         printfn $"task fiber id: %A{fiber.Id}"
//         fiber.Await())
//     let fiber = Runtime().Run eff
//     let t = task {
//         let! result = fiber.AwaitAsync()
//         printfn $"Success: %A{result}"
//         match result with
//         | Ok result -> printfn $"Success: %i{result}"
//         | Error error -> printfn $"Error: %A{error}"
//     }
//     t.Wait()

helloWorld1 ()
Console.ReadLine() |> ignore

helloWorld2 ()
Console.ReadLine() |> ignore

helloWorld3 ()
Console.ReadLine() |> ignore

helloWorld4 ()
Console.ReadLine() |> ignore

helloWorld5 ()
Console.ReadLine() |> ignore

helloWorld6 ()
Console.ReadLine() |> ignore

concurrency1 ()
Console.ReadLine() |> ignore

concurrency2 ()
Console.ReadLine() |> ignore

concurrency3 ()
Console.ReadLine() |> ignore

computationExpression1 ()
Console.ReadLine() |> ignore

computationExpression2 ()
Console.ReadLine() |> ignore

computationExpression3 ()
Console.ReadLine() |> ignore

WelcomeApp().Run()
Console.ReadLine() |> ignore

EnterNumberApp().Run()
Console.ReadLine() |> ignore

TryCatchApp().Run()
Console.ReadLine() |> ignore

ForApp().Run()
Console.ReadLine() |> ignore

GuessNumberApp().Run()
Console.ReadLine() |> ignore

PingPongApp().Run()
Console.ReadLine() |> ignore

PingPongCEApp().Run()
Console.ReadLine() |> ignore

PingPongMatchApp().Run()
Console.ReadLine() |> ignore

// TODO: Some interesting stuff going on with the types here. Worth to investigate.
ErrorHandlingApp().Run()
Console.ReadLine() |> ignore

// TODO: Should be rewritten.
// AsyncErrorHandlingApp().Run()
// Console.ReadLine() |> ignore

HighlyConcurrentApp().Run()
Console.ReadLine() |> ignore

SocketApp("127.0.0.1", 5000).Run()
Console.ReadLine() |> ignore

WebSocketApp("http://localhost:8080/", "ws://localhost:8080/").Run()
Console.ReadLine() |> ignore
