// electron-builder afterPack hook. Without a Developer ID, macOS on Apple Silicon only starts
// binaries with at least an ad-hoc signature; packing changes the bundle, so it is signed again here.
// The bundled server is signed on its own first: "--deep" does not reach executables in Resources.
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

exports.default = async function afterPack(context) {
  if (context.electronPlatformName !== 'darwin') {
    return;
  }

  const app = path.join(context.appOutDir, `${context.packager.appInfo.productFilename}.app`);
  const server = path.join(app, 'Contents', 'Resources', 'server', 'SonosDesk.Server');
  if (!fs.existsSync(server)) {
    throw new Error(`${server} is missing; run "node scripts/publish-server.mjs --platform mac" first.`);
  }

  fs.chmodSync(server, 0o755);
  execFileSync('codesign', ['--force', '--sign', '-', server], { stdio: 'inherit' });
  execFileSync('codesign', ['--force', '--deep', '--sign', '-', app], { stdio: 'inherit' });
  execFileSync('codesign', ['--verify', '--deep', '--strict', app], { stdio: 'inherit' });
};
