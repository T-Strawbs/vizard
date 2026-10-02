// WebSocket support for Vizard WebGL builds.
//
// A browser cannot speak ZMQ and cannot bind a listening socket, so WebGL
// builds reach Basilisk through the Python bridge over a WebSocket instead of
// through NetMQ. Unity's WebGL runtime has no System.Net.Sockets, so the socket
// itself has to live here in JavaScript.
//
// Received binary frames are queued here and pulled across one at a time by
// VizWebSocketTransport, which keeps all decoding on Unity's main thread.

var VizardWebSocketLib = {

  $vizWS: {
    sockets: [],
    // Matches the bridge's per-client queue: a visualiser only cares about the
    // newest state, so drop the oldest frames rather than growing without
    // bound if Unity falls behind the simulation.
    maxQueue: 64,

    STATE_CONNECTING: 0,
    STATE_OPEN: 1,
    STATE_CLOSED: 2,
    STATE_ERROR: 3,

    get: function (handle) {
      return this.sockets[handle];
    },
  },

  VizWS_Create: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var entry = {
      ws: null,
      state: vizWS.STATE_CONNECTING,
      queue: [],
      dropped: 0,
    };
    var handle = vizWS.sockets.length;
    vizWS.sockets.push(entry);

    try {
      entry.ws = new WebSocket(url);
    } catch (e) {
      console.error("[Vizard] WebSocket construction failed: " + e);
      entry.state = vizWS.STATE_ERROR;
      return handle;
    }

    entry.ws.binaryType = "arraybuffer";

    entry.ws.onopen = function () {
      entry.state = vizWS.STATE_OPEN;
      console.log("[Vizard] WebSocket open: " + url);
    };

    entry.ws.onmessage = function (ev) {
      // Text frames are bridge control messages; Unity does not need them.
      if (typeof ev.data === "string") {
        console.log("[Vizard] bridge: " + ev.data);
        return;
      }
      if (entry.queue.length >= vizWS.maxQueue) {
        entry.queue.shift();
        entry.dropped++;
      }
      entry.queue.push(new Uint8Array(ev.data));
    };

    entry.ws.onerror = function () {
      // The browser deliberately withholds the reason from script.
      console.error("[Vizard] WebSocket error on " + url);
      entry.state = vizWS.STATE_ERROR;
    };

    entry.ws.onclose = function (ev) {
      if (entry.state !== vizWS.STATE_ERROR) {
        entry.state = vizWS.STATE_CLOSED;
      }
      console.log("[Vizard] WebSocket closed, code " + ev.code);
    };

    return handle;
  },

  VizWS_State: function (handle) {
    var entry = vizWS.get(handle);
    return entry ? entry.state : vizWS.STATE_ERROR;
  },

  // Length of the next queued frame, or -1 when the queue is empty. Unity uses
  // this to size its buffer before calling VizWS_Recv.
  VizWS_PeekLength: function (handle) {
    var entry = vizWS.get(handle);
    if (!entry || entry.queue.length === 0) {
      return -1;
    }
    return entry.queue[0].length;
  },

  // Copy the next queued frame into a Unity-owned buffer and remove it from the
  // queue. Returns the number of bytes written, or -1 if nothing was copied.
  VizWS_Recv: function (handle, bufferPtr, bufferLen) {
    var entry = vizWS.get(handle);
    if (!entry || entry.queue.length === 0) {
      return -1;
    }
    var frame = entry.queue[0];
    if (frame.length > bufferLen) {
      // Caller's buffer is too small; leave the frame queued so it can retry.
      return -1;
    }
    entry.queue.shift();
    HEAPU8.set(frame, bufferPtr);
    return frame.length;
  },

  VizWS_Send: function (handle, bufferPtr, length) {
    var entry = vizWS.get(handle);
    if (!entry || !entry.ws || entry.state !== vizWS.STATE_OPEN) {
      return 0;
    }
    try {
      // Copy out of the Unity heap: the heap may move or be reused, and the
      // browser may hold the buffer past this call.
      entry.ws.send(HEAPU8.slice(bufferPtr, bufferPtr + length));
      return 1;
    } catch (e) {
      console.error("[Vizard] WebSocket send failed: " + e);
      return 0;
    }
  },

  VizWS_Dropped: function (handle) {
    var entry = vizWS.get(handle);
    return entry ? entry.dropped : 0;
  },

  VizWS_Close: function (handle) {
    var entry = vizWS.get(handle);
    if (!entry) {
      return;
    }
    try {
      if (entry.ws) {
        entry.ws.onmessage = null;
        entry.ws.close();
      }
    } catch (e) {
      // Already closing or closed; nothing useful to do.
    }
    entry.queue = [];
    entry.state = vizWS.STATE_CLOSED;
  },
};

autoAddDeps(VizardWebSocketLib, "$vizWS");
mergeInto(LibraryManager.library, VizardWebSocketLib);
