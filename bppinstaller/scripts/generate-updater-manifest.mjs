import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const projectRoot = path.resolve(import.meta.dirname, '..');
const defaultBundleDir = path.join(projectRoot, 'src-tauri', 'target', 'release', 'bundle');
const defaultOutput = path.join(projectRoot, 'latest.json');

function fail(message) {
  console.error(`Error: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const options = {
    bundleDir: defaultBundleDir,
    output: defaultOutput,
    pubDate: new Date().toISOString(),
    notes: '',
    dryRun: false,
    defaultPlatform: '',
    flattenNames: false
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];

    switch (arg) {
      case '--version':
        options.version = argv[++index];
        break;
      case '--base-url':
        options.baseUrl = argv[++index];
        break;
      case '--bundle-dir':
        options.bundleDir = path.resolve(argv[++index]);
        break;
      case '--output':
        options.output = path.resolve(argv[++index]);
        break;
      case '--notes':
        options.notes = argv[++index] ?? '';
        break;
      case '--notes-file':
        options.notes = fs.readFileSync(path.resolve(argv[++index]), 'utf8');
        break;
      case '--pub-date':
        options.pubDate = argv[++index];
        break;
      case '--default-platform':
        options.defaultPlatform = argv[++index] ?? '';
        break;
      case '--flatten-names':
        options.flattenNames = true;
        break;
      case '--dry-run':
        options.dryRun = true;
        break;
      case '--help':
      case '-h':
        printHelp();
        process.exit(0);
      default:
        fail(`Unknown argument: ${arg}`);
    }
  }

  if (!options.version) {
    fail('Missing required argument: --version');
  }

  if (!options.baseUrl) {
    fail('Missing required argument: --base-url');
  }

  return options;
}

function printHelp() {
  console.log(`Usage:
  node scripts/generate-updater-manifest.mjs --version <semver> --base-url <https-url> [options]

Options:
  --bundle-dir <path>         Bundle directory to scan. Default: src-tauri/target/release/bundle
  --output <path>             Output path for latest.json. Default: ./latest.json
  --notes <text>              Update notes string.
  --notes-file <path>         Read update notes from a file.
  --pub-date <iso-string>     Publish date. Default: current UTC timestamp
  --default-platform <key>    Fallback platform key, e.g. windows-x86_64
  --flatten-names             Copy updater assets to normalized filenames without spaces
  --dry-run                   Print JSON instead of writing a file
  --help                      Show this message`);
}

function walkFiles(rootDir) {
  const entries = fs.readdirSync(rootDir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(rootDir, entry.name);
    if (entry.isDirectory()) {
      files.push(...walkFiles(fullPath));
      continue;
    }

    if (entry.isFile()) {
      files.push(fullPath);
    }
  }

  return files;
}

function normalizeUrlSegment(segment) {
  return segment.split(path.sep).join('/');
}

function flattenFileName(fileName) {
  return fileName
    .normalize('NFKD')
    .replace(/[^\x00-\x7F]/g, '')
    .replace(/\s+/g, '-')
    .replace(/[^A-Za-z0-9._-]/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

function ensurePublishedAsset(assetPath, sigPath, options) {
  if (!options.flattenNames) {
    return {
      publishedAssetPath: assetPath,
      publishedSigPath: sigPath
    };
  }

  const dir = path.dirname(assetPath);
  const normalizedName = flattenFileName(path.basename(assetPath));
  const normalizedSigName = `${normalizedName}.sig`;
  const publishedAssetPath = path.join(dir, normalizedName);
  const publishedSigPath = path.join(dir, normalizedSigName);

  if (publishedAssetPath !== assetPath) {
    fs.copyFileSync(assetPath, publishedAssetPath);
  }

  if (publishedSigPath !== sigPath) {
    fs.copyFileSync(sigPath, publishedSigPath);
  }

  return {
    publishedAssetPath,
    publishedSigPath
  };
}

function inferArch(filePath) {
  const lower = filePath.toLowerCase();
  if (/(^|[^a-z0-9])(aarch64|arm64)([^a-z0-9]|$)/.test(lower)) return 'aarch64';
  if (/(^|[^a-z0-9])(x86_64|x64|amd64)([^a-z0-9]|$)/.test(lower)) return 'x86_64';
  if (/(^|[^a-z0-9])(i686|x86)([^a-z0-9]|$)/.test(lower)) return 'i686';
  return null;
}

function inferPlatformKey(filePath, defaultPlatform) {
  const lower = filePath.toLowerCase();
  const arch = inferArch(lower);

  if (lower.includes(`${path.sep}nsis${path.sep}`) || lower.endsWith('.msi.zip') || lower.endsWith('.nsis.zip')) {
    return arch ? `windows-${arch}` : defaultPlatform || null;
  }

  if (lower.endsWith('.app.tar.gz') || lower.includes('darwin') || lower.includes('macos')) {
    return arch ? `darwin-${arch}` : defaultPlatform || null;
  }

  if (lower.endsWith('.appimage.tar.gz') || lower.includes('linux') || lower.includes('appimage')) {
    return arch ? `linux-${arch}` : defaultPlatform || null;
  }

  return defaultPlatform || null;
}

function buildManifest(options) {
  if (!fs.existsSync(options.bundleDir)) {
    fail(`Bundle directory does not exist: ${options.bundleDir}`);
  }

  const files = walkFiles(options.bundleDir);
  const signatureFiles = files.filter((file) => file.endsWith('.sig'));

  if (signatureFiles.length === 0) {
    fail(`No .sig files found under ${options.bundleDir}`);
  }

  const platforms = {};

  for (const sigPath of signatureFiles) {
    const assetPath = sigPath.slice(0, -4);
    if (!fs.existsSync(assetPath)) {
      fail(`Signature file has no matching asset: ${sigPath}`);
    }

    const platformKey = inferPlatformKey(assetPath, options.defaultPlatform);
    if (!platformKey) {
      fail(`Could not infer updater target for asset: ${assetPath}. Pass --default-platform.`);
    }

    const { publishedAssetPath, publishedSigPath } = ensurePublishedAsset(assetPath, sigPath, options);
    const relativeAssetPath = path.relative(options.bundleDir, publishedAssetPath);
    const assetUrl = `${options.baseUrl.replace(/\/+$/, '')}/${normalizeUrlSegment(relativeAssetPath)}`;
    const signature = fs.readFileSync(publishedSigPath, 'utf8').trim();

    if (!signature) {
      fail(`Signature file was empty: ${sigPath}`);
    }

    platforms[platformKey] = {
      url: assetUrl,
      signature
    };
  }

  return {
    version: options.version,
    notes: options.notes,
    pub_date: options.pubDate,
    platforms
  };
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const manifest = buildManifest(options);
  const payload = `${JSON.stringify(manifest, null, 2)}\n`;

  if (options.dryRun) {
    process.stdout.write(payload);
    return;
  }

  fs.mkdirSync(path.dirname(options.output), { recursive: true });
  fs.writeFileSync(options.output, payload, 'utf8');
  console.log(`Wrote updater manifest to ${options.output}`);
}

main();
