// Publishes SonosDesk.Server as a self-contained single file into electron/server/<arch>/,
// where electron-builder picks it up (extraResources "server/${arch}").
//
//   node scripts/publish-server.mjs                      current platform and architecture
//   node scripts/publish-server.mjs --platform mac --arch x64,arm64
//   node scripts/publish-server.mjs --platform win --arch x64
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const electronDir = path.resolve(here, '..');
const project = path.resolve(electronDir, '..', 'src', 'SonosDesk.Server', 'SonosDesk.Server.csproj');

const PLATFORMS = { mac: 'osx', win: 'win', linux: 'linux' };
const ARCHS = ['x64', 'arm64'];

function option(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 && process.argv[index + 1] ? process.argv[index + 1] : fallback;
}

const currentPlatform = { darwin: 'mac', win32: 'win', linux: 'linux' }[process.platform];
const platform = option('platform', currentPlatform);
const archs = option('arch', process.arch).split(',').map((a) => a.trim()).filter(Boolean);

if (!PLATFORMS[platform]) {
  console.error(`Unknown platform "${platform}" (mac, win or linux).`);
  process.exit(2);
}

for (const arch of archs) {
  if (!ARCHS.includes(arch)) {
    console.error(`Unknown architecture "${arch}" (x64 or arm64).`);
    process.exit(2);
  }

  const rid = `${PLATFORMS[platform]}-${arch}`;
  const output = path.join(electronDir, 'server', arch);
  fs.rmSync(output, { recursive: true, force: true });
  console.log(`Publishing SonosDesk.Server for ${rid} → ${path.relative(electronDir, output)}`);

  const result = spawnSync(
    'dotnet',
    [
      'publish', project,
      '--configuration', 'Release',
      '--runtime', rid,
      '--self-contained', 'true',
      '--output', output,
      '-p:PublishSingleFile=true',
      '-p:IncludeNativeLibrariesForSelfExtract=true',
      '-p:EnableCompressionInSingleFile=true',
      '-p:DebugType=none',
      '-p:GenerateDocumentationFile=false',
    ],
    { stdio: 'inherit', shell: process.platform === 'win32' },
  );

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }

  // Only the executable is needed; drop leftovers such as web.config or appsettings files.
  const exe = platform === 'win' ? 'SonosDesk.Server.exe' : 'SonosDesk.Server';
  for (const entry of fs.readdirSync(output)) {
    if (entry !== exe) {
      fs.rmSync(path.join(output, entry), { recursive: true, force: true });
    }
  }

  if (!fs.existsSync(path.join(output, exe))) {
    console.error(`Publish did not produce ${exe}.`);
    process.exit(1);
  }

  if (platform !== 'win') {
    fs.chmodSync(path.join(output, exe), 0o755);
  }
}
