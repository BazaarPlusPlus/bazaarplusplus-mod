export interface EnvironmentInfo {
  steam_path: string | null;
  game_path: string | null;
  dotnet_version: string | null;
  dotnet_ok: boolean;
  bepinex_installed: boolean;
  bpp_version: string | null;
}

export interface UpdateInfo {
  current_version: string | null;
  latest_version: string | null;
  update_available: boolean;
}
