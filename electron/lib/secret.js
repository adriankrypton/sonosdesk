'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const KEY_BYTES = 32;

function isValidKey(base64) {
  return typeof base64 === 'string' && Buffer.from(base64, 'base64').length === KEY_BYTES;
}

function writeAtomic(file, data) {
  const temp = `${file}.${process.pid}.tmp`;
  fs.writeFileSync(temp, data, { mode: 0o600 });
  fs.renameSync(temp, file);
}

// The server encrypts its secrets (Control API tokens) with this key. It is created once and kept
// encrypted with the OS key store (Keychain on macOS, DPAPI on Windows) through Electron safeStorage.
// Without a key store a fresh key is used per run, so stored secrets cannot be read in later runs.
function loadOrCreateSecretKey({ safeStorage, userDataDir, logger }) {
  const file = path.join(userDataDir, 'secret.bin');
  if (!safeStorage.isEncryptionAvailable()) {
    logger.warn('safeStorage is not available; using a temporary secret key for this run');
    return { key: crypto.randomBytes(KEY_BYTES).toString('base64'), persistent: false };
  }

  if (fs.existsSync(file)) {
    try {
      const key = safeStorage.decryptString(fs.readFileSync(file));
      if (isValidKey(key)) {
        return { key, persistent: true };
      }

      logger.warn('secret.bin holds no valid key; creating a new one');
    } catch (error) {
      logger.warn(`secret.bin could not be decrypted (${error.message}); creating a new one`);
    }
  }

  const key = crypto.randomBytes(KEY_BYTES).toString('base64');
  try {
    fs.mkdirSync(userDataDir, { recursive: true });
    writeAtomic(file, safeStorage.encryptString(key));
    return { key, persistent: true };
  } catch (error) {
    logger.warn(`secret.bin could not be written (${error.message}); the key is valid for this run only`);
    return { key, persistent: false };
  }
}

module.exports = { loadOrCreateSecretKey, writeAtomic };
