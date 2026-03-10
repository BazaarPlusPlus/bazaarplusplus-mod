<script lang="ts">
  import { invoke } from '@tauri-apps/api/core';
  import { open } from '@tauri-apps/plugin-dialog';
  import type { EnvironmentInfo } from '$lib/types';

  type StepState = 'idle' | 'detecting' | 'found' | 'not_found';

  let env: EnvironmentInfo | null = null;
  let dotnetState: StepState = 'idle';
  let bazaarFound = false;
  let bazaarChecking = false;
  let bazaarInvalid = false;
  let gameVersion = '';
  let customGamePath = '';
  let installing = false;

  function effectiveGamePath(): string {
    return customGamePath || env?.game_path || '';
  }

  async function verifyGamePath(path: string) {
    return invoke<string | null>('verify_game_path', { path });
  }

  async function detectDotnet() {
    if (dotnetState === 'detecting') return;
    dotnetState = 'detecting';
    try {
      env = await invoke<EnvironmentInfo>('detect_environment');
      dotnetState = env.dotnet_ok ? 'found' : 'not_found';
      if (env.game_path && !customGamePath) {
        const version = await verifyGamePath(env.game_path);
        gameVersion = version ?? '';
        bazaarFound = version !== null;
        bazaarInvalid = version === null;
      } else if (!customGamePath) {
        bazaarFound = false;
        bazaarInvalid = false;
        gameVersion = '';
      }
    } catch {
      dotnetState = 'idle';
    }
  }

  async function pickGamePath() {
    const selected = await open({ directory: true, multiple: false });
    if (typeof selected === 'string') {
      customGamePath = selected;
    }
  }

  async function checkPath() {
    const path = effectiveGamePath();
    if (!path) return;
    bazaarChecking = true;
    bazaarInvalid = false;
    try {
      const version = await verifyGamePath(path);
      if (version !== null) {
        gameVersion = version;
        bazaarFound = true;
      } else {
        bazaarInvalid = true;
      }
    } catch {
      bazaarInvalid = true;
    } finally {
      bazaarChecking = false;
    }
  }

  function resetBazaar() {
    bazaarFound = false;
    bazaarInvalid = false;
    gameVersion = '';
    customGamePath = '';
  }

  async function install() {
    if (!canInstall) return;
    installing = true;
    try {
      await invoke('install_bepinex', { gamePath: effectiveGamePath() });
      await invoke('patch_launch_options', {
        steamPath: env!.steam_path,
        gamePath: effectiveGamePath()
      });
      env = await invoke<EnvironmentInfo>('detect_environment');
    } catch (e) {
      console.error(e);
    } finally {
      installing = false;
    }
  }

  $: hasPath = Boolean(customGamePath || env?.game_path);
  $: steamFound = Boolean(env?.steam_path);
  $: modInstalled = Boolean(env?.bpp_version);

  $: canInstall =
    !installing &&
    dotnetState !== 'idle' &&
    bazaarFound &&
    hasPath;
</script>

<svelte:head>
  <title>BazaarPlusPlus Installer</title>
  <link rel="preconnect" href="https://fonts.googleapis.com" />
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin="anonymous" />
  <link href="https://fonts.googleapis.com/css2?family=Cinzel+Decorative:wght@700&family=Cinzel:wght@400;600&family=IM+Fell+English:ital@0;1&family=Fira+Code:wght@400;500&display=swap" rel="stylesheet" />
</svelte:head>

<div class="grain" aria-hidden="true"></div>

