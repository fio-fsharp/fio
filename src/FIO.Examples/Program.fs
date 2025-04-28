(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

module private FIO.Examples

open FIO.Core
open FIO.Runtime.Advanced
open FIO.Library.Console
open FIO.Library.Network.Sockets
open FIO.Library.Network.WebSockets

open System
open System.Net
open System.Net.Sockets
open System.Globalization
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
    let concurrent = taskA <*> taskB
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
        do! writeln "Hello! What is your name?"
        let! name = readln ()
        do! writeln $"Hello, %s{name}, welcome to FIO! 🪻💜"
    }
    let fiber = Runtime().Run welcome
    task {
        let! result = fiber.AwaitAsync()
        printfn $"%A{result}"
    } |> ignore

type WelcomeApp() =
    inherit FIOApp<unit, obj>()

    override this.effect : FIO<unit, obj> = fio {
        do! writeln "Hello! What is your name?"
        let! name = readln ()
        do! writeln $"Hello, %s{name}, welcome to FIO! 🪻💜"
    }

type EnterNumberApp() =
    inherit FIOApp<string, string>()

    override this.effect = fio {
        do! write "Enter a number: "
        let! input = readln ()
        match Int32.TryParse input with
        | true, number -> return $"You entered the number: %i{number}."
        | false, _ -> return! !- "You entered an invalid number!"
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
    inherit FIOApp<unit, obj>()

    override this.effect = fio {
        for number in 1..10 do
            match number % 2 = 0 with
            | true -> do! writeln $"%i{number} is even!"
            | false -> do! writeln $"%i{number} is odd!"
    }

type GuessNumberApp() =
    inherit FIOApp<int, string>()

    override this.effect = fio {
        let! numberToGuess = !+ Random().Next(1, 100)
        let mutable guess = -1

        while guess <> numberToGuess do
            do! write "Guess a number: "
            let! input = readln ()

            match Int32.TryParse input with
            | true, parsedInput ->
                guess <- parsedInput
                if guess < numberToGuess then
                    do! writeln "Too low! Try again."
                elif guess > numberToGuess then
                    do! writeln "Too high! Try again."
                else
                    do! writeln "Congratulations! You guessed the number!"
            | _ -> do! writeln "Invalid input. Please enter a number."

        return guess
    }

type PingPongApp() =
    inherit FIOApp<unit, obj>()

    let pinger chan1 chan2 =
        "ping" --> chan1 >>= fun ping ->
        writeln $"pinger sent: %s{ping}" >>= fun _ ->
        !--> chan2 >>= fun pong ->
        writeln $"pinger received: %s{pong}" >>= fun _ ->
        !+ ()

    let ponger chan1 chan2 =
        !--> chan1 >>= fun ping ->
        writeln $"ponger received: %s{ping}" >>= fun _ ->
        "pong" --> chan2 >>= fun pong ->
        writeln $"ponger sent: %s{pong}" >>= fun _ ->
        !+ ()

    override this.effect =
        let chan1 = Channel<string>()
        let chan2 = Channel<string>()
        pinger chan1 chan2 <!> ponger chan1 chan2

type PingPongCEApp() =
    inherit FIOApp<unit, obj>()

    let pinger (chan1: Channel<string>) (chan2: Channel<string>) = fio {
        let! ping = chan1 <-- "ping"
        do! writeln $"pinger sent: %s{ping}"
        let! pong = !<-- chan2
        do! writeln $"pinger received: %s{pong}"
    }

    let ponger (chan1: Channel<string>) (chan2: Channel<string>) = fio {
        let! ping = !<-- chan1
        do! writeln $"ponger received: %s{ping}"
        let! pong = chan2 <-- "pong"
        do! writeln $"ponger sent: %s{pong}"
    }

    override this.effect = fio {
        let chan1 = Channel<string>()
        let chan2 = Channel<string>()
        return! pinger chan1 chan2 <!> ponger chan1 chan2
    }

type Message =
    | Ping
    | Pong

type PingPongMatchApp() =
    inherit FIOApp<unit, string>()

    let pinger (chan1: Channel<Message>) (chan2: Channel<Message>) = fio {
        let! ping = chan1 <-- Ping
        do! writeln $"pinger sent: %A{ping}"
        match! !<-- chan2 with
        | Pong -> do! writeln $"pinger received: %A{Pong}"
        | Ping -> return! !- $"pinger received %A{Ping} when %A{Pong} was expected!"  
    }

    let ponger (chan1: Channel<Message>) (chan2: Channel<Message>) = fio {
        match! !<-- chan1 with
        | Ping -> do! writeln $"ponger received: %A{Ping}"
        | Pong -> return! !- $"ponger received %A{Pong} when %A{Ping} was expected!"
        let! sentMsg =
            match Random().Next(0, 2) with
            | 0 -> chan2 <-- Pong
            | _ -> chan2 <-- Ping
        do! writeln $"ponger sent: %A{sentMsg}"
    }

    override this.effect = fio {
        let chan1 = Channel<Message>()
        let chan2 = Channel<Message>()
        return! pinger chan1 chan2 <!> ponger chan1 chan2
    }

type Error =
    | DbError of bool
    | WsError of int
    | GeneralError of string

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
        return! databaseResult <*> webserviceResult
    }

