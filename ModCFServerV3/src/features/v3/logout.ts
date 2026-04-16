
import type { Env } from "../../env";
import { json } from "../../http/json";
import { requireBearerAuth } from "./requireBearerAuth";

export async function handleLogout(request: Request, env: Env): Promise<Response> {
  const auth = await requireBearerAuth(request, env);
  if (auth instanceof Response) return auth;

  await env.DB.prepare(
    `UPDATE tokens SET revoked_at_utc = ? WHERE token = ?`,
  )
    .bind(new Date().toISOString(), auth.token)
    .run();

  return new Response(null, { status: 204 });
}
