<script lang="ts">
  import { invoke } from '@tauri-apps/api/core';
  import { onMount } from 'svelte';
  import { page } from '$app/stores';
  import AppModal from '$lib/components/AppModal.svelte';
  import type { ModConfigReadResult } from '$lib/types';
  import { formatMessage, messages } from '$lib/i18n';
  import { locale, handleLocaleToggle } from '$lib/locale';

  const SPEED_STEPS = [0.25, 0.5, 1.0, 2.0, 3.0, 4.0, 5.0];
  type LoadState = 'loading' | 'ready' | 'missing-path' | 'error';

  let gamePath = '';
  let loadState: LoadState = 'loading';
  let configExists = false;

  let enableNameOverride = false;
  let enchantPreviewAlwaysShow = true;
  let enableCombatStatusBar = false;
  let speedIdx = 2; // default: 1.00
  let showCombatStatusBarRestartModal = false;

  $: t = (key: keyof typeof messages.en): string => formatMessage($locale, key);
  $: localeBadge = $locale === 'zh' ? '中' : 'EN';
  $: localeButtonLabel = $locale === 'zh' ? 'Switch to English' : '切换到中文';
  $: currentSpeed = SPEED_STEPS[speedIdx];
  $: speedDisplay = currentSpeed.toFixed(2) + '×';
  $: canStepDown = speedIdx > 0;
  $: canStepUp = speedIdx < SPEED_STEPS.length - 1;

  function findSpeedIdx(val: number): number {
    const idx = SPEED_STEPS.findIndex((s) => Math.abs(s - val) < 0.001);
    return idx === -1 ? 2 : idx;
  }

  async function loadConfig() {
    if (!gamePath) {
      loadState = 'missing-path';
      return;
    }

    try {
      const result = await invoke<ModConfigReadResult>('read_mod_config', { gamePath });
      configExists = result.config_exists;
      enableNameOverride = result.values['StreamerMode.EnableNameOverride']?.toLowerCase() === 'true';
      enchantPreviewAlwaysShow = result.values['EnchantPreview.AlwaysShow']?.toLowerCase() !== 'false';
      enableCombatStatusBar = result.values['CombatStatusBar.Enabled']?.toLowerCase() !== 'false';
      const raw = parseFloat(result.values['CombatStatusBar.SpeedMultiplier'] ?? '1');
      speedIdx = findSpeedIdx(isNaN(raw) ? 1 : raw);
      loadState = 'ready';
    } catch (e) {
      console.error('read_mod_config failed:', e);
      loadState = 'error';
    }
  }

  async function writeValue(section: string, key: string, value: string): Promise<boolean> {
    if (!gamePath) return false;
    try {
      await invoke('write_config_value', { gamePath, section, key, value });
      return true;
    } catch (e) {
      console.error('write_config_value failed:', e);
      return false;
    }
  }

  async function toggleNameOverride() {
    enableNameOverride = !enableNameOverride;
    await writeValue('StreamerMode', 'EnableNameOverride', String(enableNameOverride));
  }

  async function toggleEnchantPreviewAlwaysShow() {
    enchantPreviewAlwaysShow = !enchantPreviewAlwaysShow;
    await writeValue('EnchantPreview', 'AlwaysShow', String(enchantPreviewAlwaysShow));
  }

  async function toggleCombatStatusBar() {
    const nextValue = !enableCombatStatusBar;
    enableCombatStatusBar = nextValue;
    const saved = await writeValue('CombatStatusBar', 'Enabled', String(nextValue));
    if (saved && nextValue) {
      showCombatStatusBarRestartModal = true;
    }
  }

  function closeCombatStatusBarRestartModal() {
    showCombatStatusBarRestartModal = false;
  }

  function handleRestartModalKeydown(event: KeyboardEvent) {
    if (!showCombatStatusBarRestartModal || event.key !== 'F6') return;
    event.preventDefault();
    closeCombatStatusBarRestartModal();
  }

  async function stepSpeed(delta: number) {
    const next = speedIdx + delta;
    if (next < 0 || next >= SPEED_STEPS.length) return;
    speedIdx = next;
    await writeValue('CombatStatusBar', 'SpeedMultiplier', SPEED_STEPS[next].toFixed(2));
  }

  onMount(() => {
    locale.init();
    gamePath = $page.url.searchParams.get('gamePath') ?? '';
    void loadConfig();
  });
</script>

<svelte:head>
  <title>{t('settingsTitle')} - BazaarPlusPlus</title>
