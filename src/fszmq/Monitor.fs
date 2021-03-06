(* ------------------------------------------------------------------------
This file is part of fszmq.

This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
------------------------------------------------------------------------ *)
namespace fszmq

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text

/// Encapsulates data generated by various ZMQ monitoring events
type ZMQEvent =
  { /// Identifies an individual socket-lifetime event
    EventID : uint16
    /// Specifies the endpoint of the monitored socket
    Address : string
    /// Further information (event specific)
    Details : ZMQEventDetails }
/// Provides more-granular information about a ZMQEvent
and ZMQEventDetails =
  /// Raised when the socket has successfully connected to a remote peer; 
  /// Carries the file descriptor of the underlying socket (validity no guaranteed)
  | Connected       of handle   : int
  /// Raised when a connect request on the socket is pending
  | ConnectDelayed
  /// Raised when a connect request failed, and is now being retried; 
  /// Carries the reconnect interval in milliseconds
  | ConnectRetried  of interval : int
  /// Raised when the socket was successfully bound to a network interface; 
  /// Carries the file descriptor of the underlying socket (validity no guaranteed)
  | Listening       of handle   : int
  /// Raise when the socket could not bind to a given interface;
  /// Carries the error generated during the system call
  | BindFailed      of error    : ZMQError
  /// Raised when the socket has accepted a connection from a remote peer; 
  /// Carries the file descriptor of the underlying socket (validity no guaranteed)
  | Accepted        of handle   : int
  /// Raise when the socket has rejected a connection from a remote peer;
  /// Carries the error generated during the system call
  | AcceptFailed    of error    : ZMQError
  /// Raised when the socket was closed; 
  /// Carries the file descriptor of the underlying socket (validity no guaranteed)
  | Closed          of handle   : int
  /// Raise when the socket failed to close;
  /// Carries the error generated during the system call
  | CloseFailed     of error    : ZMQError
  /// Raised when the socket was disconnected unexpectedly; 
  /// Carries the file descriptor of the underlying socket (validity no guaranteed)
  | Disconnected    of handle   : int
  /// Monitoring on the given socket ended
  | MonitorStopped
  /// Received unknown (likely garbage) event data. Recheck your code.
  | Unknown

/// Contains methods for working with Socket diagnostics
[<Extension>]
module Monitor =

    // Constructs a ZMQEventDetails instance based on a (native) ZeroMQ event and associated data
    let private buildEventDetails (event,data) =
      match event with
      // data is a reconnect interval
      | ZMQ.EVENT_CONNECT_RETRIED -> ConnectRetried data
      // data is a socket handle (a.k.a. file descriptor, or fd)
      | ZMQ.EVENT_LISTENING       -> Listening    data
      | ZMQ.EVENT_CONNECTED       -> Connected    data
      | ZMQ.EVENT_ACCEPTED        -> Accepted     data
      | ZMQ.EVENT_CLOSED          -> Closed       data
      | ZMQ.EVENT_DISCONNECTED    -> Disconnected data
      // data is a ZeroMQ error number (for use with ZMQError)
      | ZMQ.EVENT_BIND_FAILED     -> BindFailed   (ZMQ.buildError data)
      | ZMQ.EVENT_ACCEPT_FAILED   -> AcceptFailed (ZMQ.buildError data)
      | ZMQ.EVENT_CLOSE_FAILED    -> CloseFailed  (ZMQ.buildError data)
      // data is meaningless
      | ZMQ.EVENT_CONNECT_DELAYED -> ConnectDelayed
      | ZMQ.EVENT_MONITOR_STOPPED -> MonitorStopped
      | _                         -> Unknown

    /// Constructs a ZMQEvent option from a raw (binary) message
    [<CompiledName("TryBuildEvent")>]
    let tryBuildEvent message =
      match message with
      | [| details; address; |] ->
        match Array.length details with
        | n when n = ZMQ.EVENT_DETAIL_SIZE ->
            let event = BitConverter.ToUInt16(details,0)
            let value = BitConverter.ToInt32 (details,sizeof<uint16>)
            { EventID = event;
              Address = Encoding.UTF8.GetString(address);
              Details = buildEventDetails (event,value) }
            |> Some
        | _ -> None
      | _   -> None

    /// Constructs a ZMQEvent from a raw (binary) message;
    /// will raise exception if message format is incorrect
    [<CompiledName("BuildEvent")>]
    let buildEvent message =
      match tryBuildEvent message with
      | Some zmqEvent -> zmqEvent
      | None          -> ZMQ.einval "Message does not contain ZMQEvent data"

    /// Tries to receive the next ZMQEvent from a monitor socket
    [<Extension;CompiledName("TryRecvEvent")>]
    let tryRecvEvent socket =
      socket
      |> Socket.recvAll
      |> tryBuildEvent

    /// Receives the next ZMQEvent from a monitor socket;
    /// will raise exception if called on a non-monitor socket
    [<Extension;CompiledName("RecvEvent")>]
    let recvEvent socket =
      match tryRecvEvent socket with
      | Some zmqEvent -> zmqEvent
      | None          -> ZMQ.einval "Endpoint did not provide ZMQEvent data"
