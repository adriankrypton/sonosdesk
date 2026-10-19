// Copies files the app needs from outside electron/ (or from node_modules) into place.
// Usage: node scripts/copy-files.mjs <i18n|vendor|icons|all>
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, copyFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(root, '..');

function copyI18n() {
  const source = join(repo, 'src', 'SonosDesk.Core', 'Localization', 'Strings');
  const target = join(root, 'renderer', 'i18n');
  if (!existsSync(source)) {
    console.warn(`i18n: ${source} not found, keeping existing catalogs`);
    return;
  }

  mkdirSync(target, { recursive: true });
  const files = readdirSync(source).filter((f) => /^[a-z]{2}\.json$/.test(f));
  for (const file of files) {
    copyFileSync(join(source, file), join(target, file));
  }

  console.log(`i18n: ${files.length} catalogs copied`);
}

function copyVendor() {
  const source = join(root, 'node_modules', 'lit-html');
  const target = join(root, 'renderer', 'vendor', 'lit-html');
  if (!existsSync(source)) {
    throw new Error('lit-html is not installed; run npm install first');
  }

  rmSync(target, { recursive: true, force: true });
  cpSync(source, target, {
    recursive: true,
    filter: (path) => !/[\\/](node\b|development)[\\/]?/.test(path.slice(source.length)) && !path.endsWith('.map') && !path.endsWith('.d.ts'),
  });
  console.log('vendor: lit-html copied');
}

function copyIcons() {
  const assets = join(repo, 'assets');
  const build = join(root, 'build');
  const appAssets = join(root, 'assets');
  mkdirSync(build, { recursive: true });
  mkdirSync(appAssets, { recursive: true });
  const copies = [
    [join(assets, 'icon.png'), join(build, 'icon.png')],
    [join(assets, 'SonosDesk.ico'), join(build, 'icon.ico')],
    [join(assets, 'SonosDesk.ico'), join(appAssets, 'icon.ico')],
    [join(assets, 'icon-256.png'), join(appAssets, 'icon.png')],
    [join(assets, 'icon-64.png'), join(root, 'renderer', 'icon.png')],
  ];
  for (const [from, to] of copies) {
    if (existsSync(from)) {
      copyFileSync(from, to);
    } else {
      console.warn(`icons: ${from} not found`);
    }
  }

  console.log('icons: copied');
}

const tasks = { i18n: copyI18n, vendor: copyVendor, icons: copyIcons };
const what = process.argv[2] ?? 'all';
if (what === 'all') {
  for (const task of Object.values(tasks)) {
    task();
  }
} else if (tasks[what]) {
  tasks[what]();
} else {
  console.error(`Unknown target "${what}". Use one of: ${Object.keys(tasks).join(', ')}, all`);
  process.exit(1);
}