</svelte:head>

<AppModal
  open={showCombatStatusBarRestartModal}
  eyebrow="Combat Status Bar"
  title={$locale === 'zh' ? '需要重启游戏' : 'Restart Required'}
  body={$locale === 'zh'
    ? '启用战斗状态条后，需要重启游戏才能生效\n游戏内可按 F6 切换显示；请按 F6 关闭当前弹窗'
    : 'After enabling the combat status bar, restart the game for the change to take effect.\nPress F6 in-game to toggle it; press F6 to close this dialog.'}
  showConfirm={false}
  onConfirm={closeCombatStatusBarRestartModal}
/>

<svelte:window onkeydown={handleRestartModalKeydown} />

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

    <a class="back-btn" href="/">
      <svg class="back-icon" viewBox="0 0 24 24" aria-hidden="true">
        <path d="M15 18l-6-6 6-6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" fill="none" />
      </svg>
      {t('aboutBack')}
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
          d="M12 3a9 9 0 1 0 9 9a9 9 0 0 0-9-9Zm5.9 8h-2.2a14.3 14.3 0 0 0-1.2-4A7.1 7.1 0 0 1 17.9 11Zm-5.9-5.8c.7.9 1.7 2.9 2.1 5.8H9.9c.4-2.9 1.4-4.9 2.1-5.8ZM6.5 7a14.3 14.3 0 0 0-1.2 4H3.1A7.1 7.1 0 0 1 6.5 7ZM3.1 13h2.2a14.3 14.3 0 0 0 1.2 4A7.1 7.1 0 0 1 3.1 13Zm8.9 5.8c-.7-.9-1.7-2.9-2.1-5.8h4.2c-.4 2.9-1.4 4.9-2.1 5.8Zm2.5-1.8a14.3 14.3 0 0 0 1.2-4h2.2a7.1 7.1 0 0 1-3.4 4Z"
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

    <h1>{t('settingsTitle')}</h1>

    <div class="rule" aria-hidden="true">
      <span></span><span class="diamond">+</span><span></span>
    </div>
  </header>

  {#if loadState === 'loading'}
    <div class="card loading-card">
      <span class="spinner" aria-hidden="true"></span>
    </div>
  {:else if loadState !== 'ready'}
    <section class="card state-card">
      <h2 class="section-title">
        {#if loadState === 'missing-path'}
          {t('settingsMissingPathTitle')}
        {:else}
          {t('settingsLoadErrorTitle')}
        {/if}
      </h2>
      <p class="state-body">
        {#if loadState === 'missing-path'}
          {t('settingsMissingPathBody')}
        {:else}
          {t('settingsLoadErrorBody')}
        {/if}
      </p>
    </section>
  {:else}
    {#if !configExists}
      <section class="card state-card">
        <h2 class="section-title">{t('settingsDefaultConfigTitle')}</h2>
        <p class="state-body">{t('settingsDefaultConfigBody')}</p>
      </section>
    {/if}

    <!-- StreamerMode -->
    <section class="card">
      <h2 class="section-title">{t('sectionStreamerMode')}</h2>
      <ul class="setting-list">
        <li class="setting-row">
          <div class="setting-info">
            <span class="setting-label">{t('keyEnableNameOverride')}</span>
            <span class="setting-desc">{t('descEnableNameOverride')}</span>
          </div>
          <button
            class="toggle-btn"
            class:toggle-on={enableNameOverride}
            type="button"
            onclick={toggleNameOverride}
          >
            {#if enableNameOverride}◆ {t('toggleOn')}{:else}◇ {t('toggleOff')}{/if}
          </button>
        </li>
      </ul>
    </section>

    <section class="card">
      <h2 class="section-title">{t('sectionEnchantPreview')}</h2>
      <ul class="setting-list">
        <li class="setting-row">
          <div class="setting-info">
            <span class="setting-label">{t('keyEnchantPreviewAlwaysShow')}</span>
            <span class="setting-desc">{t('descEnchantPreviewAlwaysShow')}</span>
          </div>
          <button
            class="toggle-btn"
            class:toggle-on={enchantPreviewAlwaysShow}
            type="button"
            onclick={toggleEnchantPreviewAlwaysShow}
          >
            {#if enchantPreviewAlwaysShow}◆ {t('toggleOn')}{:else}◇ {t('toggleOff')}{/if}
          </button>
        </li>
      </ul>
    </section>

    <!-- Combat -->
    <section class="card">
      <h2 class="section-title">{t('sectionCombatStatusBar')}</h2>
      <ul class="setting-list">
        <li class="setting-row">
          <div class="setting-info">
            <span class="setting-label">{t('keyCombatStatusBarEnabled')}</span>
            <span class="setting-desc">{t('descCombatStatusBarEnabled')}</span>
          </div>
          <button
            class="toggle-btn"
            class:toggle-on={enableCombatStatusBar}
            type="button"
            onclick={toggleCombatStatusBar}
          >
            {#if enableCombatStatusBar}◆ {t('toggleOn')}{:else}◇ {t('toggleOff')}{/if}
          </button>
        </li>

        <li class="setting-row setting-row-block">
          <div class="setting-info">
            <span class="setting-label">{t('keyCombatStatusBarSpeedMultiplier')}</span>
            <span class="setting-desc">{t('descCombatStatusBarSpeedMultiplier')}</span>
          </div>
          <div class="stepper">
            <button
              class="stepper-btn"
              type="button"
              onclick={() => stepSpeed(-1)}
              disabled={!canStepDown}
              aria-label={t('settingsDecreaseSpeed')}
            >‹</button>
            <span class="stepper-value">{speedDisplay}</span>
            <button
              class="stepper-btn"
              type="button"
              onclick={() => stepSpeed(1)}
              disabled={!canStepUp}
              aria-label={t('settingsIncreaseSpeed')}
            >›</button>
          </div>
        </li>
      </ul>
    </section>
  {/if}

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

  .back-btn {
    position: absolute;
    top: 0.9rem;
    left: 0.9rem;
    height: 2rem;
    padding: 0.3rem 0.55rem;
    border: 1px solid rgba(200, 148, 55, 0.24);
    border-radius: 2px;
    background: linear-gradient(180deg, rgba(200, 148, 55, 0.12), rgba(200, 148, 55, 0.06));
    color: rgba(228, 216, 191, 0.82);
    font-family: 'Cinzel', 'Songti SC', 'STSong', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.3rem;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    z-index: 2;
    text-decoration: none;
    transition: background 0.15s ease, border-color 0.15s ease;
  }

  .back-btn:hover {
    background: linear-gradient(180deg, rgba(200, 148, 55, 0.2), rgba(200, 148, 55, 0.1));
    border-color: rgba(200, 148, 55, 0.4);
  }

  .back-btn:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .back-icon {
    width: 0.9rem;
    height: 0.9rem;
    flex-shrink: 0;
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
    font-family: 'Cinzel', 'Songti SC', 'STSong', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.35rem;
    box-shadow: 0 0 0 1px rgba(255, 198, 98, 0.08) inset;
    z-index: 2;
    cursor: pointer;
    border-style: solid;
    font: inherit;
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

  h1 {
    margin: 0.1rem 0 0;
    font-family: 'Cinzel Decorative', 'Songti SC', 'STSong', serif;
    font-size: clamp(1.35rem, 4.2vw, 2.1rem);
    font-weight: 700;
    line-height: 1;
    background: linear-gradient(155deg, #e8c87a 0%, #bf852e 55%, #e8c87a 100%);
    -webkit-background-clip: text;
    background-clip: text;
    -webkit-text-fill-color: transparent;
    filter: drop-shadow(0 2px 10px rgba(205, 150, 60, 0.28));
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

  /* Cards */
  .card {
    padding: 0.95rem 1.05rem;
    background: rgba(18, 11, 5, 0.88);
    border: 1px solid rgba(180, 130, 48, 0.13);
    border-radius: 3px;
    box-shadow: 0 6px 28px rgba(0,0,0,0.35);
    display: grid;
    gap: 0.6rem;
  }

  .loading-card {
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 5rem;
  }

  .state-card {
    gap: 0.45rem;
  }

  .section-title {
    margin: 0;
    font-family: 'Cinzel', 'Songti SC', 'STSong', serif;
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(220, 195, 145, 0.8);
  }

  .state-body {
    margin: 0;
    font-family: 'IM Fell English', 'Noto Serif SC', 'Songti SC', Georgia, serif;
    font-size: 0.88rem;
    line-height: 1.5;
    color: rgba(228, 216, 191, 0.78);
  }

  /* Setting rows */
  .setting-list {
    margin: 0;
    padding: 0;
    list-style: none;
    display: grid;
    gap: 0.4rem;
  }

  .setting-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 0.55rem 0.65rem;
    border-radius: 2px;
    background: rgba(200, 148, 55, 0.04);
    border: 1px solid rgba(180, 130, 48, 0.08);
  }

  .setting-row-block {
    flex-wrap: wrap;
    gap: 0.55rem;
  }

  .setting-info {
    display: grid;
    gap: 0.18rem;
    min-width: 0;
    flex: 1;
  }

  .setting-label {
    font-family: 'Cinzel', 'Songti SC', 'STSong', serif;
    font-size: 0.66rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: rgba(220, 195, 145, 0.82);
  }

  .setting-desc {
    font-family: 'IM Fell English', 'Noto Serif SC', 'Songti SC', Georgia, serif;
    font-style: italic;
    font-size: 0.76rem;
    color: rgba(200, 170, 120, 0.5);
    line-height: 1.3;
  }

  /* Toggle button */
  .toggle-btn {
    flex-shrink: 0;
    width: 72px;
    padding: 0.38rem 0.5rem;
    font-family: 'Cinzel', 'Songti SC', 'STSong', serif;
    font-size: 0.6rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    text-align: center;
    border-radius: 2px;
    cursor: pointer;
    transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
    /* OFF state */
    background: rgba(200, 148, 55, 0.06);
    border: 1px solid rgba(180, 130, 48, 0.18);
    color: rgba(200, 155, 72, 0.7);
  }

  .toggle-btn:hover {
    background: rgba(200, 148, 55, 0.12);
    border-color: rgba(200, 148, 55, 0.34);
    color: rgba(220, 180, 100, 0.9);
  }

  /* ON state */
  .toggle-btn.toggle-on {
    background: rgba(80, 180, 120, 0.12);
    border-color: rgba(80, 180, 120, 0.3);
    color: #6dd9a0;
  }

  .toggle-btn.toggle-on:hover {
    background: rgba(80, 180, 120, 0.18);
    border-color: rgba(80, 180, 120, 0.45);
  }

  .toggle-btn:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  /* Speed stepper */
  .stepper {
    display: flex;
    align-items: center;
    gap: 0;
    flex-shrink: 0;
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 2px;
    overflow: hidden;
  }

  .stepper-btn {
    width: 32px;
    height: 32px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(200, 148, 55, 0.06);
    border: none;
    color: rgba(200, 155, 72, 0.78);
    font-size: 1rem;
    cursor: pointer;
    transition: background 0.15s ease, color 0.15s ease;
  }

  .stepper-btn:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.14);
    color: rgba(220, 180, 100, 0.95);
  }

  .stepper-btn:disabled {
    opacity: 0.3;
    cursor: not-allowed;
  }

  .stepper-btn:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .stepper-btn:first-child {
    border-right: 1px solid rgba(180, 130, 48, 0.18);
  }

  .stepper-btn:last-child {
    border-left: 1px solid rgba(180, 130, 48, 0.18);
  }

  .stepper-value {
    width: 54px;
    text-align: center;
    font-family: 'Fira Code', 'SF Mono', 'PingFang SC', monospace;
    font-size: 0.82rem;
    color: rgba(228, 216, 191, 0.88);
    user-select: none;
  }

  /* Spinner */
  .spinner {
    display: inline-block;
    width: 16px;
    height: 16px;
    border: 1.5px solid rgba(200, 165, 100, 0.2);
    border-top-color: rgba(200, 165, 100, 0.7);
    border-radius: 50%;
    animation: spin 0.75s linear infinite;
  }

  @keyframes spin { to { transform: rotate(360deg); } }

  /* Footer */
  .footer {
    text-align: center;
    display: grid;
    gap: 0.4rem;
  }

  .footer p {
    margin: 0;
    font-family: 'Cinzel', 'Songti SC', 'STSong', serif;
    font-size: 0.5rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(140, 110, 60, 0.35);
  }

  button { cursor: pointer; border: none; font: inherit; }

  @media (max-width: 520px) {
    .shell { padding: 1rem 0.85rem 1.5rem; }
    .header { padding: 1.2rem 1rem 1rem; }

    .back-btn {
      top: 0.7rem;
      left: 0.7rem;
    }

    .locale-toggle {
      top: 0.7rem;
      right: 0.7rem;
    }

    .setting-row {
      flex-direction: column;
      align-items: flex-start;
    }

    .toggle-btn {
      align-self: flex-end;
    }

    .stepper {
      align-self: flex-end;
    }
  }
</style>
