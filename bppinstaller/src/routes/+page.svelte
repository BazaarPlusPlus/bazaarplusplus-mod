<script lang="ts">
  import { invoke } from '@tauri-apps/api/core';
  import { listen } from '@tauri-apps/api/event';
  import { open } from '@tauri-apps/plugin-dialog';
  import { onMount, tick } from 'svelte';
  import type { EnvironmentInfo } from '$lib/types';

  let env: EnvironmentInfo | null = null;
  let loading = true;
  let installing = false;
  let logs: string[] = [];
  let customGamePath = '';
  let logEl: HTMLDivElement | undefined;

  function effectiveGamePath(): string {
    return customGamePath || env?.game_path || '';
  }

  async function addLog(message: string) {
    logs = [...logs, `[${new Date().toLocaleTimeString()}] ${message}`];
    await tick();
    logEl?.scrollTo({ top: logEl.scrollHeight });
  }

  async function refreshEnvironment() {
    loading = true;
    try {
      env = await invoke<EnvironmentInfo>('detect_environment');
    } catch (error) {
      await addLog(`Detection error: ${error}`);
    } finally {
      loading = false;
    }
  }

  onMount(() => {
    let disposed = false;
    let unlisten: null | (() => void) = null;

    void (async () => {
      unlisten = await listen<string>('bppinstaller://log', async (event) => {
        await addLog(String(event.payload));
      });

      if (!disposed) {
        await refreshEnvironment();
      }
    })();

    return () => {
      disposed = true;
      unlisten?.();
    };
  });

  async function pickGamePath() {
    const selected = await open({ directory: true, multiple: false });
    if (typeof selected === 'string') {
      customGamePath = selected;
    }
  }

  async function install() {
    if (!env?.steam_path || !effectiveGamePath()) {
      return;
    }

    installing = true;
    await addLog('Starting installation...');
    try {
      await invoke('install_bepinex', { gamePath: effectiveGamePath() });
      await invoke('patch_launch_options', {
        steamPath: env.steam_path,
        gamePath: effectiveGamePath()
      });
      await addLog('Installation complete. Launch The Bazaar from Steam.');
      await refreshEnvironment();
    } catch (error) {
      await addLog(`Error: ${error}`);
    } finally {
      installing = false;
    }
  }

  $: canInstall = !loading && !installing && Boolean(env?.steam_path) && Boolean(effectiveGamePath());
</script>

<svelte:head>
  <title>BazaarPlusPlus Mod Installer</title>
</svelte:head>

