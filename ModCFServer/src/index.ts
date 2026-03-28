import type { Env } from "./env";
import {
  handleGhostBattleReplayDownloadLink,
  handleGhostBattlesAgainstMe,
  handleReplayDownload,
} from "./features/ghostBattles";
import { json } from "./http/json";
import { ensureSchema } from "./persistence/schema";
import { registerClient } from "./features/registerClient";
import { handleRunUpload } from "./features/uploadRun";
import { handleReplayUpload } from "./features/uploadReplay";

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    await ensureSchema(env);
    const url = new URL(request.url);

    if (request.method === "POST" && url.pathname === "/health") {
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/clients/register") {
      return registerClient(request, env);
    }

    if (request.method === "POST" && url.pathname === "/runs/upload") {
      return handleRunUpload(request, env);
    }

    if (request.method === "POST" && url.pathname === "/replays/upload") {
      return handleReplayUpload(request, env);
    }

    if (request.method === "GET" && url.pathname === "/me/pvp-battles/against-me") {
      return handleGhostBattlesAgainstMe(request, env);
    }

    const replayDownloadMatch = url.pathname.match(
      /^\/me\/pvp-battles\/([^/]+)\/replay-download-link$/,
    );
    if (request.method === "POST" && replayDownloadMatch) {
      return handleGhostBattleReplayDownloadLink(
        request,
        env,
        decodeURIComponent(replayDownloadMatch[1] ?? ""),
      );
    }

    if (request.method === "GET" && url.pathname === "/replays/download") {
      return handleReplayDownload(request, env);
    }

    return json({ error: "not_found" }, { status: 404 });
  },
};
