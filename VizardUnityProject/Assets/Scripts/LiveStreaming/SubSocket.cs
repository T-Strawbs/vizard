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

// NetMQ relies on System.Net.Sockets and a background listener thread, neither of
// which exists in a WebGL player, so this entire class is compiled out of browser
// builds. Browser builds reach Basilisk through VizWebSocketTransport instead.
#if !UNITY_WEBGL || UNITY_EDITOR

using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscriber socket for receive-only communication with live Basilisk simulation
/// </summary>
public class SubSocket
{
    private readonly Thread listenerWorker; //Thread to receive Basilisk NetMQ messages

    private bool listenerCancelled; //Flag to shutdown listenerWorker thread and cleanup
    private SubscriberSocket subSocket; //NetMQ SubscriberSocket

    private string address; //Basilisk Broadcast-Only Socket address 

    public delegate void MessageDelegate(NetMQMessage message);

    private readonly MessageDelegate messageDelegate;

    /// <summary>
    /// Constructor for SubSocket
    /// </summary>
    /// <param name="messageDelegate">NetMQ message delegate</param>
    /// <param name="address">socket address for receive-only communication</param>
    /// <param name="subscriptions">Basilisk NetMQ messages to subscribe to</param>
    public SubSocket(MessageDelegate messageDelegate, string address, List<string> subscriptions)
    {
        this.address = address;
        this.messageDelegate = messageDelegate;
        listenerWorker = new Thread(() => ListenerWork(subscriptions));
    }

    /// <summary>
    /// Connect to the receive-only socket address
    /// and start the subscriber socket listener thread
    /// </summary>
    /// <returns>True if start-up of socket is successful</returns>
    public bool Start()
    {
        listenerCancelled = false;
        try
        {
            AsyncIO.ForceDotNet.Force();
            subSocket = new SubscriberSocket();
            subSocket.Options.ReceiveHighWatermark = 1000;
            subSocket.Connect(address);
        }
        catch
        {
            Debug.Log("Connecting SubscriberSocket to specified address failed. Returning to Basilisk Startup Screen.");
            listenerCancelled = true;
            if (subSocket != null)
            {
                subSocket.Dispose();
            }

            return false;
        }

        listenerWorker.Start();
        return true;
    }

    /// <summary>
    /// Set flag to cancel the listener thread and begin shutdown of the socket
    /// </summary>
    public void Stop()
    {
        listenerCancelled = true;
        if (listenerWorker.IsAlive)
            listenerWorker.Join();
    }

    /// <summary>
    /// Provides the handling of received subscribed messages on the socket
    /// while listenerCancelled flag is false.
    /// If listenerCancelled is true, disposes of the subscriber socket.
    /// </summary>
    private void ListenerWork(List<string> subscriptions)
    {
        AsyncIO.ForceDotNet.Force();

        Debug.Log("Subscribe socket connecting to " + address + ".");
        subSocket.Options.ReceiveHighWatermark = 1000;
        subSocket.Connect(address);
        foreach (var subType in subscriptions)
        {
            subSocket.Subscribe(subType);
        }

        while (!listenerCancelled)
        {
            NetMQMessage message = new NetMQMessage();
            if (!subSocket.TryReceiveMultipartMessage(ref message)) continue;
            messageDelegate(message);
        }

        if (listenerCancelled)
        {
            subSocket.Dispose();
        }
    }
}

#endif