<main class="shell">
  <section class="hero">
    <p class="eyebrow">BazaarPlusPlus</p>
    <h1>BazaarPlusPlus Mod Installer</h1>
  </section>

  <section class="grid">
    <article class:pending={loading} class="card">
      <span class="card-label">Steam</span>
      <strong>{loading ? 'Detecting...' : env?.steam_path ?? 'Not found'}</strong>
    </article>
    <article class:pending={loading} class="card">
      <span class="card-label">The Bazaar</span>
      <strong>{loading ? 'Detecting...' : effectiveGamePath() || 'Not found'}</strong>
    </article>
    <article class:pending={loading} class="card" class:warn={!env?.dotnet_ok && !loading}>
      <span class="card-label">.NET Runtime</span>
      <strong>{loading ? 'Detecting...' : env?.dotnet_version ?? 'Not found (optional)'}</strong>
    </article>
    <article class:pending={loading} class="card" class:ok={Boolean(env?.bepinex_installed) && !loading}>
      <span class="card-label">BepInEx</span>
      <strong>{loading ? 'Detecting...' : env?.bepinex_installed ? 'Installed' : 'Not installed'}</strong>
    </article>
  </section>

  <section class="panel">
    <div class="panel-head">
      <div>
        <p class="eyebrow">Install Target</p>
        <h2>Game path</h2>
      </div>
      <button class="ghost" onclick={pickGamePath} type="button">Browse</button>
    </div>

    <input
      bind:value={customGamePath}
      class="path-input"
      placeholder="Choose The Bazaar install directory"
      type="text"
    />

    <button class="install" disabled={!canInstall} onclick={install} type="button">
      {installing ? 'Installing...' : env?.bepinex_installed ? 'Reinstall' : 'Install'}
    </button>
  </section>

  <section class="panel log-panel">
    <div class="panel-head">
      <div>
        <p class="eyebrow">Activity</p>
        <h2>Installer log</h2>
      </div>
      <button class="ghost" onclick={refreshEnvironment} type="button">Refresh</button>
    </div>

    <div bind:this={logEl} class="log">
      {#if logs.length === 0}
        <p class="placeholder">Logs will appear here once detection or installation starts.</p>
      {/if}

      {#each logs as line}
        <div class="log-line">{line}</div>
      {/each}
    </div>
  </section>
</main>

<style>
  :global(body) {
    margin: 0;
    background:
      radial-gradient(circle at top, rgba(255, 174, 66, 0.22), transparent 28%),
      linear-gradient(180deg, #16110f 0%, #0e0b0b 48%, #090809 100%);
    color: #f4ecdf;
    font-family: 'Avenir Next', 'Segoe UI', sans-serif;
  }

  .shell {
    min-height: 100vh;
    padding: 3rem 1.25rem 2rem;
    max-width: 980px;
    margin: 0 auto;
    display: grid;
    gap: 1.25rem;
  }

  .hero,
  .panel,
  .card {
    border: 1px solid rgba(255, 233, 204, 0.12);
    background: rgba(35, 24, 21, 0.82);
    box-shadow: 0 18px 60px rgba(0, 0, 0, 0.28);
    backdrop-filter: blur(16px);
  }

  .hero,
  .panel {
    border-radius: 24px;
    padding: 1.4rem;
  }

  .hero h1,
  .panel h2 {
    margin: 0;
    font-family: Georgia, 'Times New Roman', serif;
    letter-spacing: 0.02em;
  }

  .hero h1 {
    font-size: clamp(2rem, 5vw, 3.5rem);
    line-height: 1.02;
    max-width: 12ch;
  }

  .eyebrow {
    margin: 0 0 0.45rem;
    color: #f2b46f;
    text-transform: uppercase;
    letter-spacing: 0.16em;
    font-size: 0.72rem;
  }

  .grid {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 1rem;
  }

  .card {
    border-radius: 20px;
    padding: 1rem;
    min-height: 7rem;
    display: flex;
    flex-direction: column;
    gap: 0.65rem;
  }

  .card strong {
    font-size: 1rem;
    line-height: 1.4;
    word-break: break-word;
  }

  .card-label {
    color: #d8c7b4;
    text-transform: uppercase;
    font-size: 0.74rem;
    letter-spacing: 0.12em;
  }

  .card.pending {
    opacity: 0.65;
  }

  .card.ok {
    border-color: rgba(86, 208, 154, 0.45);
  }

  .card.warn {
    border-color: rgba(242, 180, 111, 0.45);
  }

  .panel-head {
    display: flex;
    align-items: start;
    justify-content: space-between;
    gap: 1rem;
    margin-bottom: 1rem;
  }

  .path-input,
  button {
    border: 1px solid rgba(255, 233, 204, 0.14);
    border-radius: 16px;
    color: inherit;
    font: inherit;
  }

  .path-input {
    width: 100%;
    box-sizing: border-box;
    background: rgba(13, 11, 11, 0.68);
    padding: 0.95rem 1rem;
    margin-bottom: 1rem;
  }

  button {
    cursor: pointer;
    transition:
      transform 120ms ease,
      border-color 120ms ease,
      background 120ms ease;
  }

  button:hover:enabled {
    transform: translateY(-1px);
  }

  button:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .ghost {
    background: rgba(255, 255, 255, 0.04);
    padding: 0.75rem 1rem;
  }

  .install {
    width: 100%;
    background: linear-gradient(135deg, #e39a4b 0%, #b85a2e 100%);
    padding: 0.95rem 1rem;
    font-weight: 700;
  }

  .log-panel {
    min-height: 18rem;
  }

  .log {
    height: 15rem;
    overflow: auto;
    border-radius: 18px;
    background: rgba(10, 10, 12, 0.86);
    padding: 1rem;
    font-family: 'SFMono-Regular', 'Menlo', monospace;
    font-size: 0.84rem;
    line-height: 1.6;
  }

  .placeholder {
    margin: 0;
    color: #92806c;
  }

  .log-line + .log-line {
    margin-top: 0.4rem;
  }

  @media (max-width: 720px) {
    .shell {
      padding-top: 1rem;
    }

    .grid {
      grid-template-columns: 1fr;
    }

    .panel-head {
      flex-direction: column;
    }
  }
</style>
