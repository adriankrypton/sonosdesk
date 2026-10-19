// Links the renderer's ES modules without running them, so missing files and missing named exports
// fail here instead of at runtime. Needs: node --experimental-vm-modules scripts/verify-imports.mjs
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', 'renderer');
const cache = new Map();
const context = vm.createContext({});

function load(path) {
  let module = cache.get(path);
  if (!module) {
    module = new vm.SourceTextModule(readFileSync(path, 'utf8'), { identifier: path, context });
    cache.set(path, module);
  }

  return module;
}

const entry = load(join(root, 'js', 'app.js'));
// link() also resolves every named import against the target module's exports.
await entry.link((specifier, referencing) => {
  if (!specifier.startsWith('.')) {
    throw new Error(`${referencing.identifier}: bare import "${specifier}" cannot load in the renderer`);
  }

  return load(resolve(dirname(referencing.identifier), specifier));
});

console.log(`${cache.size} renderer modules linked`);
