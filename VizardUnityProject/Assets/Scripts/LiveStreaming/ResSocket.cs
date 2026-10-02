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
using UnityEngine;

/// <summary>
/// Response socket for two-way communication with live Basilisk simulation
/// </summary>
public class ResSocket
{
    private readonly Thread listenerWorker; //Thread to receive and send messages
    private bool listenerCancelled; //Flag to shutdown listenerWorker thread and cleanup
    private ResponseSocket server; //NetMQ ResponseSocket

    private string address; //Basilisk Two-Way Communication Socket Address

    //NetMQ MessageDelegate
    public delegate NetMQMessage MessageDelegate(NetMQMessage message);
    private readonly MessageDelegate messageDelegate;
    
    
    /// <summary>
    /// Constructor for ResSocket
    /// </summary>
    /// <param name="address">socket address for two-way communication</param>
    /// <param name="messageDelegate">NetMQ message delegate</param>
    public ResSocket(string address, MessageDelegate messageDelegate)
    {
        this.address = address;
        this.messageDelegate = messageDelegate;
        listenerWorker = new Thread(ListenerWork);
    }

    /// <summary>
    /// Connect to the two-way socket address
    /// and start the response socket listener thread
    /// </summary>
    /// <returns>True if start-up of socket is successful</returns>
    public bool Start()
    {
        listenerCancelled = false;
        try
        {
            AsyncIO.ForceDotNet.Force();
            Debug.Log($"Trying to connect to {address}");
            server = new ResponseSocket("@" + address);
        }
        catch
        {
            Debug.Log("Creation of ResponseSocket failed. Returning to Basilisk Startup Screen.");
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
        {
            listenerWorker.Join();
        }
    }

    /// <summary>
    /// Provides the receive and response to messages on the socket while listenerCancelled
    /// flag is false. If listenerCancelled is true, disposes of the response socket.
    /// </summary>
    private void ListenerWork()
    {
        AsyncIO.ForceDotNet.Force();
        Debug.Log("Response socket connecting to " + address + ".");

        server.Connect(address);

        while (!listenerCancelled)
        {
            NetMQMessage message = new NetMQMessage();

            if (!server.TryReceiveMultipartMessage(ref message))
            {
                continue;
            }

            var response = messageDelegate(message);
            while (!listenerCancelled)
            {
                if (server.TrySendMultipartMessage(response))
                {
                    break;
                }
            }
        }

        if (listenerCancelled)
        {
            server.Dispose();
        }
    }
}

#endif