// Release build required to run, will otherwise crash.
type HighlyConcurrentApp() =
    inherit FIOApp<unit, obj>()

    let sender (chan: Channel<int>) id (rand: Random) = fio {
        let! msg = !+ rand.Next(100, 501)
        do! msg --!> chan
        do! writeln $"Sender[%i{id}] sent: %i{msg}"
    }

    let rec receiver (chan: Channel<int>) count (max: int) = fio {
        if count = 0 then
            let! maxFibers = !+ max.ToString("N0", CultureInfo "en-US")
            do! writeln $"Successfully received a message from all %s{maxFibers} fibers!"
        else
            let! msg = !<-- chan
            do! writeln $"Receiver received: %i{msg}"
            return! receiver chan (count - 1) max
    }

    let rec create chan count acc rand = fio {
        if count = 0 then
            return! acc
        else
            let newAcc = sender chan count rand <!> acc
            return! create chan (count - 1) newAcc rand
    }

    override this.effect = fio {
        let fiberCount = 1000000
        let chan = Channel<int>()
        let rand = Random()
        let acc = sender chan fiberCount rand
                  <!> receiver chan fiberCount fiberCount
        return! create chan (fiberCount - 1) acc rand
    }

type SocketApp(ip: string, port: int) =
    inherit FIOApp<unit, exn>()

    let server (ip: string) (port: int) =
    
        let sendAscii (clientSocket: FSocket<int, string>) = fio {
            try
                while true do
                    let! msg = clientSocket.Receive()
                    do! writeln $"Server received: %s{msg}"
                    let! ascii =
                        if msg.Length > 0 then
                            !+ (int <| msg.Chars 0)
                        else
                            !+ -1
                    do! clientSocket.Send ascii
            with exn ->
                do! writeln $"Error sending ascii to client: %A{exn}"
                return! !- exn
        }

        let handleClient (socket: Socket) = fio {
            try
                let! clientSocket = !+ FSocket<int, string>(socket)
                let! endpoint =
                    clientSocket.RemoteEndPoint()
                    >>= fun endPoint ->
                    !+ endPoint.ToString()
                do! writeln $"Client connected from %s{endpoint}"
                do! !!~> sendAscii(clientSocket)
            with exn ->
                do! writeln $"Error handling client: %A{exn}"
                return! !- exn
        }
        
        fio {
            try
                let! listener = 
                    FIO<TcpListener, exn>.FromFunc(fun () -> new TcpListener(IPAddress.Parse ip, port))
                do! FIO<unit, exn>.FromFunc(fun () -> listener.Start())
                do! writeln $"Server listening on %s{ip}:%i{port}..."

                while true do
                    try
                        let! socket = FIO<Socket, exn>.FromFunc(fun () -> listener.AcceptSocket())
                        do! handleClient socket
                    with exn ->
                        do! writeln $"Error accepting client: %A{exn}"
                        return! !- exn
            with exn ->
                do! writeln $"Error starting server: %A{exn}"
                return! !- exn
        }

    let client (ip: string) (port: int) =
    
        let send (socket: FSocket<string, int>) = fio {
            try
                while true do
                    do! write "Enter a message: "
                    let! msg = readln ()
                    do! socket.Send msg
            with exn ->
                do! writeln $"Error in client send: %A{exn}"
                return! !- exn
        }

        let receive (socket: FSocket<string, int>) = fio {
            try
                while true do
                    let! msg = socket.Receive()
                    do! writeln $"Client received: %i{msg}"
            with exn ->
                do! writeln $"Error in client receive: %A{exn}"
                return! !- exn
        }
    
        fio {
            do! writeln $"Connecting to %s{ip}:%i{port}..."
            let! socket =
                FIO<Socket, exn>.FromFunc(fun () -> new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            try
                let! fsocket = FSocket<string, int>.Create(socket, ip, port)
                do! writeln $"Connected to %s{ip}:%i{port}"
                do! send fsocket <!> receive fsocket
            with exn ->
                do! writeln $"Error connecting to server: %A{exn}"
                return! !- exn
        }

    override this.effect = fio {
        do! server ip port <!> client ip port
    }

type WebSocketApp(serverUrl, clientUrl) =
    inherit FIOApp<unit, exn>()

    let server url =

        let sendAscii (clientSocket: FWebSocket<int, string>) = fio {
            try
                let! state = clientSocket.State()
                while state = WebSocketState.Open do
                    let! msg = clientSocket.Receive()
                    do! writeln $"Server received: %s{msg}"
                    let! ascii =
                        if msg.Length > 0 then
                            !+ (int <| msg.Chars 0)
                        else
                            !+ -1
                    do! clientSocket.Send ascii
            with exn ->
                do! writeln $"Error sending ascii to client: %A{exn}"
                return! !- exn
        }

        let handleClient (clientSocket: FWebSocket<int, string>) = fio {
            try
                let! endpoint = 
                    clientSocket.RemoteEndPoint()
                    >>= fun endPoint ->
                    !+ endPoint.ToString()
                do! writeln $"Client connected from %s{endpoint}"
                do! !!~> sendAscii(clientSocket)
            with exn ->
                do! writeln $"Error handling client: %A{exn}"
                return! !- exn
        }
    
        fio {
            try
                let! serverSocket = !+ ServerFWebSocket<int, string>()
                do! serverSocket.Start url
                do! writeln $"Server listening on %s{url}..."

                while true do
                    let! clientSocket = serverSocket.Accept()
                    do! handleClient clientSocket
            with exn ->
                do! writeln $"Error starting server: %A{exn}"
                return! !- exn
        }

    let client url =

        let send (clientSocket: ClientFWebSocket<string, int>) = fio {
            try
                while true do
                    do! write "Enter a message: "
                    let! msg = readln ()
                    do! clientSocket.Send msg
            with exn ->
                do! writeln $"Error in client send: %A{exn}"
                return! !- exn
        }

        let receive (clientSocket: ClientFWebSocket<string, int>) = fio {
            try
                while true do
                    let! msg = clientSocket.Receive()
                    do! writeln $"Client received: %i{msg}"
            with exn ->
                do! writeln $"Error in client receive: %A{exn}"
                return! !- exn
        }

        fio {
            try
                let! clientSocket = !+ ClientFWebSocket<string, int>()
                do! clientSocket.Connect url
                do! send clientSocket <!> receive clientSocket
            with exn ->
                do! writeln $"Error connecting to server: %A{exn}"
                return! !- exn
        }

    override this.effect = fio {
        do! server serverUrl <!> client clientUrl
    }
    
let tt = task {
    let x = 1
    printfn "Started"
    do! System.Threading.Tasks.Task.Delay(100) // Wait for 1 second (1000 milliseconds)
    let z = 2
    printfn "Doing some work..."
    do! System.Threading.Tasks.Task.Delay(1000)
    let y = 3
    printfn "Done!"
    printfn "Finished!"
    return x + z + y
}
let test () =
    let eff = (FIO<int, exn>.ToFiber tt).Bind(_.Await())
    let fiber = Runtime().Run eff
    task {
        let! result = fiber.AwaitAsync()
        printfn $"Success: %A{result}"
        match result with
        | Ok result -> printfn $"Success: %i{result}"
        | Error error -> printfn $"Error: %A{error}"
    } |> ignore
    
    
// SocketApp("127.0.0.1", 5000).Run()
//Console.ReadLine() |> ignore

test()
Console.ReadLine() |> ignore


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
