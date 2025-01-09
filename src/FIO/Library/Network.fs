(************************************************************************************)
(* FIO - A type-safe, highly concurrent programming library for F#                  *)
(* Copyright (c) 2022-2025, Daniel Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                              *)
(************************************************************************************)

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
open System.Text.Json.Serialization

open FIO.Core

module Sockets =

    type Socket<'R>(socket: Socket) =
        let networkStream = new NetworkStream(socket)
        let reader = new StreamReader(networkStream)
        let writer = new StreamWriter(networkStream)

        do writer.AutoFlush <- true

        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()

        member this.Send(message: 'R) : FIO<unit, exn> =
            try
                let serialized = JsonSerializer.Serialize(message, options)
                writer.WriteLine(serialized)
                writer.Flush()
                !+ ()
            with exn ->
                !- exn

        member this.Receive() : FIO<'R, exn> =
            try 
                let line = reader.ReadLine()
                let message = JsonSerializer.Deserialize<'R>(line, options)
                !+ message
            with exn ->
                !- exn

        member this.RemoteEndPoint() : FIO<EndPoint, exn> =
            try
                !+ socket.RemoteEndPoint
            with exn ->
                !- exn

        member this.Disconnect(reuseSocket: bool) : FIO<unit, exn> =
            try
                socket.Disconnect(reuseSocket)
                !+ ()
            with exn ->
                !- exn

        member this.AddressFamily : FIO<AddressFamily, exn> =
            !+ socket.AddressFamily

        member this.Close() : FIO<unit, exn> =
            !+ socket.Close()

module WebSockets =

    type WebSocket<'R> internal (webSocketContext: HttpListenerWebSocketContext, listenerContext: HttpListenerContext) =
        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()

        member this.Send(message: 'R) : FIO<unit, exn> =
            try
                let serialized = JsonSerializer.Serialize(message, options)
                let buffer = Encoding.UTF8.GetBytes(serialized)
                webSocketContext.WebSocket.SendAsync(ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None).Wait()
                !+ ()
            with exn ->
                !- exn

            member this.Receive() : FIO<'R, exn> =
                try
                    let buffer = Array.zeroCreate 1024
                    let result = webSocketContext.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result

                    if result.MessageType = WebSocketMessageType.Close then
                        webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait()
                        !- (new Exception("Received Close message"))
                    else 
                        let serialized = Encoding.UTF8.GetString(buffer, 0, result.Count)
                        let received = JsonSerializer.Deserialize<'R>(serialized, options)
                        !+ received
                with exn ->
                    try
                        webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, exn.Message, CancellationToken.None).Wait()
                    with _ -> 
                        ()
                    !- exn

            member this.Close() : FIO<unit, exn> =
                try
                    webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait()
                    !+ ()
                with exn ->
                    !- exn

            member this.State : WebSocketState =
                webSocketContext.WebSocket.State

            member this.RemoteEndPoint() : FIO<EndPoint, exn> =
                try
                    !+ listenerContext.Request.RemoteEndPoint
                with exn ->
                    !- exn

            member this.LocalEndPoint() : FIO<EndPoint, exn> =
                try
                    !+ listenerContext.Request.LocalEndPoint
                with exn ->
                    !- exn

    type ServerWebSocket<'R>() =
        let listener = new HttpListener()
        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()

        member this.Start(url) : FIO<unit, exn> =
            try
                listener.Prefixes.Add(url)
                listener.Start()
                !+ ()
            with exn ->
                !- exn

        member this.Accept() : FIO<WebSocket<'R>, exn> =
            try
                let context = listener.GetContextAsync().Result
                if context.Request.IsWebSocketRequest then
                    let webSocketContext = context.AcceptWebSocketAsync(subProtocol = null).Result
                    !+ WebSocket<'R>(webSocketContext, context)
                else
                    context.Response.StatusCode <- 400
                    context.Response.Close()
                    !- (new Exception("Not a WebSocket request"))
            with exn ->
                !- exn

        member this.Close() : FIO<unit, exn> =
            try
                listener.Stop()
                !+ ()
            with exn ->
                !- exn

    and ClientWebSocket<'R>() =
        let clientSocket = new ClientWebSocket()
        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()

        member this.Connect(url) : FIO<unit, exn> =
            try
                let uri = new Uri(url)
                clientSocket.ConnectAsync(uri, CancellationToken.None)
                |> Async.AwaitTask
                |> Async.RunSynchronously
                !+ ()
            with exn ->
                !- exn

        member this.Send(message: 'R) : FIO<unit, exn> =
            try
                let serialized = JsonSerializer.Serialize(message, options)
                let buffer = Encoding.UTF8.GetBytes(serialized)
                clientSocket.SendAsync(ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None).Wait()
                !+ ()
            with exn ->
                !- exn

        member this.Receive() : FIO<'R, exn> = 
            try
                let buffer = Array.zeroCreate<byte> 1024
                let result = clientSocket.ReceiveAsync(ArraySegment<byte>(buffer), CancellationToken.None).Result
                let serialized = Encoding.UTF8.GetString(buffer, 0, result.Count)
                let received = JsonSerializer.Deserialize<'R>(serialized, options) // crashes for strings
                !+ received
            with exn ->
                !- exn

        member this.Close() : FIO<unit, exn> =
            try
                clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait()
                !+ ()
            with exn ->
                !- exn

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
