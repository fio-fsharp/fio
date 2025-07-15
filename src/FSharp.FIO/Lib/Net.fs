(*********************************************************************************************)
(* FIO - A Type-Safe, Purely Functional Effect System for Asynchronous and Concurrent F#     *)
(* Copyright (c) 2022-2025 - Daniel "iyyel" Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                                       *)
(*********************************************************************************************)

namespace FSharp.FIO.Lib.Net

open FSharp.FIO.DSL

open System
open System.IO
open System.Net
open System.Text
open System.Text.Json
open System.Threading
open System.Net.Sockets
open System.Net.WebSockets
open System.Threading.Tasks

module Sockets =

    /// Functional Socket
    type FSocket<'S, 'R, 'E> private (socket: Socket, reader: StreamReader, writer: StreamWriter, onError: exn -> 'E, options: JsonSerializerOptions) =

        // Partially applied function as it is the same
        // onError function used everywhere in the type
        let ( !<<< ) (func: unit -> 'T) : FIO<'T, 'E> =
            !<<< func onError
    
        static member Create<'S, 'R, 'E> (socket: Socket, onError: exn -> 'E, options: JsonSerializerOptions) : FIO<FSocket<'S, 'R, 'E>, 'E> =
            fio {
                let! networkStream = !<<< (fun () -> new NetworkStream (socket)) onError
                let! writer = !<<< (fun () -> new StreamWriter (networkStream)) onError
                let! reader = !<<< (fun () -> new StreamReader (networkStream)) onError
                do! !<<< (fun () -> writer.AutoFlush <- true) onError
                return FSocket (socket, reader, writer, onError, options)
            }

        static member Create<'S, 'R, 'E> (socket: Socket, options: JsonSerializerOptions) : FIO<FSocket<'S, 'R, exn>, exn> =
            fio {
                return! FSocket.Create<'S, 'R, exn> (socket, id, options)
            }

        static member Create<'S, 'R, 'E> (socket: Socket, onError: exn -> 'E) : FIO<FSocket<'S, 'R, 'E>, 'E> =
            fio {
                return! FSocket.Create<'S, 'R, 'E> (socket, onError, JsonSerializerOptions())
            }

        static member Create<'S, 'R, 'E> (socket: Socket) : FIO<FSocket<'S, 'R, exn>, exn> =
            fio {
                return! FSocket.Create<'S, 'R, exn> (socket, id, JsonSerializerOptions())
            }

        static member Create<'S, 'R, 'E> (socket: Socket, host: string, port: int, onError: exn -> 'E, options: JsonSerializerOptions) : FIO<FSocket<'S, 'R, 'E>, 'E> =
            fio {
                do! !<<< (fun () -> socket.Connect(host, port)) onError
                return! FSocket.Create<'S, 'R, 'E> (socket, onError, options)
            }
        
        static member Create<'S, 'R, 'E> (socket: Socket, host: string, port: int, options: JsonSerializerOptions) : FIO<FSocket<'S, 'R, exn>, exn> =
            fio {
                do! !<<< (fun () -> socket.Connect(host, port)) id
                return! FSocket.Create<'S, 'R, exn> (socket, id, options)
            }
        
        static member Create<'S, 'R, 'E> (socket: Socket, host: string, port: int, onError: exn -> 'E) : FIO<FSocket<'S, 'R, 'E>, 'E> =
            fio {
                return! FSocket.Create<'S, 'R, 'E> (socket, host, port, onError, JsonSerializerOptions())
            }
        
        static member Create<'S, 'R, 'E> (socket: Socket, host: string, port: int) : FIO<FSocket<'S, 'R, exn>, exn> =
            fio {
                return! FSocket.Create<'S, 'R, exn> (socket, host, port, id, JsonSerializerOptions())
            }

        member _.Send<'S, 'E> (msg: 'S) : FIO<unit, 'E> =
            fio {
                let! json = !<<< (fun () -> JsonSerializer.Serialize(msg, options))
                do! !<<< (fun () -> writer.WriteLine json)
                do! !<<< writer.Flush
            }

        member _.Receive<'R, 'E> () : FIO<'R, 'E> =
            fio {
                let! json = !<<< reader.ReadLine
                let! msg = !<<< (fun () -> JsonSerializer.Deserialize<'R>(json, options))
                return msg
            }
        
        member _.Disconnect<'E> (reuseSocket: bool) : FIO<unit, 'E> =
            fio {
                do! !<<< (fun () -> socket.Disconnect reuseSocket)
            }

        member _.Close<'E> () : FIO<unit, 'E> =
            fio {
                do! !<<< (fun () -> socket.Close())
            }
        
        member _.RemoteEndPoint<'E> () : FIO<EndPoint, 'E> =
            fio {
                return! !<<< (fun () -> socket.RemoteEndPoint)
            }
        
        member _.AddressFamily : FIO<AddressFamily, 'E> =
            fio {
                return! !<<< (fun () -> socket.AddressFamily)
            }

module WebSockets =

    /// Functional WebSocket
    type FWebSocket<'S, 'R, 'E> private (ctx: HttpListenerWebSocketContext, listenerCtx: HttpListenerContext, onError: exn -> 'E, options: JsonSerializerOptions) =

        // Partially applied functions as it is the same
        // onError function used everywhere in the type
        let ( !<<< ) (func: unit -> 'T) : FIO<'T, 'E> =
            !<<< func onError

        let ( !<<~ ) (task: Task) : FIO<unit, 'E> =
            !<<~ task onError

        let ( !<<~~ ) (task: Task<'T>) : FIO<'T, 'E> =
            !<<~~ task onError

        static member Create<'S, 'R, 'E> (ctx, listenerCtx, onError: exn -> 'E, options) : FIO<FWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                return FWebSocket (ctx, listenerCtx, onError, options)
            }
        
        static member Create<'S, 'R, 'E> (ctx, listenerCtx, options) : FIO<FWebSocket<'S, 'R, exn>, exn> =
            fio {
                return! FWebSocket.Create<'S, 'R, exn> (ctx, listenerCtx, id, options)
            }
        
        static member Create<'S, 'R, 'E> (ctx, listenerCtx, onError: exn -> 'E) : FIO<FWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                return! FWebSocket.Create<'S, 'R, 'E> (ctx, listenerCtx, onError, JsonSerializerOptions())
            }
        
        static member Create<'S, 'R, 'E> (ctx, listenerCtx) : FIO<FWebSocket<'S, 'R, exn>, exn> =
            fio {
            return! FWebSocket.Create<'S, 'R, exn> (ctx, listenerCtx, id, JsonSerializerOptions())
            }
        
        member _.Send<'S, 'E> (msg: 'S) : FIO<unit, 'E> =
            fio {
                let! json = !<<< (fun () -> JsonSerializer.Serialize(msg, options))
                let! buffer = !<<< (fun () -> Encoding.UTF8.GetBytes json)
                let! task = !<<< (fun () ->
                    ctx.WebSocket.SendAsync(
                        ArraySegment<byte> buffer,
                        WebSocketMessageType.Text,
                        true, CancellationToken.None))
                do! !<<~ task
            }

        member _.Receive<'R, 'E> () : FIO<'R, 'E> =
            fio {
                let! buffer = !<<< (fun () -> Array.zeroCreate 1024)
                let! receiveTask = !<<< (fun () ->
                    ctx.WebSocket.ReceiveAsync(
                        ArraySegment<byte> buffer,
                        CancellationToken.None))
                let! receiveResult = !<<~~ receiveTask
                
                if receiveResult.MessageType = WebSocketMessageType.Close then
                    let! closeTask = !<<< (fun () ->
                        ctx.WebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None))
                    do! !<<~ closeTask
                    return! !- (onError <| Exception("Received Close message"))
                else
                    let! json = !<<< (fun () -> Encoding.UTF8.GetString(buffer, 0, receiveResult.Count))
                    let! msg = !<<< (fun () -> JsonSerializer.Deserialize<'R>(json, options))
                    return msg
            }

        member _.Close<'E> () : FIO<unit, 'E> =
            fio {
                let! closeTask = !<<< (fun () ->
                    ctx.WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing", CancellationToken.None))
                do! !<<~ closeTask
            }

        member _.RemoteEndPoint<'E> () : FIO<IPEndPoint, 'E> =
            fio {
                return! !<<< (fun () -> listenerCtx.Request.RemoteEndPoint)
            }

        member _.LocalEndPoint<'E> () : FIO<IPEndPoint, 'E> =
            fio {
                return! !<<< (fun () -> listenerCtx.Request.LocalEndPoint)
            }

        member _.State<'E> () : FIO<WebSocketState, 'E> =
            fio {
                return! !<<< (fun () -> ctx.WebSocket.State)
            }

    /// Functional Server WebSocket
    type FServerWebSocket<'S, 'R, 'E> private (listener: HttpListener, onError: exn -> 'E, options: JsonSerializerOptions) =

        // Partially applied functions as it is the same
        // onError function used everywhere in the type
        let ( !<<< ) (func: unit -> 'T) : FIO<'T, 'E> =
            !<<< func onError

        let ( !<<~ ) (task: Task) : FIO<unit, 'E> =
            !<<~ task onError

        let ( !<<~~ ) (task: Task<'T>) : FIO<'T, 'E> =
            !<<~~ task onError

        static member Create<'S, 'R, 'E> (onError: exn -> 'E, options: JsonSerializerOptions) : FIO<FServerWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                let! listener = !<<< (fun () -> new HttpListener()) onError
                return FServerWebSocket (listener, onError, options)
            }
        
        static member Create<'S, 'R, 'E> (options: JsonSerializerOptions) : FIO<FServerWebSocket<'S, 'R, exn>, exn> =
            fio {
                return! FServerWebSocket.Create<'S, 'R, exn> (id, options)
            }
        
        static member Create<'S, 'R, 'E> (onError: exn -> 'E) : FIO<FServerWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                return! FServerWebSocket.Create<'S, 'R, 'E> (onError, JsonSerializerOptions())
            }

        static member Create<'S, 'R, 'E> () : FIO<FServerWebSocket<'S, 'R, exn>, exn> =
            fio {
                return! FServerWebSocket.Create<'S, 'R, exn> (id, JsonSerializerOptions())
            }

        member _.Start<'E> url : FIO<unit, 'E> =
            fio {
                do! !<<< (fun () -> listener.Prefixes.Add url)
                do! !<<< (fun () -> listener.Start())
            }

        member _.Accept<'S, 'R, 'E> () : FIO<FWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                let! listenerCtxTask = !<<< (fun () -> listener.GetContextAsync())
                let! listenerCtx = !<<~~ listenerCtxTask
                
                if listenerCtx.Request.IsWebSocketRequest then
                    let! ctxTask = !<<< (fun () ->
                        listenerCtx.AcceptWebSocketAsync(
                            subProtocol = null))
                    let! ctx = !<<~~ ctxTask
                    return! FWebSocket.Create<'S, 'R, 'E> (ctx, listenerCtx, onError, options)
                else
                    do! !<<< (fun () -> listenerCtx.Response.StatusCode <- 400)
                    do! !<<< (fun () -> listenerCtx.Response.Close())
                    let! error = !- (onError <| Exception("Not a WebSocket request"))
                    return error
            }

        member _.Close<'E> () : FIO<unit, 'E> =
            fio {
                do! !<<< (fun () -> listener.Stop())
            }

    /// Functional Client WebSocket
    type FClientWebSocket<'S, 'R, 'E> private (clientWebSocket: ClientWebSocket, onError: exn -> 'E, options: JsonSerializerOptions) =

        // Partially applied functions as it is the same
        // onError function used everywhere in the type
        let ( !<<< ) (func: unit -> 'T) : FIO<'T, 'E> =
            !<<< func onError

        let ( !<<~ ) (task: Task) : FIO<unit, 'E>=
            !<<~ task onError

        let ( !<<~~ ) (task: Task<'T>) : FIO<'T, 'E> =
            !<<~~ task onError

        static member Create<'S, 'R, 'E> (onError: exn -> 'E, options: JsonSerializerOptions) : FIO<FClientWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                let! clientWebSocket = !<<< (fun () -> new ClientWebSocket()) onError
                return FClientWebSocket (clientWebSocket, onError, options)
            }
        
        static member Create<'S, 'R, 'E> (options: JsonSerializerOptions) : FIO<FClientWebSocket<'S, 'R, exn>, exn> =
            fio {
                return! FClientWebSocket.Create<'S, 'R, exn> (id, options)
            }
        
        static member Create<'S, 'R, 'E> (onError: exn -> 'E) : FIO<FClientWebSocket<'S, 'R, 'E>, 'E> =
            fio {
                return! FClientWebSocket.Create<'S, 'R, 'E> (onError, JsonSerializerOptions())
            }
        
        static member Create<'S, 'R, 'E> () : FIO<FClientWebSocket<'S, 'R, exn>, exn> =
            fio {
                return! FClientWebSocket.Create<'S, 'R, exn> (id, JsonSerializerOptions())
            }
        
        member _.Connect<'E> url : FIO<unit, 'E> =
            fio {
                let! uri = !<<< (fun () -> Uri url)
                let! connectTask = !<<< (fun () ->
                    clientWebSocket.ConnectAsync(
                        uri, CancellationToken.None))
                do! !<<~ connectTask
            }
        
        member _.Send<'S, 'E> (msg: 'S) : FIO<unit, 'E> =
            fio {
                let! json = !<<< (fun () -> JsonSerializer.Serialize(msg, options))
                let! buffer = !<<< (fun () -> Encoding.UTF8.GetBytes json)
                let! sendTask = !<<< (fun () ->
                    clientWebSocket.SendAsync(
                        ArraySegment buffer,
                        WebSocketMessageType.Text,
                        true, CancellationToken.None))
                do! !<<~ sendTask
            }

        member _.Receive<'R, 'E> () : FIO<'R, 'E> =
            fio {
                let! buffer = !<<< (fun () -> Array.zeroCreate 1024)
                let! receiveTask = !<<< (fun () ->
                    clientWebSocket.ReceiveAsync(
                        ArraySegment buffer,
                        CancellationToken.None))
                let! receiveResult = !<<~~ receiveTask
                let! json = !<<< (fun () -> Encoding.UTF8.GetString(buffer, 0, receiveResult.Count))
                let! msg = !<<< (fun () -> JsonSerializer.Deserialize<'R>(json, options))
                return msg
            }

        member _.Close<'E> () : FIO<unit, 'E> =
            fio {
                let! closeTask = !<<< (fun () ->
                    clientWebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing", CancellationToken.None))
                do! !<<~ closeTask
            }
