import { build as viteBuild } from "vite";
import {
  copyFile,
  mkdtemp,
  readFile,
  readdir,
  rm,
  stat,
  writeFile,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const frontendDirectory = dirname(fileURLToPath(import.meta.url));
const resourceDirectory = resolve(frontendDirectory, "..");
const configFile = join(frontendDirectory, "vite.config.ts");
const committedArtifacts = {
  "viewer.js": join(resourceDirectory, "viewer.js"),
  "viewer.css": join(resourceDirectory, "viewer.css"),
};
const expectedOutputs = Object.keys(committedArtifacts).sort();
const sourceDirectory = join(frontendDirectory, "src");

async function outputFiles(directory) {
  const files = [];
  async function visit(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const absolute = join(current, entry.name);
      if (entry.isDirectory()) {
        await visit(absolute);
      } else if (entry.isFile()) {
        files.push(relative(directory, absolute).replaceAll("\\", "/"));
      } else {
        files.push(relative(directory, absolute).replaceAll("\\", "/"));
      }
    }
  }
  await visit(directory);
  return files.sort();
}

function assertExactOutputs(files) {
  if (
    files.length !== expectedOutputs.length
    || files.some((file, index) => file !== expectedOutputs[index])
  ) {
    throw new Error(
      `Viewer build must emit exactly ${expectedOutputs.join(", ")}; received ${files.join(", ") || "(none)"}.`,
    );
  }
}

async function assertThemeOwnership() {
  const sourceFiles = (await outputFiles(sourceDirectory)).filter((file) =>
    /\.(?:css|ts|tsx)$/u.test(file)
  );
  const violations = [];
  const stockPalette =
    /\b(?:bg|text|border|from|to|via)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|black|white)(?:-|\/|\b)/u;
  const stockTypography =
    /\btext-(?:xs|sm|base|lg|xl|2xl|3xl)\b|\b(?:text|leading|font)-\[[^\]]+\]/u;
  const literalColor = /#[\da-f]{3,8}\b/iu;
  const literalCssTypography =
    /(?:font-size|line-height|font-family)\s*:(?!\s*var\()\s*/u;
  const stockRadius =
    /\brounded(?!(?:-(?:full|none|panel|art|[trbl](?:-(?:none|panel))?))\b|-\[inherit\])(?:-(?:sm|md|lg|xl|2xl|3xl))?\b/u;
  const retiredActivityTable = /\.bpp-activity-table\b/u;
  const rawInteractiveElement = /<(?:button|table)\b/u;

  for (const file of sourceFiles) {
    if (file === "styles/theme.css") continue;
    const source = await readFile(join(sourceDirectory, file), "utf8");
    if (stockPalette.test(source)) {
      violations.push(`${file}: stock Tailwind palette`);
    }
    if (stockTypography.test(source)) {
      violations.push(`${file}: stock or arbitrary typography`);
    }
    if (literalColor.test(source)) {
      violations.push(`${file}: literal color`);
    }
    if (file.endsWith(".css") && literalCssTypography.test(source)) {
      violations.push(`${file}: literal CSS typography`);
    }
    if (stockRadius.test(source)) {
      violations.push(`${file}: stock Tailwind radius`);
    }
    if (retiredActivityTable.test(source)) {
      violations.push(`${file}: retired activity-table styling`);
    }
    if (
      file.endsWith(".tsx")
      && !file.startsWith("components/ui/")
      && rawInteractiveElement.test(source)
    ) {
      violations.push(`${file}: raw button/table outside shadcn primitives`);
    }
  }

  if (sourceFiles.includes("components/ui/control-styles.ts")) {
    violations.push(
      "components/ui/control-styles.ts: retired parallel control system",
    );
  }

  if (violations.length > 0) {
    throw new Error(
      `Viewer visual tokens must be owned by styles/theme.css: ${violations.join(", ")}.`,
    );
  }
}

async function buildArtifacts(outputDirectory) {
  await viteBuild({
    configFile,
    build: {
      emptyOutDir: true,
      outDir: outputDirectory,
    },
  });
  const files = await outputFiles(outputDirectory);
  assertExactOutputs(files);
  // ECharts emits whitespace-only lines inside generated template literals. Strip only those
  // lines so the committed artifact passes the repository's trailing-whitespace gate.
  await Promise.all(
    files.map(async (file) => {
      const path = join(outputDirectory, file);
      const source = await readFile(path, "utf8");
      const normalized = source.replace(/^[\t ]+$/gmu, "");
      if (normalized !== source) {
        await writeFile(path, normalized, "utf8");
      }
    }),
  );
  return Object.fromEntries(
    files.map((file) => [file, join(outputDirectory, file)]),
  );
}

async function bytesEqual(leftPath, rightPath) {
  const [left, right] = await Promise.all([
    readFile(leftPath),
    readFile(rightPath),
  ]);
  return left.equals(right);
}

async function assertRuntimeBoundary(artifacts) {
  const [script, stylesheet] = await Promise.all([
    readFile(artifacts["viewer.js"], "utf8"),
    readFile(artifacts["viewer.css"], "utf8"),
  ]);
  const violations = [];
  const scriptRules = [
    [/\bfetch\s*\(/u, "runtime fetch"],
    [/\bXMLHttpRequest\b/u, "XMLHttpRequest"],
    [/\bWebSocket\b/u, "WebSocket"],
    [/\bEventSource\b/u, "EventSource"],
    [/\bnew\s+(?:Shared)?Worker\s*\(/u, "runtime worker"],
    [/\bimport\s*\(/u, "dynamic import"],
  ];
  for (const [pattern, label] of scriptRules) {
    if (pattern.test(script)) violations.push(label);
  }
  if (/@import\b/u.test(stylesheet)) violations.push("runtime CSS import");
  const stylesheetUrls = Array.from(
    stylesheet.matchAll(/\burl\(\s*(['"]?)(.*?)\1\s*\)/giu),
    (match) => match[2],
  );
  if (stylesheetUrls.some((value) => !value.startsWith("data:"))) {
    violations.push("non-embedded runtime CSS URL");
  }
  if (violations.length > 0) {
    throw new Error(
      `Generated Viewer violates the static file:// boundary: ${violations.join(", ")}.`,
    );
  }
}

async function assertNonEmpty(artifacts) {
  for (const path of Object.values(artifacts)) {
    const metadata = await stat(path);
    if (metadata.size === 0) {
      throw new Error(`Viewer artifact is empty: ${path}.`);
    }
  }
}

async function generate() {
  const temporaryDirectory = await mkdtemp(join(tmpdir(), "bpp-viewer-build-"));
  try {
    await assertThemeOwnership();
    const artifacts = await buildArtifacts(temporaryDirectory);
    await assertRuntimeBoundary(artifacts);
    await assertNonEmpty(artifacts);
    await Promise.all(
      expectedOutputs.map((file) =>
        copyFile(artifacts[file], committedArtifacts[file]),
      ),
    );
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
}

async function check() {
  const temporaryDirectory = await mkdtemp(join(tmpdir(), "bpp-viewer-check-"));
  try {
    await assertThemeOwnership();
    const artifacts = await buildArtifacts(temporaryDirectory);
    await Promise.all([
      assertRuntimeBoundary(artifacts),
      assertNonEmpty(artifacts),
    ]);
    for (const file of expectedOutputs) {
      if (!(await bytesEqual(artifacts[file], committedArtifacts[file]))) {
        throw new Error(
          `Committed ${file} is stale; run npm run viewer:build.`,
        );
      }
    }
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
}

if (process.argv.includes("--check")) {
  await check();
} else {
  await generate();
}
