<script lang="ts">
  import { invoke } from '@tauri-apps/api/core';
  import { getVersion } from '@tauri-apps/api/app';
  import { open } from '@tauri-apps/plugin-dialog';
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { onMount } from 'svelte';
  import AppModal from '$lib/components/AppModal.svelte';
  import InstallerUpdateHighlights from '$lib/components/InstallerUpdateHighlights.svelte';
  import type { DotnetInfo, EnvironmentInfo } from '$lib/types';
  import { locale, handleLocaleToggle } from '$lib/locale';
  import { formatMessage, messages } from '$lib/i18n';

  type StepState = 'idle' | 'detecting' | 'found' | 'not_found';
  const CUSTOM_GAME_PATH_STORAGE_KEY = 'bppinstaller:custom-game-path';

  function loadPersistedCustomGamePath(): string {
    if (typeof window === 'undefined') return '';
    return window.localStorage.getItem(CUSTOM_GAME_PATH_STORAGE_KEY)?.trim() ?? '';
  }

  function persistCustomGamePath(path: string) {
    if (typeof window === 'undefined') return;
    const normalizedPath = path.trim();
    if (normalizedPath) {
      window.localStorage.setItem(CUSTOM_GAME_PATH_STORAGE_KEY, normalizedPath);
      return;
    }
    window.localStorage.removeItem(CUSTOM_GAME_PATH_STORAGE_KEY);
  }

  function selectedGamePath(): string | null {
    const path = customGamePath.trim();
    return path ? path : null;
  }

  let env: EnvironmentInfo | null = null;
  let dotnetState: StepState = 'idle';
  let bazaarFound = false;
  let bazaarChecking = false;
  let bazaarInvalid = false;
  let customGamePath = loadPersistedCustomGamePath();
  let actionBusy: 'idle' | 'detect' | 'install' | 'uninstall' = 'idle';
  let actionMenuOpen = false;
  const STEAM_BAZAAR_URL = 'steam://rungameid/1617400';
  const BILIBILI_URL = 'https://space.bilibili.com/3546978457750467';
  let showInstallModal = false;
  let showUpdatedInstallerModal = false;
  let pendingReinstallAfterUpdate = false;
  let installAcknowledged = false;
  const APP_VERSION_STORAGE_KEY = 'bppinstaller:last-seen-app-version';
  const installDebugMode =
    import.meta.env.DEV &&
    typeof window !== 'undefined' &&
    new URLSearchParams(window.location.search).get('debug-install') === '1';

  $: t = (key: keyof typeof messages.en, params?: Record<string, string | number>): string =>
    formatMessage($locale, key, params);

  function hasTauriRuntime(): boolean {
    return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
  }

  const isDebugInstallPreview = installDebugMode && !hasTauriRuntime();

  function applyInstallDebugState() {
    env = {
      steam_path: 'C:\\Program Files (x86)\\Steam',
      game_path: 'C:\\Games\\The Bazaar',
      dotnet_version: '9.0.0',
      dotnet_ok: true,
      bepinex_installed: false,
      bpp_version: null,
      bundled_bpp_version: 'debug-preview'
    };
    dotnetState = 'found';
    bazaarFound = true;
    bazaarInvalid = false;
  }

  function effectiveGamePath(): string {
    return selectedGamePath() || env?.game_path || '';
  }

  function requestInstall() {
    if (!canInstall) return;
    installAcknowledged = false;
    showInstallModal = true;
  }

  async function confirmInstall() {
    if (!installAcknowledged) return;
    showInstallModal = false;
    await installBundled();
  }

  function acknowledgeUpdatedInstallerPrompt() {
    showUpdatedInstallerModal = false;
  }

  function reopenInstallFlowAfterUpdate() {
    showUpdatedInstallerModal = false;
    pendingReinstallAfterUpdate = true;
    void detectEnvironment();
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
            bundled_bpp_version: null,
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

    if (isDebugInstallPreview) {
      applyInstallDebugState();
      return;
    }

    actionBusy = 'detect';
    dotnetState = 'detecting';
    const dotnetPromise = detectDotnetRuntime();
    const requestedGamePath = selectedGamePath();

    try {
      env = requestedGamePath
        ? await invoke<EnvironmentInfo>('detect_environment', { gamePath: requestedGamePath })
        : await invoke<EnvironmentInfo>('detect_environment');

      if (requestedGamePath) {
        bazaarFound = await verifyGamePath(requestedGamePath);
        bazaarInvalid = !bazaarFound;
      } else if (env.game_path) {
        bazaarFound = await verifyGamePath(env.game_path);
        bazaarInvalid = !bazaarFound;
      } else {
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
      customGamePath = selected.trim();
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

    if (isDebugInstallPreview) {
      actionBusy = 'install';
      await new Promise((resolve) => window.setTimeout(resolve, 450));
      env = env ? { ...env, bpp_version: env.bundled_bpp_version ?? 'debug-preview' } : env;
      actionBusy = 'idle';
      return;
    }

    actionBusy = 'install';
    try {
      await invoke('install_bepinex', { gamePath: effectiveGamePath() });
      await invoke('patch_launch_options', {
        steamPath: env?.steam_path ?? '',
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

  async function launchGame() {
    if (!canLaunchGame) return;

    try {
      await openUrl(STEAM_BAZAAR_URL);
    } catch (e) {
      console.error(e);
    }
  }

  async function openBilibili(event?: MouseEvent) {
    event?.preventDefault();

    if (!hasTauriRuntime()) {
      if (typeof window !== 'undefined') {
        window.open(BILIBILI_URL, '_blank', 'noopener,noreferrer');
      }
      return;
    }

    try {
      await openUrl(BILIBILI_URL);
    } catch (e) {
      console.error(e);
    }
  }

  $: hasPath = Boolean(selectedGamePath() || env?.game_path);
  $: modInstalled = Boolean(env?.bpp_version);
  $: bundledBppVersion = env?.bundled_bpp_version ?? null;
  $: installedBppVersion = env?.bpp_version ?? null;
  $: versionMismatch =
    Boolean(modInstalled && bundledBppVersion && installedBppVersion && bundledBppVersion !== installedBppVersion);
  $: isBusy = actionBusy !== 'idle';
  $: installPrereqsMet = dotnetState !== 'idle' && bazaarFound && hasPath;
  $: canInstall = !isBusy && (installPrereqsMet || isDebugInstallPreview);
  $: canLaunchGame = !isBusy && bazaarFound;
  $: dotnetDownloadUrl = $locale === 'zh'
    ? 'https://dotnet.microsoft.com/zh-cn/download'
    : 'https://dotnet.microsoft.com/en-us/download';
  $: localeBadge = $locale === 'zh' ? '中' : 'EN';
  $: localeButtonLabel = $locale === 'zh' ? 'Switch to English' : '切换到中文';
  $: if (pendingReinstallAfterUpdate && canInstall) {
    pendingReinstallAfterUpdate = false;
    requestInstall();
  }
  $: persistCustomGamePath(customGamePath);

  onMount(() => {
    locale.init();
    void detectEnvironment();
    void detectUpdatedInstaller();
  });

  async function detectUpdatedInstaller() {
    if (!hasTauriRuntime()) return;

    try {
      const currentVersion = await getVersion();
      const lastSeenVersion = window.localStorage.getItem(APP_VERSION_STORAGE_KEY);

      if (lastSeenVersion && lastSeenVersion !== currentVersion) {
        showUpdatedInstallerModal = true;
      }

      window.localStorage.setItem(APP_VERSION_STORAGE_KEY, currentVersion);
    } catch (e) {
      console.error(e);
    }
  }
</script>

<svelte:head>
  <title>{t('pageTitle')}</title>
</svelte:head>

<main class="shell">
  <AppModal
    open={showInstallModal}
    eyebrow="BazaarPlusPlus"
    title={$locale === 'zh' ? '开始安装' : 'Install BazaarPlusPlus'}
    bodyClass="install-preview"
    confirmText={$locale === 'zh' ? '确认安装' : 'Install'}
    confirmDisabled={!installAcknowledged}
    onConfirm={confirmInstall}
  >
    <div class="feature-list">
      <article class="feature-card">
        <div class="feature-icon">I</div>
        <div class="feature-copy">
          <h3>{$locale === 'zh' ? '怪物预览增强' : 'Enhanced Monster Preview'}</h3>
          <p>
            {$locale === 'zh'
              ? '右键点击查看怪物棋盘与技能信息'
              : 'Right-click to inspect the monster board and skill details.'}
          </p>
        </div>
      </article>

      <article class="feature-card">
        <div class="feature-icon">II</div>
        <div class="feature-copy">
          <h3>{$locale === 'zh' ? '附魔预览增强' : 'Enhanced Enchantment Preview'}</h3>
          {#if $locale === 'zh'}
            <p>
              默认直接显示附魔效果预览
            </p>
          {:else}
            <p>
              Enchantment results are shown directly by default.
            </p>
          {/if}
        </div>
      </article>

      <article class="feature-card feature-card-wide">
        <div class="feature-icon">III</div>
        <div class="feature-copy">
          <h3>{$locale === 'zh' ? '战斗状态条' : 'Combat Status Bar'}</h3>
          {#if $locale === 'zh'}
            <p>
              可选功能，显示战斗时间、帧数和速度控制
            </p>
            <p class="feature-callout">
              <span class="feature-callout-line">
                首次使用可在 <span class="feature-emphasis">游戏内选项菜单中开启</span>
              </span>
              <span class="feature-callout-line">
                游戏内可按 <span class="feature-hotkey">F6</span> 快速切换显示
              </span>
            </p>
          {:else}
            <p>
              Optional feature showing battle time, frame count, and speed controls.
              <br />
              Launch the game once, enable it from the in-game options menu, then restart the game
              to apply. Press <span class="feature-hotkey">F6</span> in-game to toggle it quickly.
            </p>
            <p class="feature-callout">
              <span class="feature-callout-line">Enable it from the in-game options menu</span>
              <span class="feature-callout-line">
                Press <span class="feature-hotkey">F6</span> in-game to toggle it quickly
              </span>
            </p>
          {/if}
        </div>
      </article>
    </div>

    <label class="install-acknowledge">
      <input class="install-acknowledge-input" bind:checked={installAcknowledged} type="checkbox" />
      <span class="install-acknowledge-box" aria-hidden="true"></span>
      <span>
        {$locale === 'zh'
          ? '我已了解战斗状态条需在安装后前往游戏内选项菜单手动开启'
          : 'I understand that the combat status bar must be enabled later from the in-game options menu after installation.'}
      </span>
    </label>
  </AppModal>

  <AppModal
    open={showUpdatedInstallerModal}
    eyebrow="BazaarPlusPlus"
    title={$locale === 'zh' ? '安装器已更新' : 'Installer Updated'}
    wide={true}
    confirmText={$locale === 'zh' ? '重新安装' : 'Reinstall'}
    showCancel={true}
    cancelText={$locale === 'zh' ? '稍后' : 'Later'}
    onConfirm={reopenInstallFlowAfterUpdate}
    onCancel={acknowledgeUpdatedInstallerPrompt}
  >
    <InstallerUpdateHighlights />
    <a class="update-detail-link" href="/whats-new">
      {$locale === 'zh' ? '在独立页面查看这部分内容' : 'Open this section on a dedicated page'}
    </a>
  </AppModal>

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

    <div class="header-corner-links">
      <a class="about-toggle" href="/about" title={t('aboutLabel')}>
        <svg class="about-icon" viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="1.5" fill="none" />
          <path d="M12 11v5M12 8h.01" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
        </svg>
      </a>

      <a
        class="about-toggle social-toggle"
        href={BILIBILI_URL}
        rel="noreferrer"
        target="_blank"
        title={$locale === 'zh' ? '打开 Bilibili 主页' : 'Open Bilibili'}
        aria-label={$locale === 'zh' ? '打开 Bilibili 主页' : 'Open Bilibili'}
        onclick={openBilibili}
      >
        <svg class="about-icon" viewBox="0 0 24 24" aria-hidden="true">
          <rect x="4.5" y="7.5" width="15" height="10" rx="2.2" stroke="currentColor" stroke-width="1.5" fill="none" />
          <path d="M9 5.5L7.4 3.8M15 5.5l1.6-1.7M9 11.2h1.8M13.2 11.2H15M9.3 14.1c1 .7 4.4.7 5.4 0" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" fill="none" />
        </svg>
      </a>
    </div>

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

    <div class="header-links">
      <a class="header-link header-link-featured" href="/whats-new">
        <span class="header-link-kicker">{$locale === 'zh' ? '新版本' : 'Update'}</span>
        <span class="header-link-title">{$locale === 'zh' ? '查看 WhatsNew' : "Open What's New"}</span>
      </a>
    </div>

  </header>

  <div class="steps">
    <div class="step" class:step-found={modInstalled && !versionMismatch} class:step-error={versionMismatch}>
      <div class="step-index" aria-hidden="true">I</div>
      <div class="step-body step-body-bpp">
        <div class="step-bpp-content">
          <span class="step-title">
            {t('stepBpp')}
            {#if versionMismatch}
              <span class="tag tag-danger">{$locale === 'zh' ? '版本不一致' : 'Version mismatch'}</span>
            {:else if modInstalled}
              <span class="tag tag-ok">{t('statusInstalled')}{env?.bpp_version ? ` · v${env.bpp_version}` : ''}</span>
            {:else if actionBusy === 'detect'}
              <span class="tag">{t('statusChecking')}</span>
            {:else}
              <span class="tag tag-warn">{t('statusNotInstalled')}</span>
            {/if}
          </span>
          {#if versionMismatch}
            <div class="mismatch-summary">
              <p class="detail-line detail-muted">
                {$locale === 'zh'
                  ? '已安装版本和安装器内置版本不同，建议重新安装前先看一下本次更新内容。'
                  : 'The installed version differs from the bundled one. Check what changed before reinstalling.'}
              </p>
              <a class="mismatch-link" href="/whats-new">
                {$locale === 'zh' ? '查看更新内容' : "View what's new"}
              </a>
            </div>
            <div class="mismatch-versions">
              <span class="mismatch-version">
                <span class="mismatch-version-label">{$locale === 'zh' ? '已安装' : 'Installed'}</span>
                <span class="mismatch-version-value">v{installedBppVersion}</span>
              </span>
              <span class="mismatch-version">
                <span class="mismatch-version-label">{$locale === 'zh' ? '安装器内置' : 'Installer bundle'}</span>
                <span class="mismatch-version-value">v{bundledBppVersion}</span>
              </span>
            </div>
          {:else if modInstalled}
            <p class="detail-line detail-muted">{t('modInstalledHint')}</p>
            {#if bundledBppVersion}
              <p class="detail-line detail-faint">
                {$locale === 'zh' ? '安装器内置版本' : 'Installer bundle'}: v{bundledBppVersion}
              </p>
            {/if}
          {:else}
            <p class="detail-line detail-muted">{t('detectInstalledHint')}</p>
          {/if}
        </div>
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
            <button class="install-btn" class:install-btn-danger={versionMismatch} disabled={!canInstall} onclick={requestInstall} type="button">
              {#if actionBusy === 'install'}
                <span class="spinner dark" aria-hidden="true"></span>
                {t('actionInstalling')}
              {:else if versionMismatch}
                {$locale === 'zh' ? '⚠ 需要重新安装' : '⚠ Reinstall Required'}
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

        <button class="secondary-btn launch-btn" type="button" onclick={launchGame} disabled={!canLaunchGame}>
          {$locale === 'zh' ? '\u542f\u52a8\u6e38\u620f' : 'Launch Game'}
        </button>
      </div>
    </div>
  </div>

  <footer class="footer" aria-hidden="true">
    <div class="rule"><span></span><span class="diamond small">+</span><span></span></div>
    <p>{t('footer')}</p>
  </footer>
</main>

<style>
  .feature-list {
    display: grid;
    gap: 0.7rem;
    text-align: left;
  }

  .feature-card {
    display: grid;
    grid-template-columns: 2.25rem 1fr;
    gap: 0.8rem;
    align-items: start;
    padding: 0.9rem;
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 3px;
    background:
      linear-gradient(180deg, rgba(200, 148, 55, 0.08), rgba(200, 148, 55, 0.02)),
      rgba(12, 8, 4, 0.82);
    box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.04);
  }

  .feature-icon {
    width: 2.25rem;
    height: 2.25rem;
    display: grid;
    place-items: center;
    border: 1px solid rgba(214, 169, 84, 0.28);
    border-radius: 999px;
    background: radial-gradient(circle at 30% 30%, rgba(232, 200, 122, 0.22), rgba(158, 92, 30, 0.14));
    color: rgba(232, 200, 122, 0.92);
    font-family: 'Cinzel', serif;
    font-size: 0.66rem;
    letter-spacing: 0.12em;
  }

  .feature-copy {
    display: grid;
    gap: 0.28rem;
    min-width: 0;
  }

  .feature-copy h3 {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.78rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(233, 215, 182, 0.92);
  }

  .feature-copy p {
    margin: 0;
    font-size: 0.84rem;
    line-height: 1.55;
    color: rgba(228, 216, 191, 0.72);
    white-space: pre-line;
  }

  .feature-callout {
    display: grid;
    gap: 0.22rem;
    margin-top: 0.08rem;
    padding: 0.32rem 0.48rem;
    border: 1px solid rgba(240, 201, 120, 0.1);
    border-radius: 3px;
    background: linear-gradient(180deg, rgba(240, 201, 120, 0.035), rgba(240, 201, 120, 0.01));
    color: rgba(228, 216, 191, 0.56);
    font-size: 0.72rem;
    line-height: 1.4;
  }

  .feature-callout-line {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.35rem;
  }

  .feature-emphasis {
    color: rgba(246, 216, 146, 0.88);
    font-weight: 600;
  }

  .feature-hotkey {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    vertical-align: middle;
    padding: 0.05rem 0.34rem;
    border: 1px solid rgba(240, 201, 120, 0.36);
    border-radius: 999px;
    background: linear-gradient(180deg, rgba(240, 201, 120, 0.12), rgba(158, 92, 30, 0.1));
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.05) inset;
    color: #f7d995;
    font-family: 'Fira Code', monospace;
    font-size: 0.78em;
    font-weight: 700;
    letter-spacing: 0.08em;
    white-space: nowrap;
  }

  .update-detail-link {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    margin-top: 0.15rem;
    padding: 0.62rem 0.85rem;
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 999px;
    background: rgba(200, 148, 55, 0.05);
    color: rgba(228, 216, 191, 0.76);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    text-decoration: none;
  }

  .update-detail-link:hover {
    background: rgba(200, 148, 55, 0.12);
    border-color: rgba(200, 148, 55, 0.34);
    color: rgba(240, 222, 185, 0.92);
  }

  .install-acknowledge {
    display: grid;
    grid-template-columns: auto auto 1fr;
    gap: 0.7rem;
    align-items: start;
    padding: 0.8rem 0.88rem;
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 4px;
    background:
      linear-gradient(180deg, rgba(200, 148, 55, 0.055), rgba(200, 148, 55, 0.015)),
      rgba(12, 8, 4, 0.78);
    box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.04);
    text-align: left;
    color: rgba(228, 216, 191, 0.78);
    font-size: 0.77rem;
    line-height: 1.45;
    cursor: pointer;
  }

  .install-acknowledge-input {
    position: absolute;
    opacity: 0;
    pointer-events: none;
  }

  .install-acknowledge-box {
    width: 1.15rem;
    height: 1.15rem;
    margin-top: 0.08rem;
    border: 1px solid rgba(244, 227, 188, 0.58);
    border-radius: 0.28rem;
    background: linear-gradient(180deg, rgba(255, 255, 255, 0.09), rgba(255, 255, 255, 0.03));
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.05) inset,
      0 2px 10px rgba(0, 0, 0, 0.16);
    position: relative;
    transition:
      border-color 0.15s ease,
      background 0.15s ease,
      box-shadow 0.15s ease,
      transform 0.15s ease;
  }

  .install-acknowledge-box::after {
    content: '';
    position: absolute;
    left: 0.33rem;
    top: 0.14rem;
    width: 0.32rem;
    height: 0.62rem;
    border-right: 2px solid transparent;
    border-bottom: 2px solid transparent;
    transform: rotate(45deg);
    transition: border-color 0.15s ease;
  }

  .install-acknowledge-input:checked + .install-acknowledge-box {
    border-color: rgba(240, 201, 120, 0.62);
    background: linear-gradient(180deg, rgba(212, 160, 64, 0.28), rgba(158, 92, 30, 0.22));
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.12) inset,
      0 4px 14px rgba(170, 100, 25, 0.24);
  }

  .install-acknowledge-input:checked + .install-acknowledge-box::after {
    border-color: #fff2ca;
  }

  .install-acknowledge:hover .install-acknowledge-box {
    border-color: rgba(255, 214, 140, 0.8);
    transform: translateY(-1px);
  }

  .install-acknowledge-input:focus-visible + .install-acknowledge-box {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

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

  .header-corner-links {
    position: absolute;
    top: 0.9rem;
    left: 0.9rem;
    display: flex;
    gap: 0.45rem;
    z-index: 2;
  }

  .about-toggle {
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

  .social-toggle {
    padding: 0;
    cursor: pointer;
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

  .header-links {
    width: 100%;
    display: flex;
    justify-content: center;
    margin-top: 0.55rem;
  }

  .header-link {
    min-width: min(100%, 260px);
    display: grid;
    gap: 0.12rem;
    justify-items: center;
    padding: 0.72rem 0.95rem 0.78rem;
    border: 1px solid rgba(200, 148, 55, 0.2);
    border-radius: 3px;
    text-decoration: none;
    transition:
      transform 0.15s ease,
      background 0.15s ease,
      border-color 0.15s ease,
      box-shadow 0.15s ease;
  }

  .header-link-featured {
    background:
      radial-gradient(circle at top, rgba(255, 214, 140, 0.08), transparent 58%),
      linear-gradient(180deg, rgba(200, 148, 55, 0.09), rgba(200, 148, 55, 0.03));
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.05) inset,
      0 10px 24px rgba(0, 0, 0, 0.22);
  }

  .header-link:hover {
    transform: translateY(-1px);
    border-color: rgba(220, 168, 76, 0.36);
    background:
      radial-gradient(circle at top, rgba(255, 214, 140, 0.12), transparent 58%),
      linear-gradient(180deg, rgba(200, 148, 55, 0.14), rgba(200, 148, 55, 0.05));
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.09) inset,
      0 14px 28px rgba(0, 0, 0, 0.28);
  }

  .header-link:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .header-link-kicker {
    font-family: 'Cinzel', serif;
    font-size: 0.52rem;
    letter-spacing: 0.22em;
    text-transform: uppercase;
    color: rgba(214, 171, 96, 0.72);
  }

  .header-link-title {
    font-family: 'Cinzel', serif;
    font-size: 0.68rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(236, 224, 196, 0.9);
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

  .step-error {
    border-color: rgba(196, 98, 76, 0.28);
    box-shadow: 0 6px 28px rgba(0,0,0,0.35), 0 0 14px rgba(196, 98, 76, 0.04);
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

  .step-body-bpp {
    display: flex;
    align-items: stretch;
    gap: 1.1rem;
  }

  .step-bpp-content {
    flex: 1;
    display: grid;
    gap: 0.7rem;
    min-width: 0;
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
    min-width: 0;
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
  .tag-danger { background: rgba(191, 104, 81, 0.1); color: #f0b2a2; border: 1px solid rgba(191, 104, 81, 0.2); }

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

  .detail-faint {
    font-size: 0.74rem;
    color: rgba(180, 150, 110, 0.48);
  }

  .mismatch-summary {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 0.55rem 0.85rem;
  }

  .mismatch-link {
    flex-shrink: 0;
    color: rgba(223, 184, 115, 0.86);
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    text-decoration: none;
    white-space: nowrap;
  }

  .mismatch-link:hover {
    color: rgba(240, 211, 152, 0.96);
  }

  .mismatch-link:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .mismatch-versions {
    display: flex;
    flex-wrap: wrap;
    gap: 0.45rem;
  }

  .mismatch-version {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    min-width: 0;
    padding: 0.32rem 0.52rem;
    border: 1px solid rgba(191, 104, 81, 0.14);
    border-radius: 999px;
    background: rgba(191, 104, 81, 0.06);
  }

  .mismatch-version-label {
    color: rgba(214, 182, 126, 0.62);
    font-size: 0.68rem;
    white-space: nowrap;
  }

  .mismatch-version-value {
    color: rgba(235, 223, 198, 0.86);
    font-family: 'Fira Code', monospace;
    font-size: 0.72rem;
    white-space: nowrap;
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
    flex-wrap: wrap;
    align-items: stretch;
    gap: 0.5rem;
    position: relative;
  }

  .launch-btn {
    width: 100%;
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
    flex: 1 1 0;
    width: auto;
    min-width: 0;
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

  .install-btn.install-btn-danger {
    color: #fff3ee;
    background: linear-gradient(135deg, #bf5442 0%, #842619 52%, #d46d5a 100%);
    border-color: rgba(226, 128, 110, 0.52);
    box-shadow: 0 0 0 1px rgba(255, 181, 166, 0.16) inset, 0 4px 22px rgba(132, 38, 25, 0.34);
  }

  .install-btn.install-btn-danger::before {
    background: linear-gradient(180deg, rgba(255, 216, 208, 0.14) 0%, transparent 55%);
  }

  .install-btn.install-btn-danger:hover:not(:disabled) {
    box-shadow: 0 0 0 1px rgba(255, 181, 166, 0.22) inset, 0 6px 30px rgba(132, 38, 25, 0.5), 0 0 40px rgba(191, 84, 66, 0.18);
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

    .feature-card,
    .install-acknowledge {
      padding-left: 0.85rem;
      padding-right: 0.85rem;
    }

    .header-link {
      min-width: 0;
      width: 100%;
    }

    .step-body-bpp {
      flex-direction: column;
      gap: 0.7rem;
    }

    .detect-btn,
    .menu-trigger {
      width: 100%;
    }

    .locale-toggle {
      top: 0.7rem;
      right: 0.7rem;
    }

    .header-corner-links {
      top: 0.7rem;
      left: 0.7rem;
    }
  }
</style>
