import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.argv[2] || 8765);
const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".mp4": "video/mp4",
  ".png": "image/png",
};

function resolveRequestPath(url) {
  const pathname = decodeURIComponent(new URL(url, "http://127.0.0.1").pathname);
  const relative = pathname === "/" ? "index.html" : pathname.replace(/^\/+/, "");
  const resolved = path.resolve(root, relative);
  return resolved === root || resolved.startsWith(`${root}${path.sep}`) ? resolved : null;
}

function parseRange(header, size) {
  const match = /^bytes=(\d*)-(\d*)$/.exec(header || "");
  if (!match) return null;
  const requestedStart = match[1] ? Number(match[1]) : null;
  const requestedEnd = match[2] ? Number(match[2]) : null;
  const start = requestedStart ?? Math.max(0, size - (requestedEnd || 0));
  const end = Math.min(size - 1, requestedEnd ?? size - 1);
  return Number.isFinite(start) && Number.isFinite(end) && start >= 0 && start <= end && start < size
    ? { start, end }
    : null;
}

const server = http.createServer((request, response) => {
  const target = resolveRequestPath(request.url || "/");
  if (!target) {
    response.writeHead(403).end();
    return;
  }
  fs.stat(target, (statError, stat) => {
    if (statError || !stat.isFile()) {
      response.writeHead(404).end();
      return;
    }
    const headers = {
      "Accept-Ranges": "bytes",
      "Cache-Control": [".html", ".css", ".js", ".json", ".mjs"].includes(path.extname(target).toLowerCase())
        ? "no-store"
        : "no-cache",
      "Content-Type": contentTypes[path.extname(target).toLowerCase()] || "application/octet-stream",
    };
    const range = parseRange(request.headers.range, stat.size);
    if (request.headers.range && !range) {
      response.writeHead(416, { ...headers, "Content-Range": `bytes */${stat.size}` }).end();
      return;
    }
    if (range) {
      const length = range.end - range.start + 1;
      response.writeHead(206, {
        ...headers,
        "Content-Length": length,
        "Content-Range": `bytes ${range.start}-${range.end}/${stat.size}`,
      });
      if (request.method === "HEAD") response.end();
      else fs.createReadStream(target, range).pipe(response);
      return;
    }
    response.writeHead(200, { ...headers, "Content-Length": stat.size });
    if (request.method === "HEAD") response.end();
    else fs.createReadStream(target).pipe(response);
  });
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Post-combat timeline: http://127.0.0.1:${port}/`);
});
