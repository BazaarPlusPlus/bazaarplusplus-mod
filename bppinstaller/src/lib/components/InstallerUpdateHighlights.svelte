<script lang="ts">
  import { locale } from '$lib/locale';
</script>

<section class="update-hero">
  <p class="update-kicker">
    {$locale === 'zh' ? '首次启动 · 新版本导览' : 'First launch · What is new'}
  </p>
  <p class="update-summary">
    {$locale === 'zh'
      ? '安装器已经更新。重新执行一次安装，可以把最新 bundle 重新写入游戏目录。下面这几项是这个版本里更值得先看到的内容。'
      : 'The installer has been updated. Run the install step once more to refresh the game directory with the latest bundle. These are the highlights worth noticing first in this build.'}
  </p>
</section>

<div class="update-feature-list">
  <article class="update-feature-card">
    <div class="update-feature-icon">I</div>
    <div class="update-feature-copy">
      <h3>{$locale === 'zh' ? '首启展示更完整' : 'Better first-open overview'}</h3>
      <p>
        {$locale === 'zh'
          ? '更新后的首次打开会直接展示重点内容，不必先去 About 页面翻纯文本。'
          : 'The first launch after an update now surfaces the important changes directly instead of hiding them behind a plain text changelog.'}
      </p>
    </div>
  </article>

  <article class="update-feature-card">
    <div class="update-feature-icon">II</div>
    <div class="update-feature-copy">
      <h3>{$locale === 'zh' ? '新功能更容易扫一眼' : 'New features are easier to scan'}</h3>
      <p>
        {$locale === 'zh'
          ? '像怪物预览、设置项扩展、战斗状态条这类功能，可以在这里用更适合展示的卡片形式说明。'
          : 'Feature highlights like monster preview, expanded settings, and the combat status bar can be presented here in a more visual card format.'}
      </p>
    </div>
  </article>

  <article class="update-feature-card">
    <div class="update-feature-icon">III</div>
    <div class="update-feature-copy">
      <h3>{$locale === 'zh' ? 'Release Notes 保持简洁' : 'Release notes stay concise'}</h3>
      <p>
        {$locale === 'zh'
          ? 'About 页面里的 release notes 可以继续保留为简单文本，而首页首启提示和独立页负责更完整的视觉化说明。'
          : 'The About page can keep a simpler text changelog, while the first-launch prompt and dedicated page handle the richer visual summary.'}
      </p>
      <p class="update-feature-callout">
        {$locale === 'zh'
          ? '点击“重新安装”后会重新写入最新文件，现有设置不会因为这一步被重置。'
          : 'Selecting Reinstall writes the latest files again without resetting the current settings.'}
      </p>
    </div>
  </article>
</div>

<style>
  .update-hero {
    display: grid;
    gap: 0.5rem;
    padding: 1rem 1.05rem;
    text-align: left;
    border: 1px solid rgba(200, 148, 55, 0.18);
    border-radius: 4px;
    background:
      radial-gradient(circle at top right, rgba(232, 200, 122, 0.16), transparent 42%),
      linear-gradient(180deg, rgba(200, 148, 55, 0.08), rgba(200, 148, 55, 0.02)),
      rgba(12, 8, 4, 0.84);
    box-shadow: inset 0 0 0 1px rgba(255, 198, 98, 0.04);
  }

  .update-kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.22em;
    text-transform: uppercase;
    color: rgba(232, 200, 122, 0.7);
  }

  .update-summary {
    margin: 0;
    font-size: 0.9rem;
    line-height: 1.65;
    color: rgba(233, 222, 198, 0.84);
  }

  .update-feature-list {
    display: grid;
    gap: 0.7rem;
    margin-top: 0.15rem;
    text-align: left;
  }

  .update-feature-card {
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

  .update-feature-icon {
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

  .update-feature-copy {
    display: grid;
    gap: 0.28rem;
    min-width: 0;
  }

  .update-feature-copy h3 {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.78rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(233, 215, 182, 0.92);
  }

  .update-feature-copy p {
    margin: 0;
    font-size: 0.84rem;
    line-height: 1.55;
    color: rgba(228, 216, 191, 0.72);
  }

  .update-feature-callout {
    margin-top: 0.08rem;
    padding: 0.38rem 0.48rem;
    border: 1px solid rgba(240, 201, 120, 0.1);
    border-radius: 3px;
    background: linear-gradient(180deg, rgba(240, 201, 120, 0.035), rgba(240, 201, 120, 0.01));
    color: rgba(228, 216, 191, 0.6);
    font-size: 0.74rem;
  }

  @media (max-width: 520px) {
    .update-hero,
    .update-feature-card {
      padding-left: 0.85rem;
      padding-right: 0.85rem;
    }
  }
</style>
