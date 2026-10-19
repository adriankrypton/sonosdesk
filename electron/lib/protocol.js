'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const { protocol } = require('electron');

const SCHEME = 'app';
const HOST = 'sonosdesk';
const ORIGIN = `${SCHEME}://${HOST}`;
const PROXY_TIMEOUT_MS = 90_000;
const MAX_BODY_BYTES = 3 * 1024 * 1024;

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
};

// Must run before the app is ready.
function registerScheme() {
  protocol.registerSchemesAsPrivileged([
    { scheme: SCHEME, privileges: { standard: true, secure: true, supportFetchAPI: true, codeCache: true } },
  ]);
}

function contentSecurityPolicy(server) {
  const http = server ? ` ${server.baseUrl}` : '';
  const ws = server ? ` ${server.baseUrl.replace(/^http:/, 'ws:')}` : '';
  return [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    `img-src 'self' data:${http}`,
    `connect-src 'self'${http}${ws}`,
    "object-src 'none'",
    "base-uri 'none'",
    "form-action 'none'",
    "frame-ancestors 'none'",
  ].join('; ');
}

function jsonError(status, kind) {
  return new Response(JSON.stringify({ message: '', kind }), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8' },
  });
}

async function serveFile(rendererDir, url, getServer) {
  let relative;
  try {
    relative = decodeURIComponent(url.pathname);
  } catch {
    return new Response('Bad request', { status: 400 });
  }

  if (relative === '/' || relative === '') {
    relative = '/index.html';
  }

  const file = path.normalize(path.join(rendererDir, relative));
  if (!file.startsWith(rendererDir + path.sep)) {
    return new Response('Not found', { status: 404 });
  }

  const type = CONTENT_TYPES[path.extname(file).toLowerCase()];
  if (!type) {
    return new Response('Not found', { status: 404 });
  }

  try {
    const body = await fs.readFile(file);
    const headers = { 'content-type': type, 'x-content-type-options': 'nosniff' };
    if (type.startsWith('text/html')) {
      headers['content-security-policy'] = contentSecurityPolicy(getServer());
    }

    return new Response(body, { status: 200, headers });
  } catch {
    return new Response('Not found', { status: 404 });
  }
}

// The renderer calls the API same-origin (app://sonosdesk/api/…); the main process adds the token
// and forwards to the local server. No CORS is needed and the token never ends up in the DOM.
async function proxy(request, url, server) {
  if (!server) {
    return jsonError(503, 'unavailable');
  }

  if (url.pathname === '/api/events') {
    return jsonError(404, 'notFound');
  }

  const method = request.method.toUpperCase();
  if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
    return jsonError(405, 'notSupported');
  }

  const headers = { authorization: `Bearer ${server.token}`, accept: request.headers.get('accept') || 'application/json' };
  let body;
  if (method !== 'GET') {
    const buffer = await request.arrayBuffer();
    if (buffer.byteLength > MAX_BODY_BYTES) {
      return jsonError(413, 'rejected');
    }

    if (buffer.byteLength > 0) {
      body = buffer;
      headers['content-type'] = request.headers.get('content-type') || 'application/json';
    }
  }

  try {
    const upstream = await fetch(`${server.baseUrl}${url.pathname}${url.search}`, {
      method,
      headers,
      body,
      redirect: 'error',
      signal: AbortSignal.timeout(PROXY_TIMEOUT_MS),
    });
    const responseHeaders = {};
    for (const name of ['content-type', 'cache-control', 'etag', 'last-modified']) {
      const value = upstream.headers.get(name);
      if (value) {
        responseHeaders[name] = value;
      }
    }

    const empty = upstream.status === 204 || upstream.status === 304 || method === 'HEAD';
    return new Response(empty ? null : upstream.body, { status: upstream.status, headers: responseHeaders });
  } catch (error) {
    return jsonError(error?.name === 'TimeoutError' ? 504 : 502, error?.name === 'TimeoutError' ? 'timeout' : 'unavailable');
  }
}

function installHandler({ rendererDir, getServer }) {
  const root = path.resolve(rendererDir);
  protocol.handle(SCHEME, (request) => {
    let url;
    try {
      url = new URL(request.url);
    } catch {
      return new Response('Bad request', { status: 400 });
    }

    if (url.host !== HOST) {
      return new Response('Not found', { status: 404 });
    }

    return url.pathname.startsWith('/api/') ? proxy(request, url, getServer()) : serveFile(root, url, getServer);
  });
}

module.exports = { registerScheme, installHandler, contentSecurityPolicy, ORIGIN };
