(*************************************************************************************************************)
(* FIO - A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU)                          *)
(* All rights reserved                                                                                       *)
(*************************************************************************************************************)

namespace FIO.Library.Network

open System
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Net.WebSockets
open System.Threading
open System.Text.Json

open FIO.Core

module Sockets =

    type FSocket<'S, 'R>(socket: Socket, options: JsonSerializerOptions) =
        let networkStream = new NetworkStream(socket)
        let reader = new StreamReader(networkStream)
        let writer = new StreamWriter(networkStream)

        do 
            writer.AutoFlush <- true

        new(socket) = FSocket(socket, new JsonSerializerOptions())

        member this.Send (msg: 'S) : FIO<unit, exn> =
            try
                let serialized = JsonSerializer.Serialize(msg, options)
                writer.WriteLine serialized
                writer.Flush ()
                !+ ()
            with exn ->
                !- exn

        member this.Receive () : FIO<'R, exn> =
            try 
                let line = reader.ReadLine ()
                !+ JsonSerializer.Deserialize<'R>(line, options)
            with exn ->
                !- exn

        member this.RemoteEndPoint () : FIO<EndPoint, exn> =
            try
                !+ socket.RemoteEndPoint
            with exn ->
                !- exn

        member this.Disconnect (reuseSocket: bool) : FIO<unit, exn> =
            try
                socket.Disconnect reuseSocket
                !+ ()
            with exn ->
                !- exn

        member this.AddressFamily : FIO<AddressFamily, exn> =
            !+ socket.AddressFamily

        member this.Close () : FIO<unit, exn> =
            !+ socket.Close()

module WebSockets =

    type FWebSocket<'S, 'R> internal (webSocketContext: HttpListenerWebSocketContext, listenerContext: HttpListenerContext, options: JsonSerializerOptions) =

        new(webSocketContext, listenerContext) = FWebSocket(webSocketContext, listenerContext, new JsonSerializerOptions())

        member this.Send (msg: 'S) : FIO<unit, exn> = fio {
            try
                let! serialized = !+ JsonSerializer.Serialize(msg, options)
                let! buffer = !+ Encoding.UTF8.GetBytes(serialized)
                do! FIO<unit, exn>.FromTask(webSocketContext.WebSocket.SendAsync(ArraySegment<byte> buffer, WebSocketMessageType.Text, true, CancellationToken.None), "SendAsync, Spawned Socket")
                return ()
            with exn ->
                return! !- exn
        }

        member this.Receive () : FIO<'R, exn> = fio {
            try
                let! buffer = !+ Array.zeroCreate(1024)
                let! result =
                    FIO<WebSocketReceiveResult, exn>.FromGenericTask(webSocketContext.WebSocket.ReceiveAsync(ArraySegment<byte> buffer, CancellationToken.None), "ReceiveAsync, Spawned Socket")
                if result.MessageType = WebSocketMessageType.Close then
                    do! FIO<unit, exn>.FromTask(webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None), "CloseAsync, Spawned Socket")
                    return! !- Exception("Received Close message")
                else 
                    let! serialized = !+ Encoding.UTF8.GetString(buffer, 0, result.Count)
                    return JsonSerializer.Deserialize<'R>(serialized, options)
            with exn ->
                try
                    do! FIO<unit, exn>.FromTask(webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, exn.Message, CancellationToken.None), "CloseAsync, Spawned Socket")
                    return ()
                with _ -> 
                    return ()
                return! !- exn
        }

        member this.Close () : FIO<unit, exn> = fio {
            try
                do! FIO<unit, exn>.FromTask(webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None), "CloseAsync, Spawned Socket")
                return ()
            with exn ->
                return! !- exn
        }

        member this.State () : FIO<WebSocketState, exn> = fio {
            return webSocketContext.WebSocket.State
        }

        member this.RemoteEndPoint () : FIO<EndPoint, exn> = fio {
            try
                return listenerContext.Request.RemoteEndPoint
            with exn ->
                return! !- exn
        }

        member this.LocalEndPoint () : FIO<EndPoint, exn> = fio {
            try
                return listenerContext.Request.LocalEndPoint
            with exn ->
                return! !- exn
        }

    type ServerFWebSocket<'S, 'R>(options: JsonSerializerOptions) =
        let listener = new HttpListener()

        new() = ServerFWebSocket(new JsonSerializerOptions())

        member this.Start url : FIO<unit, exn> = fio {
            try
                do! !+ (listener.Prefixes.Add url)
                do! !+ listener.Start()
                return ()
            with exn ->
                return! !- exn
        }

        member this.Accept () : FIO<FWebSocket<'S, 'R>, exn> = fio {
            try
                let! context =
                    FIO<HttpListenerContext, exn>.FromGenericTask(listener.GetContextAsync(), "GetContextAsync, ServerSocket")
                if context.Request.IsWebSocketRequest then
                    let! webSocketContext =
                        FIO<HttpListenerWebSocketContext, exn>.FromGenericTask(context.AcceptWebSocketAsync(subProtocol = null), "AcceptWebSocketAsync, ServerSocket")
                    return FWebSocket<'S, 'R>(webSocketContext, context, options)
                else
                    do! !+ (context.Response.StatusCode <- 400)
                    do! !+ context.Response.Close()
                    return! !- Exception("Not a WebSocket request")
            with exn ->
                return! !- exn
        }

        member this.Close() : FIO<unit, exn> = fio {
            try
                do! !+ listener.Stop()
                return ()
            with exn ->
                return! !- exn
        }

    and ClientFWebSocket<'S, 'R>(options: JsonSerializerOptions) =
        let clientSocket = new ClientWebSocket()

        new() = ClientFWebSocket(new JsonSerializerOptions())

        member this.Connect url : FIO<unit, exn> = fio {
            try
                let! uri = !+ (Uri url)
                do! FIO<unit, exn>.FromTask(clientSocket.ConnectAsync(uri, CancellationToken.None), "ConnectAsync, ClientSocket")
                return ()
            with exn ->
                return! !- exn
        }

        member this.Send (msg: 'S) : FIO<unit, exn> = fio {
            try
                let! serialized = !+ JsonSerializer.Serialize(msg, options)
                let! buffer = !+ Encoding.UTF8.GetBytes(serialized)
                do! FIO<unit, exn>.FromTask(clientSocket.SendAsync(ArraySegment buffer, WebSocketMessageType.Text, true, CancellationToken.None), "SendAsync, ClientSocket")
                return ()
            with exn ->
                return! !- exn
        }

        member this.Receive () : FIO<'R, exn> = fio {
            try
                let! buffer = !+ Array.zeroCreate(1024)
                let! result = 
                    FIO<WebSocketReceiveResult, exn>.FromGenericTask(clientSocket.ReceiveAsync(ArraySegment buffer, CancellationToken.None), "ReceiveAsync, ClientSocket")
                let! serialized = !+ Encoding.UTF8.GetString(buffer, 0, result.Count)
                return JsonSerializer.Deserialize<'R>(serialized, options)
            with exn ->
                return! !- exn
        }

        member this.Close () : FIO<unit, exn> = fio {
            try
                do! FIO<unit, exn>.FromTask(clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None), "CloseAsync, ClientSocket")
                return ()
            with exn ->
                return! !- exn
        }

module Http =
    
        type HttpClient() =
            let client = new Http.HttpClient()
    
            member this.Get(url: string) : FIO<string, exn> =
                try
                    let response = client.GetAsync(url).Result
                    let result = response.Content.ReadAsStringAsync().Result
                    !+ result
                with exn ->
                    !- exn  
    
            member this.Post(url: string, message: string) : FIO<string, exn> =
                try
                    let response = client.PostAsync(url, new StringContent(message)).Result
                    let result = response.Content.ReadAsStringAsync().Result
                    !+ result
                with exn ->
                    !- exn
    
            member this.Put(url: string, message: string) : FIO<string, exn> =
                try
                    let stringContent = new StringContent(message)
                    let response = client.PutAsync(url, stringContent).Result
                    let result = response.Content.ReadAsStringAsync().Result
                    !+ result
                with exn ->
                    !- exn

            member this.Delete(url: string) : FIO<string, exn> =
                try
                    let response = client.DeleteAsync(url).Result
                    let result = response.Content.ReadAsStringAsync().Result
                    !+ result
                with exn ->
                    !- exn
