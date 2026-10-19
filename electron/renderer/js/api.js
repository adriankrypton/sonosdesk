// HTTP goes same-origin to app://sonosdesk/api/…; the main process adds the token and forwards it
// to SonosDesk.Server. Only the WebSocket connects to the server directly.

export class ApiError extends Error {
  constructor(message, kind, status, data = null) {
    super(message);
    this.kind = kind;
    this.status = status;
    this.data = data;
  }
}

export const enc = encodeURIComponent;

async function request(method, path, body, contentType = 'application/json') {
  const init = { method, headers: { accept: 'application/json' } };
  if (body !== undefined) {
    init.headers['content-type'] = contentType;
    init.body = contentType === 'application/json' ? JSON.stringify(body) : body;
  }

  let response;
  try {
    response = await fetch(path, init);
  } catch {
    throw new ApiError('', 'unavailable', 0);
  }

  if (response.status === 204) {
    return null;
  }

  const text = await response.text();
  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = null;
    }
  }

  if (!response.ok) {
    throw new ApiError(typeof data?.message === 'string' ? data.message : '', data?.kind || 'unknown', response.status, data);
  }

  return data;
}

export const api = {
  get: (path) => request('GET', path),
  post: (path, body) => request('POST', path, body),
  put: (path, body) => request('PUT', path, body),
  putBinary: (path, blob, type) => request('PUT', path, blob, type),
  patch: (path, body) => request('PATCH', path, body),
  del: (path) => request('DELETE', path),
};

// Server image URLs ("/api/art?u=…", "/api/logo/…") load through the same proxy.
export function assetUrl(url) {
  return typeof url === 'string' && url.startsWith('/api/') ? url : null;
}

export function connectEvents(server, handlers) {
  const url = `${server.baseUrl.replace(/^http:/, 'ws:')}/api/events?token=${enc(server.token)}`;
  let socket = null;
  let attempt = 0;
  let timer = null;
  let closed = false;

  const open = () => {
    socket = new WebSocket(url);
    socket.onopen = () => {
      attempt = 0;
      handlers.onOpen();
    };
    socket.onmessage = (event) => {
      let message;
      try {
        message = JSON.parse(event.data);
      } catch {
        return;
      }

      if (message && typeof message.type === 'string') {
        handlers.onMessage(message);
      }
    };
    socket.onclose = () => {
      socket = null;
      handlers.onClose();
      if (!closed) {
        timer = setTimeout(open, Math.min(15_000, 500 * 2 ** attempt));
        attempt++;
      }
    };
    socket.onerror = () => {
      // onclose follows and schedules the reconnect.
    };
  };

  open();
  return {
    close() {
      closed = true;
      clearTimeout(timer);
      socket?.close();
    },
  };
}
