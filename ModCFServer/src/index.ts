import type { Env } from "./env";
import { handleBindClient } from "./features/bindClient";
import {
  handleGhostBattleReplayDownloadLink,
  handleGhostBattlesAgainstMe,
  handleReplayDownload,
} from "./features/ghostBattles";
import { json } from "./http/json";
import { registerClient } from "./features/registerClient";
import { handleRunUpload } from "./features/uploadRun";
import { handleBattleUpload } from "./features/uploadBattle";
import { purgeExpiredNonces } from "./persistence/nonces";

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/health") {
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/clients/register") {
      return registerClient(request, env);
    }

    if (request.method === "POST" && url.pathname === "/clients/bind") {
      return handleBindClient(request, env);
    }

    if (request.method === "POST" && url.pathname === "/runs/upload") {
      return handleRunUpload(request, env);
    }

    if (request.method === "POST" && url.pathname === "/battles/upload") {
      return handleBattleUpload(request, env);
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

  async scheduled(_event: ScheduledEvent, env: Env): Promise<void> {
    await purgeExpiredNonces(env, 15 * 60 * 1000);
  },
};
