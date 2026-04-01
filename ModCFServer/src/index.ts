import type { Env } from "./env";
import { handleBindPlayer } from "./features/bindPlayer";
import { handleCreateReplayLink } from "./features/createReplayLink";
import { handleDownloadReplay } from "./features/downloadReplay";
import { handleQueryGhostBattles } from "./features/queryGhostBattles";
import { handleUploadBattleArtifact } from "./features/uploadBattleArtifact";
import { handleUploadRunSummary } from "./features/uploadRunSummary";
import { json } from "./http/json";
import { registerClient } from "./features/registerClient";

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/health") {
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/clients/register") {
      return registerClient(request, env);
    }

    if (request.method === "POST" && url.pathname === "/clients/bind-player") {
      return handleBindPlayer(request, env);
    }

    if (request.method === "POST" && url.pathname === "/runs") {
      return handleUploadRunSummary(request, env);
    }

    if (request.method === "POST" && url.pathname === "/battles") {
      return handleUploadBattleArtifact(request, env);
    }

    if (request.method === "GET" && url.pathname === "/players/me/ghost-battles") {
      return handleQueryGhostBattles(request, env);
    }

    const replayDownloadMatch = url.pathname.match(
      /^\/players\/me\/ghost-battles\/([^/]+)\/replay-link$/,
    );
    if (request.method === "POST" && replayDownloadMatch) {
      return handleCreateReplayLink(
        request,
        env,
        decodeURIComponent(replayDownloadMatch[1] ?? ""),
      );
    }

    const replayTokenMatch = url.pathname.match(/^\/replays\/([^/]+)$/);
    if (request.method === "GET" && replayTokenMatch) {
      return handleDownloadReplay(
        request,
        env,
        decodeURIComponent(replayTokenMatch[1] ?? ""),
      );
    }

    return json({ error: "not_found" }, { status: 404 });
  },
};