<main class="shell">
  <!-- Header -->
  <header class="header" data-tauri-drag-region>
    <div class="corner tl" aria-hidden="true">
      <svg width="40" height="40" viewBox="0 0 40 40" fill="none">
        <path d="M2 2L2 16M2 2L16 2" stroke="currentColor" stroke-width="1.5" stroke-linecap="square"/>
        <circle cx="2" cy="2" r="1.5" fill="currentColor"/>
      </svg>
    </div>
    <div class="corner tr" aria-hidden="true">
      <svg width="40" height="40" viewBox="0 0 40 40" fill="none">
        <path d="M38 2L38 16M38 2L24 2" stroke="currentColor" stroke-width="1.5" stroke-linecap="square"/>
        <circle cx="38" cy="2" r="1.5" fill="currentColor"/>
      </svg>
    </div>

    <div class="sigil" aria-hidden="true">
      <svg width="44" height="44" viewBox="0 0 44 44" fill="none">
        <polygon points="22,3 41,34 3,34" stroke="currentColor" stroke-width="1" fill="none" opacity="0.55"/>
        <polygon points="22,11 35,31 9,31" stroke="currentColor" stroke-width="0.5" fill="none" opacity="0.3"/>
        <circle cx="22" cy="22" r="5" stroke="currentColor" stroke-width="0.8" fill="none"/>
        <circle cx="22" cy="22" r="2" fill="currentColor" opacity="0.75"/>
      </svg>
    </div>

    <p class="kicker">Arcane Foundry</p>
    <h1>BazaarPlusPlus</h1>
    <p class="subtitle"><em>Mod Installation Rite</em></p>

    <div class="rule" aria-hidden="true">
      <span></span><span class="diamond">◆</span><span></span>
    </div>
  </header>

  <!-- Steps -->
  <div class="steps">
    <!-- Step 1: Steam -->
    <div class="step" class:step-found={steamFound}>
      <div class="step-index" aria-hidden="true">I</div>
      <div class="step-body">
        <span class="step-title">
          Steam
          {#if steamFound}
            <span class="tag tag-ok">Found</span>
          {/if}
        </span>

        {#if dotnetState === 'idle' || dotnetState === 'detecting'}
          <button
            class="bar-btn"
            class:bar-btn-loading={dotnetState === 'detecting'}
            onclick={detectDotnet}
            type="button"
            disabled={dotnetState === 'detecting'}
          >
            {#if dotnetState === 'detecting'}
              <span class="spinner" aria-hidden="true"></span>
              Divining…
            {:else}
              <span class="bar-dash" aria-hidden="true">—</span>
              Click to detect
            {/if}
          </button>
        {:else if env?.steam_path}
          <p class="detail-line detail-path" title={env.steam_path}>{env.steam_path}</p>
        {:else}
          <p class="detail-line detail-muted">Steam path not found</p>
        {/if}
      </div>
    </div>

    <!-- Step 2: .NET Runtime -->
    <div class="step" class:step-found={dotnetState === 'found'} class:step-warn={dotnetState === 'not_found'}>
      <div class="step-index" aria-hidden="true">II</div>
      <div class="step-body">
        <span class="step-title">
          .NET Runtime
          {#if dotnetState === 'found'}
            <span class="tag tag-ok">{env?.dotnet_version ?? 'OK'}</span>
          {:else if dotnetState === 'not_found'}
            <span class="tag tag-warn">Optional — not found</span>
          {/if}
        </span>

        {#if dotnetState === 'found' && env?.dotnet_version}
          <p class="detail-line detail-muted">Runtime: {env.dotnet_version}</p>
        {:else if dotnetState === 'not_found'}
          <p class="detail-line detail-muted">No compatible .NET runtime was detected</p>
        {:else if dotnetState === 'idle'}
          <p class="detail-line detail-muted">Run detection to inspect the local runtime</p>
        {/if}
      </div>
    </div>

    <!-- Step 3: The Bazaar -->
    <div class="step" class:step-found={bazaarFound}>
      <div class="step-index" aria-hidden="true">III</div>
      <div class="step-body">
        <span class="step-title">
          The Bazaar
          {#if bazaarFound}
            <span class="tag tag-ok">{gameVersion ? `v${gameVersion}` : 'Found'}</span>
          {/if}
        </span>

        {#if bazaarFound}
          <p class="detail-line detail-path" title={effectiveGamePath()}>{effectiveGamePath()}</p>
          <button class="redetect-btn" onclick={resetBazaar} type="button">Re-enter</button>
        {:else}
          <div class="locate-bar" class:locate-bar-invalid={bazaarInvalid}>
            <button class="locate-browse" onclick={pickGamePath} type="button" disabled={bazaarChecking}>Browse</button>
            <div class="locate-input-wrap">
              <input
                bind:value={customGamePath}
                class="path-input"
                placeholder="Game install path…"
                type="text"
                spellcheck="false"
                onkeydown={(e) => e.key === 'Enter' && checkPath()}
                oninput={() => { bazaarInvalid = false; }}
              />
            </div>
            <button
              class="locate-confirm"
              onclick={checkPath}
              type="button"
              disabled={!hasPath || bazaarChecking}
            >
              {#if bazaarChecking}
                <span class="spinner" aria-hidden="true"></span>
              {:else}
                检查
              {/if}
            </button>
          </div>
          {#if bazaarInvalid}
            <p class="locate-error">未在该目录中找到 TheBazaar — 请确认路径是否正确</p>
          {/if}
        {/if}
      </div>
    </div>

    <!-- Step 4: Install -->
    <div class="step step-install">
      <div class="step-index" aria-hidden="true">IV</div>
      <div class="step-body">
        <span class="step-title">
          BepInEx
          {#if modInstalled}
            <span class="tag tag-ok">
              Installed{env?.bpp_version ? ` · v${env.bpp_version}` : ''}
            </span>
          {/if}
        </span>
        <button
          class="install-btn"
          class:installing
          disabled={!canInstall}
          onclick={install}
          type="button"
        >
          {#if installing}
            <span class="spinner dark" aria-hidden="true"></span>
            Binding the Sigil…
          {:else if modInstalled}
            ✦ Rebind the Rite
          {:else}
            ✦ Begin the Rite
          {/if}
        </button>
      </div>
    </div>
  </div>

  <footer class="footer" aria-hidden="true">
    <div class="rule"><span></span><span class="diamond small">◇</span><span></span></div>
    <p>BazaarPlusPlus · Arcane Foundry</p>
  </footer>
</main>

<style>
  :global(*, *::before, *::after) { box-sizing: border-box; }

  :global(html) {
    height: 100%;
    overflow: hidden;
    overscroll-behavior: none;
  }

  :global(body) {
    margin: 0;
    height: 100%;
    overflow-y: auto;
    overscroll-behavior: none;
    background-color: #0b0906;
    background-image:
      radial-gradient(ellipse 70% 42% at 50% -4%, rgba(200, 130, 40, 0.18) 0%, transparent 70%),
      radial-gradient(ellipse 40% 28% at 80% 85%, rgba(120, 60, 20, 0.1) 0%, transparent 60%);
    color: #e8dcc8;
    font-family: 'IM Fell English', Georgia, serif;
    -webkit-font-smoothing: antialiased;
    user-select: none;
  }

  .grain {
    position: fixed;
    inset: 0;
    pointer-events: none;
    z-index: 100;
    opacity: 0.025;
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='300' height='300'%3E%3Cfilter id='g'%3E%3CfeTurbulence type='turbulence' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='300' height='300' filter='url(%23g)'/%3E%3C/svg%3E");
    background-repeat: repeat;
  }

  .shell {
    width: 100%;
    max-width: 620px;
    margin: 0 auto;
    padding: 2rem 1.5rem 2.5rem;
    display: grid;
    gap: 1.25rem;
    animation: fade-up 0.5s ease both;
  }

  @keyframes fade-up {
    from { opacity: 0; transform: translateY(14px); }
    to   { opacity: 1; transform: translateY(0); }
  }

  /* ── Header ──────────────────────────────────────────────── */
  .header {
    position: relative;
    text-align: center;
    padding: 2.25rem 2.5rem 1.75rem;
    background: linear-gradient(175deg, rgba(38, 23, 9, 0.92), rgba(16, 10, 5, 0.88));
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 3px;
    box-shadow: 0 0 0 1px rgba(200, 148, 55, 0.06) inset, 0 24px 64px rgba(0,0,0,0.5);
    display: grid;
    gap: 0.25rem;
    justify-items: center;
  }

  .corner { position: absolute; color: rgba(200, 148, 55, 0.42); }
  .tl { top: 10px; left: 10px; }
  .tr { top: 10px; right: 10px; }

  .sigil {
    color: rgba(205, 150, 60, 0.65);
    margin-bottom: 0.5rem;
    animation: slow-spin 45s linear infinite;
    filter: drop-shadow(0 0 7px rgba(205, 150, 60, 0.22));
  }
  @keyframes slow-spin {
    from { transform: rotate(0deg); }
    to   { transform: rotate(360deg); }
  }

  .kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.56rem;
    letter-spacing: 0.38em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.55);
  }

  h1 {
    margin: 0.2rem 0 0;
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.5rem, 5vw, 2.5rem);
    font-weight: 700;
    line-height: 1;
    background: linear-gradient(155deg, #e8c87a 0%, #bf852e 55%, #e8c87a 100%);
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
    filter: drop-shadow(0 2px 10px rgba(205, 150, 60, 0.28));
  }

  .subtitle {
    margin: 0.4rem 0 0.75rem;
    font-family: 'IM Fell English', serif;
    font-style: italic;
    font-size: 0.88rem;
    color: rgba(200, 170, 120, 0.55);
  }

  .rule {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 0.65rem;
    color: rgba(200, 148, 55, 0.35);
  }
  .rule span:first-child,
  .rule span:last-child {
    flex: 1;
    height: 1px;
    background: linear-gradient(90deg, transparent, rgba(200, 148, 55, 0.3) 40%, rgba(200, 148, 55, 0.3) 60%, transparent);
  }
  .diamond { font-size: 0.55rem; color: rgba(205, 150, 60, 0.55); }
  .diamond.small { font-size: 0.42rem; }

  /* ── Steps ───────────────────────────────────────────────── */
  .steps {
    display: grid;
    gap: 0.6rem;
  }

  .step {
    display: flex;
    gap: 1rem;
    align-items: flex-start;
    padding: 1.1rem 1.3rem;
    background: rgba(18, 11, 5, 0.88);
    border: 1px solid rgba(180, 130, 48, 0.13);
    border-radius: 3px;
    box-shadow: 0 6px 28px rgba(0,0,0,0.35);
    transition: border-color 0.3s ease, box-shadow 0.3s ease;
  }

  .step-found {
    border-color: rgba(90, 200, 130, 0.25);
    box-shadow: 0 6px 28px rgba(0,0,0,0.35), 0 0 18px rgba(90, 200, 130, 0.05);
  }

  .step-warn {
    border-color: rgba(205, 150, 60, 0.3);
  }

  .step-install {
    margin-top: 0.4rem;
  }

  .step-index {
    font-family: 'Cinzel', serif;
    font-size: 0.55rem;
    letter-spacing: 0.15em;
    color: rgba(200, 148, 55, 0.4);
    padding-top: 0.25rem;
    flex-shrink: 0;
    width: 1.4rem;
    text-align: center;
  }

  .step-body {
    flex: 1;
    display: grid;
    gap: 0.7rem;
    min-width: 0;
    overflow: hidden;
  }

  .step-title {
    font-family: 'Cinzel', serif;
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(220, 195, 145, 0.8);
    display: flex;
    align-items: center;
    gap: 0.65rem;
    overflow: hidden;
  }

  .tag {
    font-family: 'Fira Code', monospace;
    font-size: 0.65rem;
    letter-spacing: 0;
    text-transform: none;
    padding: 0.18rem 0.55rem;
    border-radius: 2px;
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .tag-ok   { background: rgba(80, 180, 120, 0.15); color: #6dd9a0; border: 1px solid rgba(80, 180, 120, 0.25); }
  .tag-warn { background: rgba(200, 140, 50, 0.12); color: #c4923a; border: 1px solid rgba(200, 140, 50, 0.22); }

  .detail-line {
    margin: 0;
    min-width: 0;
  }

  .detail-path {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    user-select: text;
    font-family: 'Fira Code', monospace;
    font-size: 0.73rem;
    color: rgba(228, 216, 191, 0.82);
  }

  .detail-muted {
    font-size: 0.8rem;
    color: rgba(200, 170, 120, 0.6);
  }

  /* ── Bar Button ──────────────────────────────────────────── */
  .bar-btn {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.72rem 1rem;
    background: rgba(200, 148, 55, 0.04);
    border: 1px dashed rgba(200, 148, 55, 0.25);
    border-radius: 2px;
    color: rgba(200, 165, 100, 0.55);
    font-family: 'IM Fell English', serif;
    font-style: italic;
    font-size: 0.88rem;
    cursor: pointer;
    transition: all 0.18s ease;
    text-align: left;
  }

  .bar-btn:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.08);
    border-color: rgba(200, 148, 55, 0.45);
    color: rgba(220, 185, 120, 0.85);
    border-style: solid;
  }

  .bar-btn:disabled {
    cursor: default;
  }

  .bar-btn.bar-btn-loading {
    border-style: solid;
    border-color: rgba(200, 148, 55, 0.3);
    color: rgba(200, 165, 100, 0.65);
  }

  .bar-dash {
    font-style: normal;
    font-size: 1.1rem;
    line-height: 1;
    color: rgba(200, 148, 55, 0.45);
    letter-spacing: -0.05em;
  }

  /* ── Locate Bar ──────────────────────────────────────────── */
  .locate-bar {
    display: flex;
    align-items: stretch;
    border: 1px solid rgba(180, 130, 48, 0.2);
    border-radius: 2px;
    overflow: hidden;
    background: rgba(6, 4, 2, 0.85);
    transition: border-color 0.18s ease;
  }

  .locate-bar:focus-within {
    border-color: rgba(200, 148, 55, 0.4);
  }

  .locate-browse {
    flex-shrink: 0;
    padding: 0.68rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.7);
    background: rgba(200, 148, 55, 0.06);
    border: none;
    border-right: 1px solid rgba(180, 130, 48, 0.18);
    cursor: pointer;
    transition: background 0.15s ease, color 0.15s ease;
    white-space: nowrap;
  }

  .locate-browse:hover {
    background: rgba(200, 148, 55, 0.12);
    color: rgba(220, 180, 100, 0.9);
  }

  .locate-input-wrap {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    padding: 0 0.75rem;
  }

  .path-input {
    width: 100%;
    background: none;
    border: none;
    outline: none;
    color: rgba(225, 210, 185, 0.88);
    font-family: 'Fira Code', monospace;
    font-size: 0.78rem;
    min-width: 0;
    user-select: text;
  }

  .path-input::placeholder {
    color: rgba(150, 120, 75, 0.35);
    font-style: italic;
  }

  .locate-confirm {
    flex-shrink: 0;
    padding: 0.68rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.7);
    background: rgba(200, 148, 55, 0.06);
    border: none;
    border-left: 1px solid rgba(180, 130, 48, 0.18);
    cursor: pointer;
    transition: background 0.15s ease, color 0.15s ease;
    white-space: nowrap;
  }

  .locate-confirm:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.14);
    color: rgba(220, 180, 100, 0.9);
  }

  .locate-confirm:disabled {
    opacity: 0.3;
    cursor: not-allowed;
  }

  .locate-bar-invalid {
    border-color: rgba(200, 80, 60, 0.45) !important;
  }

  .locate-error {
    margin: 0;
    font-family: 'Fira Code', monospace;
    font-size: 0.72rem;
    color: rgba(220, 100, 80, 0.8);
    animation: fade-up 0.2s ease both;
  }

  /* ── Buttons ─────────────────────────────────────────────── */
  button { cursor: pointer; border: none; outline: none; font: inherit; }

  .redetect-btn {
    align-self: start;
    padding: 0.38rem 0.8rem;
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(160, 120, 55, 0.55);
    border: 1px solid rgba(160, 120, 55, 0.18);
    border-radius: 2px;
    background: none;
    transition: all 0.15s ease;
  }

  .redetect-btn:hover {
    color: rgba(200, 160, 80, 0.8);
    border-color: rgba(200, 148, 55, 0.35);
  }

  .install-btn {
    width: 100%;
    padding: 0.95rem 1rem;
    font-family: 'Cinzel', serif;
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: #18100400;
    background: linear-gradient(135deg, #d4a040 0%, #9e5c1e 50%, #d4a040 100%);
    background-size: 200% 100%;
    border: 1px solid rgba(210, 158, 60, 0.45);
    border-radius: 2px;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.14) inset, 0 4px 22px rgba(170, 100, 25, 0.3);
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.6rem;
    position: relative;
    overflow: hidden;
    transition: all 0.22s ease;
    color: #1c0e03;
  }

  .install-btn::before {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(180deg, rgba(255, 218, 128, 0.16) 0%, transparent 55%);
    pointer-events: none;
  }

  .install-btn:hover:not(:disabled) {
    background-position: 100% 0;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.2) inset, 0 6px 30px rgba(170, 100, 25, 0.5), 0 0 44px rgba(205, 150, 60, 0.15);
    transform: translateY(-1px);
  }

  .install-btn:disabled {
    opacity: 0.32;
    cursor: not-allowed;
  }

  .install-btn.installing {
    background-position: 100% 0;
    animation: shimmer 1.8s ease-in-out infinite;
    pointer-events: none;
  }

  @keyframes shimmer {
    0%, 100% { background-position: 0% 0; }
    50%       { background-position: 100% 0; }
  }

  /* ── Spinner ─────────────────────────────────────────────── */
  .spinner {
    display: inline-block;
    width: 11px;
    height: 11px;
    border: 1.5px solid rgba(200, 165, 100, 0.25);
    border-top-color: rgba(200, 165, 100, 0.75);
    border-radius: 50%;
    animation: spin 0.75s linear infinite;
    flex-shrink: 0;
  }
  .spinner.dark {
    border-color: rgba(30, 15, 4, 0.25);
    border-top-color: rgba(30, 15, 4, 0.7);
  }
  @keyframes spin { to { transform: rotate(360deg); } }

  /* ── Footer ──────────────────────────────────────────────── */
  .footer {
    text-align: center;
    display: grid;
    gap: 0.6rem;
  }
  .footer p {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(140, 110, 60, 0.35);
  }

  /* ── Responsive ──────────────────────────────────────────── */
  @media (max-width: 520px) {
    .shell { padding: 1.25rem 1rem 2rem; }
    .header { padding: 1.75rem 1.25rem 1.5rem; }
  }
</style>
