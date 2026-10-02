/*
 ISC License

 Copyright (c) 2025, Autonomous Vehicle Systems Lab, University of Colorado at Boulder

 Permission to use, copy, modify, and/or distribute this software for any
 purpose with or without fee is hereby granted, provided that the above
 copyright notice and this permission notice appear in all copies.

 THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
 WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
 MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
 ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
 WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
 ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
 OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

 */

using System;

/// <summary>
/// Kind of Basilisk payload carried by a <see cref="VizPayload"/>.
/// <remarks>The values are the tag bytes used on the WebSocket wire and must
/// stay in step with the Python bridge (bridge/bsk_viz_bridge.py).</remarks>
/// </summary>
public enum VizPayloadType : byte
{
    /// <summary>A serialized VizMessage protobuf (Basilisk SIM_UPDATE).</summary>
    SimUpdate = 0x01,

    /// <summary>A serialized VizBroadcastSyncSettings protobuf (Basilisk SYNC_SETTINGS).</summary>
    SyncSettings = 0x02,
}

/// <summary>
/// One decoded Basilisk payload, independent of how it arrived.
/// </summary>
public readonly struct VizPayload
{
    public readonly VizPayloadType Type;
    public readonly byte[] Data;

    public VizPayload(VizPayloadType type, byte[] data)
    {
        Type = type;
        Data = data;
    }
}

/// <summary>Connection state of an <see cref="IVizTransport"/>.</summary>
public enum VizTransportState
{
    Idle,
    Connecting,
    Connected,
    Closed,
    Failed,
}

/// <summary>
/// Abstracts how Vizard receives Basilisk messages, so the scene code does not
/// depend on a particular network stack.
/// <remarks>
/// Two implementations exist. On desktop, NetMQ talks ZMQ directly to Basilisk.
/// In the browser neither ZMQ nor listening sockets are available, so
/// <see cref="VizWebSocketTransport"/> connects out to the Python bridge
/// instead. Implementations deliver payloads through <see cref="TryDequeue"/>
/// rather than a callback, so that decoding always happens on Unity's main
/// thread regardless of where the bytes were received.
/// </remarks>
/// </summary>
public interface IVizTransport : IDisposable
{
    VizTransportState State { get; }

    /// <summary>Human readable reason the transport failed, or null.</summary>
    string LastError { get; }

    /// <summary>
    /// Begin connecting. Returns false only if the attempt could not be
    /// started at all; a successful return does not mean the connection is
    /// established yet, so callers should watch <see cref="State"/>.
    /// </summary>
    bool Connect();

    /// <summary>
    /// Pump the transport. Must be called once per frame from the main thread;
    /// some implementations do all of their work here.
    /// </summary>
    void Poll();

    /// <summary>Take the next received payload, if any.</summary>
    bool TryDequeue(out VizPayload payload);

    /// <summary>
    /// Send accumulated Vizard user input back toward Basilisk. Ignored by
    /// transports that are receive-only.
    /// </summary>
    void SendInput(byte[] serializedVizInput);

    void Close();
}
