'use strict';

const { spawn, execFile } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline');

const READY_PREFIX = 'SONOSDESK_READY ';
const STOP_GRACE_MS = 3000;

class ServerStartError extends Error {
  constructor(kind, detail) {
    super(`${kind}: ${detail}`);
    this.kind = kind; // 'timeout' | 'exited' | 'spawn'
    this.detail = detail;
  }
}

// Only a plain loopback URL is accepted from the server's ready line.
function parseReadyLine(line) {
  if (!line.startsWith(READY_PREFIX)) {
    return null;
  }

  try {
    const url = new URL(line.slice(READY_PREFIX.length).trim());
    if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || !url.port) {
      return null;
    }

    return `http://127.0.0.1:${url.port}`;
  } catch {
    return null;
  }
}

class ServerProcess {
  constructor({ command, args, shell = false, cwd, env, logFile, timeoutMs }) {
    this.command = command;
    this.args = args;
    this.shell = shell;
    this.cwd = cwd;
    this.env = env;
    this.logFile = logFile;
    this.timeoutMs = timeoutMs;
    this.child = null;
    this.baseUrl = null;
    this.stopping = false;
    this.onUnexpectedExit = null;
    this.log = null;
  }

  start() {
    fs.mkdirSync(path.dirname(this.logFile), { recursive: true });
    this.log = fs.createWriteStream(this.logFile, { flags: 'a' });
    this.writeLog(`--- start ${new Date().toISOString()}: ${this.command} ${this.args.join(' ')}`);

    return new Promise((resolve, reject) => {
      let settled = false;
      let timer = null;
      const settle = (error, value) => {
        if (settled) {
          return;
        }

        settled = true;
        clearTimeout(timer);
        if (error) {
          reject(error);
        } else {
          resolve(value);
        }
      };

      let child;
      try {
        child = spawn(this.command, this.args, {
          cwd: this.cwd,
          env: this.env,
          shell: this.shell,
          windowsHide: true,
          // Own process group on macOS so the whole tree (dotnet run → server) can be stopped.
          detached: process.platform !== 'win32',
          stdio: ['pipe', 'pipe', 'pipe'],
        });
      } catch (error) {
        settle(new ServerStartError('spawn', error.message));
        return;
      }

      this.child = child;
      timer = setTimeout(() => {
        settle(new ServerStartError('timeout', String(Math.round(this.timeoutMs / 1000))));
        this.stop();
      }, this.timeoutMs);

      child.on('error', (error) => {
        this.writeLog(`spawn error: ${error.message}`);
        settle(new ServerStartError('spawn', error.message));
      });

      readline.createInterface({ input: child.stdout }).on('line', (line) => {
        const baseUrl = !this.baseUrl ? parseReadyLine(line) : null;
        if (baseUrl) {
          this.baseUrl = baseUrl;
          this.writeLog(`ready at ${baseUrl}`);
          settle(null, baseUrl);
        } else {
          this.writeLog(`[out] ${line}`);
        }
      });
      readline.createInterface({ input: child.stderr }).on('line', (line) => this.writeLog(`[err] ${line}`));

      child.on('exit', (code, signal) => {
        this.writeLog(`exited code=${code} signal=${signal}`);
        this.child = null;
        settle(new ServerStartError('exited', code ?? signal ?? '?'));
        if (!this.stopping && this.baseUrl && this.onUnexpectedExit) {
          this.onUnexpectedExit(code ?? signal);
        }
      });
    });
  }

  // Closing stdin asks the server to shut down; anything still alive after 3 s is killed.
  stop() {
    this.stopping = true;
    const child = this.child;
    if (!child) {
      return Promise.resolve();
    }

    return new Promise((resolve) => {
      const done = () => {
        clearTimeout(killTimer);
        resolve();
      };
      const killTimer = setTimeout(() => {
        this.writeLog('server did not stop in time, killing it');
        killTree(child.pid);
        resolve();
      }, STOP_GRACE_MS);
      child.once('exit', done);
      try {
        child.stdin.end();
      } catch {
        killTree(child.pid);
      }
    });
  }

  writeLog(line) {
    if (this.log && !this.log.destroyed) {
      this.log.write(`${line}\n`);
    }
  }

  closeLog() {
    this.log?.end();
  }
}

function killTree(pid) {
  if (!pid) {
    return;
  }

  if (process.platform === 'win32') {
    execFile('taskkill', ['/pid', String(pid), '/T', '/F'], { windowsHide: true }, () => {});
    return;
  }

  try {
    process.kill(-pid, 'SIGKILL');
  } catch {
    try {
      process.kill(pid, 'SIGKILL');
    } catch {
      // Already gone.
    }
  }
}

module.exports = { ServerProcess, ServerStartError, parseReadyLine };
