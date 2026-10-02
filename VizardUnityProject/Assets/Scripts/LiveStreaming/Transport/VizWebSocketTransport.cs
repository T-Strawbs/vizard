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
using System.Collections.Concurrent;
using UnityEngine;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
#endif

/// <summary>
/// Receives Basilisk messages over a WebSocket from the Python bridge
/// (bridge/bsk_viz_bridge.py).
/// <remarks>
/// <para>Two implementations live here behind one API. WebGL builds have no
/// System.Net.Sockets, so the socket is owned by JavaScript in
/// Plugins/WebGL/VizardWebSocket.jslib. Everywhere else — including the Editor —
/// a ClientWebSocket is used, so the browser transport can be developed and
/// debugged in the Editor without producing a WebGL build.</para>
/// <para>Wire format is a single tag byte followed by a protobuf payload; see
/// <see cref="VizPayloadType"/>.</para>
/// </remarks>
/// </summary>
public class VizWebSocketTransport : IVizTransport
{
    private const byte TagClientInput = 0x10;

    /// <summary>Largest frame accepted from the bridge, as a sanity bound.</summary>
    private const int MaxFrameBytes = 64 * 1024 * 1024;

    private readonly string url;
    private readonly ConcurrentQueue<VizPayload> inbox = new ConcurrentQueue<VizPayload>();

    private VizTransportState state = VizTransportState.Idle;
    private string lastError;

    public VizTransportState State => state;
    public string LastError => lastError;

    /// <param name="url">Bridge endpoint, for example ws://127.0.0.1:8765</param>
    public VizWebSocketTransport(string url)
    {
        this.url = url;
    }

    /// <summary>
    /// True if the address names a WebSocket endpoint and so should be handled
    /// by this transport rather than by NetMQ.
    /// </summary>
    public static bool IsWebSocketAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        return address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
               || address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
    }

    public bool TryDequeue(out VizPayload payload)
    {
        return inbox.TryDequeue(out payload);
    }

    /// <summary>
    /// Split a received frame into its tag and payload and queue it for the
    /// main thread. Unknown tags are logged and dropped so that adding message
    /// types to the bridge does not break older clients.
    /// </summary>
    private void EnqueueFrame(byte[] frame, int length)
    {
        if (length < 1)
        {
            return;
        }

        byte tag = frame[0];
        if (tag != (byte)VizPayloadType.SimUpdate && tag != (byte)VizPayloadType.SyncSettings)
        {
            Debug.LogWarning($"Bridge sent an unrecognized frame tag 0x{tag:x2}; ignoring it.");
            return;
        }

        byte[] payload = new byte[length - 1];
        Buffer.BlockCopy(frame, 1, payload, 0, length - 1);
        inbox.Enqueue(new VizPayload((VizPayloadType)tag, payload));
    }

    private void Fail(string message)
    {
        lastError = message;
        state = VizTransportState.Failed;
        Debug.LogError($"Bridge connection failed: {message}");
    }

#if UNITY_WEBGL && !UNITY_EDITOR

    // --- WebGL: the socket is owned by VizardWebSocket.jslib -----------------

    [DllImport("__Internal")] private static extern int VizWS_Create(string url);
    [DllImport("__Internal")] private static extern int VizWS_State(int handle);
    [DllImport("__Internal")] private static extern int VizWS_PeekLength(int handle);
    [DllImport("__Internal")] private static extern int VizWS_Recv(int handle, byte[] buffer, int bufferLen);
    [DllImport("__Internal")] private static extern int VizWS_Send(int handle, byte[] buffer, int length);
    [DllImport("__Internal")] private static extern int VizWS_Dropped(int handle);
    [DllImport("__Internal")] private static extern void VizWS_Close(int handle);

    private const int JsStateConnecting = 0;
    private const int JsStateOpen = 1;
    private const int JsStateClosed = 2;
    private const int JsStateError = 3;

    private int handle = -1;
    private byte[] scratch = new byte[64 * 1024];
    private int reportedDrops;

    public bool Connect()
    {
        state = VizTransportState.Connecting;
        handle = VizWS_Create(url);
        if (handle < 0)
        {
            Fail("the browser refused to create a WebSocket");
            return false;
        }

        Debug.Log($"Connecting to bridge at {url}");
        return true;
    }

    public void Poll()
    {
        if (handle < 0)
        {
            return;
        }

        switch (VizWS_State(handle))
        {
            case JsStateOpen:
                state = VizTransportState.Connected;
                break;
            case JsStateConnecting:
                state = VizTransportState.Connecting;
                break;
            case JsStateClosed:
                if (state != VizTransportState.Failed)
                {
                    state = VizTransportState.Closed;
                }
                break;
            case JsStateError:
                // The browser does not expose the reason to script; the
                // browser console carries the detail.
                Fail($"could not reach {url} (see the browser console)");
                break;
        }

        // Drain everything JavaScript has queued since the last frame.
        while (true)
        {
            int needed = VizWS_PeekLength(handle);
            if (needed < 0)
            {
                break;
            }

            if (needed > MaxFrameBytes)
            {
                Fail($"bridge sent an implausible {needed} byte frame");
                return;
            }

            if (needed > scratch.Length)
            {
                scratch = new byte[Mathf.NextPowerOfTwo(needed)];
            }

            int copied = VizWS_Recv(handle, scratch, scratch.Length);
            if (copied < 0)
            {
                break;
            }

            EnqueueFrame(scratch, copied);
        }

        int drops = VizWS_Dropped(handle);
        if (drops > reportedDrops)
        {
            Debug.LogWarning(
                $"Dropped {drops - reportedDrops} message(s): Vizard is not keeping up with the simulation rate.");
            reportedDrops = drops;
        }
    }

    public void SendInput(byte[] serializedVizInput)
    {
        if (handle < 0 || state != VizTransportState.Connected)
        {
            return;
        }

        byte[] framed = new byte[(serializedVizInput?.Length ?? 0) + 1];
        framed[0] = TagClientInput;
        if (serializedVizInput != null && serializedVizInput.Length > 0)
        {
            Buffer.BlockCopy(serializedVizInput, 0, framed, 1, serializedVizInput.Length);
        }

        VizWS_Send(handle, framed, framed.Length);
    }

    public void Close()
    {
        if (handle >= 0)
        {
            VizWS_Close(handle);
            handle = -1;
        }

        if (state != VizTransportState.Failed)
        {
            state = VizTransportState.Closed;
        }
    }

