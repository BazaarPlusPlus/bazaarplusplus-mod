export interface EnvironmentInfo {
  steam_path: string | null;
  game_path: string | null;
  dotnet_version: string | null;
  dotnet_ok: boolean;
  bepinex_installed: boolean;
  bpp_version: string | null;
}

export interface DotnetInfo {
  dotnet_version: string | null;
  dotnet_ok: boolean;
}

export interface ModConfigReadResult {
  config_exists: boolean;
  values: Record<string, string>;
}

export interface UpdateInfo {
  current_version: string | null;
  latest_version: string | null;
  update_available: boolean;
}
