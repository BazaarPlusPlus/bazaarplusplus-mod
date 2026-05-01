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

type StaticRoute = {
  method: string;
  path: string;
  handle: (request: Request, env: Env) => Promise<Response> | Response;
};

const StaticRoutes: StaticRoute[] = [
  {
    method: "GET",
    path: "/health",
    handle: () => json({ ok: true }),
  },
  {
    method: "POST",
    path: "/activate",
    handle: handleActivate,
  },
  {
    method: "POST",
    path: "/login",
    handle: handleLogin,
  },
  {
    method: "POST",
    path: "/logout",
    handle: handleLogout,
  },
  {
    method: "POST",
    path: "/run-bundles",
    handle: handleUploadRunBundle,
  },
  {
    method: "GET",
    path: "/ghost-battles",
    handle: handleQueryGhostBattles,
  },
];

function findStaticRoute(method: string, path: string): StaticRoute | undefined {
  return StaticRoutes.find((route) => route.method === method && route.path === path);
}

export default {
  async fetch(request: Request, _env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return preflight(request);
    }

    try {
      const url = new URL(request.url);
      const staticRoute = findStaticRoute(request.method, url.pathname);
      if (staticRoute) {
        return withCors(request, await staticRoute.handle(request, _env));
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