#else

    // --- Editor and desktop: ClientWebSocket on a background task -----------

    private ClientWebSocket socket;
    private CancellationTokenSource cancellation;
    private int droppedFrames;
    private int reportedDrops;

    /// <summary>Bound on the inbox so a stalled main thread cannot exhaust memory.</summary>
    private const int MaxInboxDepth = 64;

    public bool Connect()
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            Fail($"'{url}' is not a valid WebSocket URL");
            return false;
        }

        state = VizTransportState.Connecting;
        cancellation = new CancellationTokenSource();
        socket = new ClientWebSocket();
        Debug.Log($"Connecting to bridge at {url}");
        _ = RunAsync(uri, cancellation.Token);
        return true;
    }

    private async Task RunAsync(Uri uri, CancellationToken token)
    {
        try
        {
            await socket.ConnectAsync(uri, token);
            state = VizTransportState.Connected;
            Debug.Log($"Connected to bridge at {url}");
            await ReceiveLoopAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] chunk = new byte[64 * 1024];
        using MemoryStream assembled = new MemoryStream();

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            assembled.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    state = VizTransportState.Closed;
                    return;
                }

                assembled.Write(chunk, 0, result.Count);
                if (assembled.Length > MaxFrameBytes)
                {
                    Fail($"bridge sent an implausible {assembled.Length} byte frame");
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                // Bridge control message; informational only.
                Debug.Log($"Bridge: {System.Text.Encoding.UTF8.GetString(assembled.GetBuffer(), 0, (int)assembled.Length)}");
                continue;
            }

            if (inbox.Count >= MaxInboxDepth)
            {
                // Keep the newest state, which is all a visualiser needs.
                if (inbox.TryDequeue(out _))
                {
                    Interlocked.Increment(ref droppedFrames);
                }
            }

            EnqueueFrame(assembled.GetBuffer(), (int)assembled.Length);
        }

        if (state == VizTransportState.Connected)
        {
            state = VizTransportState.Closed;
        }
    }

    public void Poll()
    {
        int drops = droppedFrames;
        if (drops > reportedDrops)
        {
            Debug.LogWarning(
                $"Dropped {drops - reportedDrops} message(s): Vizard is not keeping up with the simulation rate.");
            reportedDrops = drops;
        }
    }

    public void SendInput(byte[] serializedVizInput)
    {
        if (socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        byte[] framed = new byte[(serializedVizInput?.Length ?? 0) + 1];
        framed[0] = TagClientInput;
        if (serializedVizInput != null && serializedVizInput.Length > 0)
        {
            Buffer.BlockCopy(serializedVizInput, 0, framed, 1, serializedVizInput.Length);
        }

        // Fire and forget: user input is not worth stalling a frame for, and a
        // failed send simply means Basilisk sees no input this step.
        _ = socket.SendAsync(
            new ArraySegment<byte>(framed), WebSocketMessageType.Binary, true,
            cancellation?.Token ?? CancellationToken.None);
    }

    public void Close()
    {
        try
        {
            cancellation?.Cancel();
            socket?.Dispose();
        }
        catch (Exception e)
        {
            Debug.Log($"Ignoring error while closing bridge connection: {e.Message}");
        }
        finally
        {
            socket = null;
            cancellation?.Dispose();
            cancellation = null;
            if (state != VizTransportState.Failed)
            {
                state = VizTransportState.Closed;
            }
        }
    }

#endif

    public void Dispose()
    {
        Close();
    }
}
