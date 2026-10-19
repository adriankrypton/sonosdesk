// Syntax check (node --check) of all JavaScript sources, without node_modules, vendor and build output,
// then a link check of the renderer's ES module imports.
import { spawnSync } from 'node:child_process';
import { readdirSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const skip = new Set(['node_modules', 'vendor', 'dist', 'server', 'build']);

function collect(dir, files = []) {
  for (const entry of readdirSync(dir)) {
    if (skip.has(entry) || entry.startsWith('.')) {
      continue;
    }

    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      collect(path, files);
    } else if (/\.(c|m)?js$/.test(entry)) {
      files.push(path);
    }
  }

  return files;
}

let failed = 0;
const files = collect(root);
for (const file of files) {
  const result = spawnSync(process.execPath, ['--check', file], { encoding: 'utf8' });
  if (result.status !== 0) {
    failed++;
    console.error(`✗ ${relative(root, file)}\n${result.stderr}`);
  }
}

console.log(`${files.length - failed}/${files.length} files OK`);
const linked = spawnSync(process.execPath, ['--experimental-vm-modules', '--no-warnings', join(root, 'scripts', 'verify-imports.mjs')], { stdio: 'inherit' });
process.exit(failed > 0 || linked.status !== 0 ? 1 : 0);
