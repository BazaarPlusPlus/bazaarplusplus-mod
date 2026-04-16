import type { Env } from "./env";
import { handleActivate } from "./features/v3/activate";
import { handleCreateReplayLink } from "./features/v3/createReplayLink";
import { handleDownloadReplay } from "./features/v3/downloadReplay";
import { handleLogin } from "./features/v3/login";
import { handleLogout } from "./features/v3/logout";
import { handleQueryGhostBattles } from "./features/v3/queryGhostBattles";
import { handleUploadRunBundle } from "./features/v3/uploadRunBundle";
import { preflight, withCors } from "./http/cors";
import { json } from "./http/json";

export default {
  async fetch(request: Request, _env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return preflight(request);
    }

    try {
      const url = new URL(request.url);

      if (request.method === "GET" && url.pathname === "/health") {
        return withCors(request, json({ ok: true }));
      }

      if (request.method === "POST" && url.pathname === "/activate") {
        return withCors(request, await handleActivate(request, _env));
      }

      if (request.method === "POST" && url.pathname === "/login") {
        return withCors(request, await handleLogin(request, _env));
      }

      if (request.method === "POST" && url.pathname === "/logout") {
        return withCors(request, await handleLogout(request, _env));
      }

      if (request.method === "POST" && url.pathname === "/run-bundles") {
        return withCors(request, await handleUploadRunBundle(request, _env));
      }

      if (request.method === "GET" && url.pathname === "/ghost-battles") {
        return withCors(request, await handleQueryGhostBattles(request, _env));
      }

      const replayLinkMatch = url.pathname.match(/^\/ghost-battles\/([^/]+)\/replay-link$/);
      if (request.method === "POST" && replayLinkMatch) {
        return withCors(
          request,
          await handleCreateReplayLink(
            request,
            _env,
            decodeURIComponent(replayLinkMatch[1] ?? ""),
          ),
        );
      }

      const replayDownloadMatch = url.pathname.match(/^\/replays\/([^/]+)$/);
      if (request.method === "GET" && replayDownloadMatch) {
        return withCors(
          request,
          await handleDownloadReplay(
            request,
            _env,
            decodeURIComponent(replayDownloadMatch[1] ?? ""),
          ),
        );
      }

      return withCors(request, json({ error: "not_found" }, { status: 404 }));
    } catch (error) {
      if (error instanceof Response) {
        return withCors(request, error);
      }

      throw error;
    }
  },
};
