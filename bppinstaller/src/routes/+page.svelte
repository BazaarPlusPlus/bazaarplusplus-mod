<script lang="ts">
  import { invoke } from '@tauri-apps/api/core';
  import { open } from '@tauri-apps/plugin-dialog';
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { onMount } from 'svelte';
  import { formatMessage, messages, resolveInitialLocale, type Locale } from '$lib/i18n';
  import type { DotnetInfo, EnvironmentInfo } from '$lib/types';

  type StepState = 'idle' | 'detecting' | 'found' | 'not_found';

  let env: EnvironmentInfo | null = null;
  let dotnetState: StepState = 'idle';
  let bazaarFound = false;
  let bazaarChecking = false;
  let bazaarInvalid = false;
  let customGamePath = '';
  let actionBusy: 'idle' | 'detect' | 'install' | 'uninstall' = 'idle';
  let actionMenuOpen = false;
  let locale: Locale = 'zh';

  $: t = (key: keyof typeof messages.en, params?: Record<string, string | number>): string =>
    formatMessage(locale, key, params);

  function effectiveGamePath(): string {
    return customGamePath || env?.game_path || '';
  }

  function applyLocale(nextLocale: Locale) {
    locale = nextLocale;

    if (typeof document !== 'undefined') {
      document.documentElement.lang = messages[nextLocale].htmlLang;
    }

    if (typeof window !== 'undefined') {
      window.localStorage.setItem('locale', nextLocale);
    }
  }

  function toggleLocale() {
    applyLocale(locale === 'zh' ? 'en' : 'zh');
  }

  function handleLocaleToggle(event: MouseEvent) {
    event.preventDefault();
    event.stopPropagation();
    toggleLocale();
  }

  async function verifyGamePath(path: string) {
    return invoke<boolean>('verify_game_path', { path });
  }

  async function detectDotnetRuntime() {
    try {
      const result = await invoke<DotnetInfo>('detect_dotnet_runtime');
      env = env
        ? { ...env, ...result }
        : {
            steam_path: null,
            game_path: null,
            bpp_version: null,
            bepinex_installed: false,
            ...result
          };
      dotnetState = result.dotnet_ok ? 'found' : 'not_found';
    } catch {
      dotnetState = 'idle';
    }
  }

  async function detectEnvironment() {
    if (actionBusy !== 'idle') return;

    actionBusy = 'detect';
    dotnetState = 'detecting';
    const dotnetPromise = detectDotnetRuntime();

    try {
      env = await invoke<EnvironmentInfo>('detect_environment');

      if (env.game_path && !customGamePath) {
        bazaarFound = await verifyGamePath(env.game_path);
        bazaarInvalid = !bazaarFound;
      } else if (!customGamePath) {
        bazaarFound = false;
        bazaarInvalid = false;
      }

      await dotnetPromise;
    } catch {
      dotnetState = 'idle';
    } finally {
      actionBusy = 'idle';
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
      bazaarFound = await verifyGamePath(path);
      if (!bazaarFound) {
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
    customGamePath = '';
  }

  async function refreshAfterAction() {
    actionBusy = 'idle';
    await detectEnvironment();
  }

  async function installBundled() {
    if (!canInstall) return;

    actionBusy = 'install';
    try {
      await invoke('install_bepinex', { gamePath: effectiveGamePath() });
      await invoke('patch_launch_options', {
        steamPath: env!.steam_path,
        gamePath: effectiveGamePath()
      });
      await refreshAfterAction();
    } catch (e) {
      console.error(e);
      actionBusy = 'idle';
    }
  }

  async function uninstallBpp() {
    if (!effectiveGamePath() || actionBusy !== 'idle') return;

    actionBusy = 'uninstall';
    actionMenuOpen = false;
    try {
      await invoke('uninstall_bpp', {
        steamPath: env?.steam_path ?? '',
        gamePath: effectiveGamePath()
      });
      await refreshAfterAction();
    } catch (e) {
      console.error(e);
      actionBusy = 'idle';
    }
  }

  $: hasPath = Boolean(customGamePath || env?.game_path);
  $: modInstalled = Boolean(env?.bpp_version);
  $: isBusy = actionBusy !== 'idle';
  $: canInstall = !isBusy && dotnetState !== 'idle' && bazaarFound && hasPath;
  $: dotnetDownloadUrl = locale === 'zh'
    ? 'https://dotnet.microsoft.com/zh-cn/download'
    : 'https://dotnet.microsoft.com/en-us/download';
  $: localeBadge = locale === 'zh' ? '中' : 'EN';
  $: localeButtonLabel = locale === 'zh' ? 'Switch to English' : '切换到中文';

  onMount(() => {
    applyLocale(resolveInitialLocale());
    void detectEnvironment();
  });
</script>

<svelte:head>
  <title>{t('pageTitle')}</title>
</svelte:head>

<main class="shell">
  <header class="header">
    <div class="corner tl" aria-hidden="true">
      <svg width="28" height="28" viewBox="0 0 40 40" fill="none">
        <path d="M2 2L2 16M2 2L16 2" stroke="currentColor" stroke-width="1.5" stroke-linecap="square" />
        <circle cx="2" cy="2" r="1.5" fill="currentColor" />
      </svg>
    </div>
    <div class="corner tr" aria-hidden="true">
      <svg width="28" height="28" viewBox="0 0 40 40" fill="none">
        <path d="M38 2L38 16M38 2L24 2" stroke="currentColor" stroke-width="1.5" stroke-linecap="square" />
        <circle cx="38" cy="2" r="1.5" fill="currentColor" />
      </svg>
    </div>

    <a class="about-toggle" href="/about" title={t('aboutLabel')}>
      <svg class="about-icon" viewBox="0 0 24 24" aria-hidden="true">
        <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="1.5" fill="none" />
        <path d="M12 11v5M12 8h.01" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
      </svg>
    </a>

    <button
      class="locale-toggle"
      onclick={handleLocaleToggle}
      type="button"
      aria-label={localeButtonLabel}
      title={localeButtonLabel}
    >
      <svg class="locale-icon" viewBox="0 0 24 24" aria-hidden="true">
        <path
          d="M12 3a9 9 0 1 0 9 9a9 9 0 0 0-9-9Zm5.9 8h-2.2a14.3 14.3 0 0 0-1.2-4A7.1 7.1 0 0 1 17.9 11Zm-5.9-5.8c.7.9 1.7 2.9 2.1 5.8H9.9c.4-2.9 1.4-4.9 2.1-5.8ZM6.5 7a14.3 14.3 0 0 0-1.2 4H3.1A7.1 7.1 0 0 1 6.5 7ZM3.1 13h2.2a14.3 14.3 0 0 0 1.2 4A7.1 7.1 0 0 1 3.1 13Zm8.9 5.8c-.7-.9-1.7-2.9-2.1-5.8h4.2c-.4 2.9-1.4 4.9-2.1 5.8Zm2.5-1.8a14.3 14.3 0 0 0 1.2-4h2.2a7.1 7.1 0 0 1-3.4 4Zm-8.1-4c.1 1.4.4 2.7.9 4A12.4 12.4 0 0 1 5.9 13Zm.8-2a12.4 12.4 0 0 1 1.4-4c-.5 1.3-.8 2.6-.9 4Zm8.8 0c-.1-1.4-.4-2.7-.9-4a12.4 12.4 0 0 1 1.4 4Zm-1.4 2c.5-1.3.8-2.6.9-4a12.4 12.4 0 0 1-1.4 4Z"
          fill="currentColor"
        />
      </svg>
      <span class="locale-badge">{localeBadge}</span>
    </button>

    <div class="sigil" aria-hidden="true">
      <svg width="32" height="32" viewBox="0 0 44 44" fill="none">
        <polygon points="22,3 41,34 3,34" stroke="currentColor" stroke-width="1" fill="none" opacity="0.55" />
        <polygon points="22,11 35,31 9,31" stroke="currentColor" stroke-width="0.5" fill="none" opacity="0.3" />
        <circle cx="22" cy="22" r="5" stroke="currentColor" stroke-width="0.8" fill="none" />
        <circle cx="22" cy="22" r="2" fill="currentColor" opacity="0.75" />
      </svg>
    </div>

    <p class="kicker">{t('kicker')}</p>
    <h1>BazaarPlusPlus</h1>
    <p class="subtitle"><em>{t('subtitle')}</em></p>

    <div class="rule" aria-hidden="true">
      <span></span><span class="diamond">+</span><span></span>
    </div>
  </header>

  <div class="steps">
    <div class="step" class:step-found={modInstalled}>
      <div class="step-index" aria-hidden="true">I</div>
      <div class="step-body">
        <span class="step-title">
          {t('stepBpp')}
          {#if modInstalled}
            <span class="tag tag-ok">{t('statusInstalled')}{env?.bpp_version ? ` · v${env.bpp_version}` : ''}</span>
          {:else if actionBusy === 'detect'}
            <span class="tag">{t('statusChecking')}</span>
          {:else}
            <span class="tag tag-warn">{t('statusNotInstalled')}</span>
          {/if}
        </span>

        {#if !modInstalled}
          <p class="detail-line detail-muted">{t('detectInstalledHint')}</p>
        {/if}
      </div>
    </div>

    <div class="step" class:step-found={dotnetState === 'found'} class:step-warn={dotnetState === 'not_found'}>
      <div class="step-index" aria-hidden="true">II</div>
      <div class="step-body">
        <span class="step-title">
          {t('stepDotnet')}
          {#if dotnetState === 'found'}
            <span class="tag tag-ok">{env?.dotnet_version ?? 'OK'}</span>
          {:else if dotnetState === 'not_found'}
            <span class="tag tag-warn">{t('statusRuntimeMissing')}</span>
          {/if}
        </span>

        {#if dotnetState === 'found'}
          <p class="detail-line detail-muted">{t('runtimeCompatible')}</p>
        {:else if dotnetState === 'not_found'}
          <p class="detail-line detail-muted">{t('runtimeNotFound')}</p>
          <button class="dotnet-download-btn" onclick={() => openUrl(dotnetDownloadUrl)} type="button">
            {t('runtimeDownload')}
          </button>
        {:else if dotnetState === 'idle'}
          <p class="detail-line detail-muted">{t('runtimeIdle')}</p>
        {/if}
      </div>
    </div>

    <div class="step" class:step-found={bazaarFound}>
      <div class="step-index" aria-hidden="true">III</div>
      <div class="step-body">
        <span class="step-title">
          {t('stepBazaar')}
          {#if bazaarFound}
            <span class="tag tag-ok">{t('statusFound')}</span>
          {/if}
        </span>

        {#if bazaarFound}
          <p class="detail-line detail-path" title={effectiveGamePath()}>{effectiveGamePath()}</p>
          <button class="redetect-btn" onclick={resetBazaar} type="button">{t('actionReenter')}</button>
        {:else}
          <div class="locate-bar" class:locate-bar-invalid={bazaarInvalid}>
            <button class="locate-browse" onclick={pickGamePath} type="button" disabled={bazaarChecking}>
              {t('actionBrowse')}
            </button>
            <div class="locate-input-wrap">
              <input
                bind:value={customGamePath}
                class="path-input"
                placeholder={t('placeholderGamePath')}
                type="text"
                spellcheck="false"
                onkeydown={(e) => e.key === 'Enter' && checkPath()}
                oninput={() => {
                  bazaarInvalid = false;
                }}
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
                {t('actionCheck')}
              {/if}
            </button>
          </div>
          {#if bazaarInvalid}
            <p class="locate-error">{t('errorGamePath')}</p>
          {/if}
        {/if}
      </div>
    </div>

    <div class="step step-install">
      <div class="step-index" aria-hidden="true">IV</div>
      <div class="step-body">
        <span class="step-title">{t('stepActions')}</span>
        <div class="action-row">
          <button class="secondary-btn detect-btn" onclick={detectEnvironment} type="button" disabled={isBusy}>
            {#if actionBusy === 'detect'}
              <span class="spinner" aria-hidden="true"></span>
              {t('actionDetecting')}
            {:else}
              {t('actionDetect')}
            {/if}
          </button>

          <div class="action-primary">
            <button class="install-btn" disabled={!canInstall} onclick={installBundled} type="button">
              {#if actionBusy === 'install'}
                <span class="spinner dark" aria-hidden="true"></span>
                {t('actionInstalling')}
              {:else if modInstalled}
                ✦ {t('actionReinstall')}
              {:else}
                ✦ {t('actionInstall')}
              {/if}
            </button>

            <div class="menu-wrap">
              <button
                class="secondary-btn menu-trigger"
                type="button"
                onclick={() => {
                  actionMenuOpen = !actionMenuOpen;
                }}
                disabled={isBusy}
                aria-expanded={actionMenuOpen}
              >
                ...
              </button>
              {#if actionMenuOpen}
                <div class="action-menu">
                  <button class="menu-item" type="button" onclick={uninstallBpp} disabled={isBusy}>
                    {#if actionBusy === 'uninstall'}
                      {t('actionUninstalling')}
                    {:else}
                      {t('actionUninstall')}
                    {/if}
                  </button>
                </div>
              {/if}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <footer class="footer" aria-hidden="true">
    <div class="rule"><span></span><span class="diamond small">+</span><span></span></div>
    <p>{t('footer')}</p>
  </footer>
</main>

<style>
  .shell {
    width: 100%;
    max-width: 560px;
    margin: 0 auto;
    padding: 1.25rem 1rem 1.75rem;
    display: grid;
    gap: 0.85rem;
    animation: fade-up 0.5s ease both;
  }

  @keyframes fade-up {
    from { opacity: 0; transform: translateY(14px); }
    to   { opacity: 1; transform: translateY(0); }
  }

  .header {
    position: relative;
    text-align: center;
    padding: 1.45rem 1.75rem 1.15rem;
    background: linear-gradient(175deg, rgba(38, 23, 9, 0.92), rgba(16, 10, 5, 0.88));
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 3px;
    box-shadow: 0 0 0 1px rgba(200, 148, 55, 0.06) inset, 0 24px 64px rgba(0,0,0,0.5);
    display: grid;
    gap: 0.15rem;
    justify-items: center;
  }

  .corner { position: absolute; color: rgba(200, 148, 55, 0.42); }
  .tl { top: 8px; left: 8px; }
  .tr { top: 8px; right: 8px; }

  .about-toggle {
    position: absolute;
    top: 0.9rem;
    left: 0.9rem;
    width: 2rem;
    height: 2rem;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: 1px solid rgba(200, 148, 55, 0.24);
    border-radius: 2px;
    background: linear-gradient(180deg, rgba(200, 148, 55, 0.12), rgba(200, 148, 55, 0.06));
    color: rgba(228, 216, 191, 0.82);
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    z-index: 2;
    text-decoration: none;
    transition: background 0.15s ease, border-color 0.15s ease;
  }

  .about-toggle:hover {
    background: linear-gradient(180deg, rgba(200, 148, 55, 0.2), rgba(200, 148, 55, 0.1));
    border-color: rgba(200, 148, 55, 0.4);
  }

  .about-toggle:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .about-icon {
    width: 1rem;
    height: 1rem;
    opacity: 0.9;
  }

  .locale-toggle {
    position: absolute;
    top: 0.9rem;
    right: 0.9rem;
    min-width: 3.2rem;
    height: 2rem;
    padding: 0.3rem 0.55rem;
    border: 1px solid rgba(200, 148, 55, 0.24);
    border-radius: 2px;
    background: linear-gradient(180deg, rgba(200, 148, 55, 0.12), rgba(200, 148, 55, 0.06));
    color: rgba(228, 216, 191, 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.35rem;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    z-index: 2;
  }

  .locale-toggle:hover {
    background: linear-gradient(180deg, rgba(200, 148, 55, 0.2), rgba(200, 148, 55, 0.1));
    border-color: rgba(200, 148, 55, 0.4);
  }

  .locale-toggle:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .locale-icon {
    width: 0.9rem;
    height: 0.9rem;
    flex-shrink: 0;
    opacity: 0.9;
  }

  .locale-badge {
    min-width: 1.1rem;
    text-align: center;
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    letter-spacing: 0.05em;
  }

  .sigil {
    color: rgba(205, 150, 60, 0.65);
    margin-bottom: 0.2rem;
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
    font-size: 0.5rem;
    letter-spacing: 0.38em;
    text-transform: uppercase;
    color: rgba(205, 150, 60, 0.55);
  }

  h1 {
    margin: 0.1rem 0 0;
    font-family: 'Cinzel Decorative', serif;
    font-size: clamp(1.35rem, 4.2vw, 2.1rem);
    font-weight: 700;
    line-height: 1;
    background: linear-gradient(155deg, #e8c87a 0%, #bf852e 55%, #e8c87a 100%);
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
    filter: drop-shadow(0 2px 10px rgba(205, 150, 60, 0.28));
  }

  .subtitle {
    margin: 0.22rem 0 0.5rem;
    font-family: 'IM Fell English', serif;
    font-style: italic;
    font-size: 0.78rem;
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

  .steps {
    display: grid;
    gap: 0.5rem;
  }

  .step {
    display: flex;
    gap: 1rem;
    align-items: flex-start;
    padding: 0.95rem 1.05rem;
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
    margin-top: 0.2rem;
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
    overflow: visible;
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

  .locate-bar {
    display: flex;
    align-items: center;
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

  button { cursor: pointer; border: none; font: inherit; }

  button:focus-visible,
  .path-input:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .secondary-btn {
    padding: 0.68rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.78);
    background: rgba(200, 148, 55, 0.06);
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 2px;
    transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease;
    white-space: nowrap;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.45rem;
  }

  .secondary-btn:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.12);
    color: rgba(220, 180, 100, 0.95);
    border-color: rgba(200, 148, 55, 0.34);
  }

  .secondary-btn:disabled {
    opacity: 0.35;
    cursor: not-allowed;
  }

  .action-row {
    display: flex;
    align-items: stretch;
    gap: 0.75rem;
  }

  .detect-btn {
    flex: 0 0 auto;
    min-width: 96px;
  }

  .action-primary {
    flex: 1;
    min-width: 0;
    display: flex;
    gap: 0.5rem;
    position: relative;
  }

  .menu-wrap {
    position: relative;
    flex-shrink: 0;
  }

  .menu-trigger {
    min-width: 42px;
    height: 100%;
    padding-left: 0.7rem;
    padding-right: 0.7rem;
  }

  .action-menu {
    position: absolute;
    right: 0;
    top: calc(100% + 0.35rem);
    min-width: 140px;
    padding: 0.35rem;
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 3px;
    background: rgba(18, 11, 5, 0.96);
    box-shadow: 0 12px 30px rgba(0,0,0,0.35);
    z-index: 20;
  }

  .menu-item {
    width: 100%;
    text-align: left;
    padding: 0.62rem 0.7rem;
    border-radius: 2px;
    background: transparent;
    color: rgba(228, 216, 191, 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
  }

  .menu-item:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.1);
  }

  .menu-item:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

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

  .dotnet-download-btn {
    align-self: start;
    margin-top: 0.25rem;
    padding: 0.38rem 0.8rem;
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(100, 160, 220, 0.78);
    border: 1px solid rgba(100, 160, 220, 0.22);
    border-radius: 2px;
    background: rgba(100, 160, 220, 0.08);
    transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease;
  }

  .dotnet-download-btn:hover {
    color: rgba(130, 190, 240, 0.95);
    background: rgba(100, 160, 220, 0.14);
    border-color: rgba(100, 160, 220, 0.38);
  }

  .install-btn {
    width: 100%;
    padding: 0.95rem 1rem;
    font-family: 'Cinzel', serif;
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: #1c0e03;
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

  .footer {
    text-align: center;
    display: grid;
    gap: 0.4rem;
  }

  .footer p {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(140, 110, 60, 0.35);
  }

  @media (max-width: 520px) {
    .shell { padding: 1rem 0.85rem 1.5rem; }
    .header { padding: 1.2rem 1rem 1rem; }
    .action-row,
    .action-primary {
      flex-direction: column;
    }

    .detect-btn,
    .menu-trigger {
      width: 100%;
    }

    .locale-toggle {
      top: 0.7rem;
      right: 0.7rem;
    }

    .about-toggle {
      top: 0.7rem;
      left: 0.7rem;
    }
  }
</style>
