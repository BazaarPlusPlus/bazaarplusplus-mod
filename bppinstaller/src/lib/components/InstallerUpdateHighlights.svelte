<script lang="ts">
  import { locale } from '$lib/locale';

  type LocalizedText = {
    zh: string;
    en: string;
  };

  type HighlightSection = {
    icon: string;
    title: LocalizedText;
    bullets: LocalizedText[];
    tone?: 'default' | 'support' | 'warning';
  };

  const sections: HighlightSection[] = [
    {
      icon: 'I',
      title: {
        zh: '野怪掉落预览',
        en: 'Monster Preview'
      },
      bullets: [
        {
          zh: '修复了进入门票事件后，野怪预览可能失效的问题。',
          en: 'Fixed an issue where monster previews might fail to display after entering a ticket event.'
        },
        {
          zh: '新增野怪的血量、经验和金币展示，战斗收益信息更直观。',
          en: 'Monster previews now show health, experience, and gold so combat rewards are easier to read.'
        }
      ]
    },
    {
      icon: 'II',
      title: {
        zh: '附魔效果预览',
        en: 'Enchant Preview'
      },
      bullets: [
        {
          zh: '优化了附魔效果预览的展示形式。',
          en: 'The enchant preview layout has been refined.'
        },
        {
          zh: '现在对手棋盘和商店中的物品也支持附魔预览。',
          en: 'Items on the opponent board and in shops now support enchant previews as well.'
        },
        {
          zh: '修复了关闭“总是显示附魔”后，悬停时再按 Ctrl 无法正常显示附魔预览的问题。',
          en: 'Fixed an issue where enchant previews would not appear on hover with Ctrl after disabling Always Show.'
        }
      ]
    },
    {
      icon: 'III',
      title: {
        zh: '升级效果预览',
        en: 'Upgrade Preview'
      },
      bullets: [
        {
          zh: '鼠标悬停卡牌时，按住 Shift 即可查看升级后的效果变化。',
          en: 'Hold Shift while hovering a card to inspect how its upgraded effect changes.'
        }
      ]
    },
    {
      icon: 'IV',
      title: {
        zh: '战斗状态展示',
        en: 'Combat Status Bar'
      },
      bullets: [
        {
          zh: '战斗状态条支持的最大倍速已从 5 降低到 3。',
          en: 'The maximum speed multiplier supported by the combat status bar has been reduced from 5x to 3x.'
        },
        {
          zh: '优化了战斗状态条的显示逻辑，现在可以显示上一场战斗的耗时。',
          en: 'The display logic has been improved and can now show the duration of the previous combat.'
        }
      ]
    },
    {
      icon: 'V',
      title: {
        zh: '安装器',
        en: 'Installer'
      },
      bullets: [
        {
          zh: '安装器现已加入可选的自动更新功能。',
          en: 'The installer now includes optional automatic updates.'
        },
        {
          zh: '优化了自定义游戏路径下的安装检测与重装流程。',
          en: 'Improved installation detection and reinstall flow for custom game paths.'
        },
        {
          zh: '安装器现在更聚焦于检测、安装、更新和启动流程。',
          en: 'The installer is now more focused on detection, installation, updates, and launch flow.'
        }
      ]
    },
    {
      icon: 'VI',
      title: {
        zh: '支持者',
        en: 'Supporters'
      },
      bullets: [
        {
          zh: '信息页面增加了支持者名单，感谢大家的支持。',
          en: 'The information page now includes a supporters list to thank everyone backing the project.'
        }
      ],
      tone: 'support'
    },
    {
      icon: 'VII',
      title: {
        zh: '已知问题',
        en: 'Known Issues'
      },
      bullets: [
        {
          zh: '商店中的卡牌在预览升级效果时，文本可能会出现溢出。',
          en: 'Upgrade preview text can still overflow for cards shown in shops.'
        },
        {
          zh: '厨师职业的高温词条可能会影响 Bazaar++ Logo 的显示。',
          en: 'The Chef class high-temperature affix can still affect Bazaar++ logo rendering.'
        }
      ],
      tone: 'warning'
    }
  ];
</script>

<section class="update-hero">
  <p class="update-kicker">
    {$locale === 'zh' ? '当前版本 · 更新亮点' : 'Current build · What is new'}
  </p>
  <h2 class="update-title">
    {$locale === 'zh' ? 'BazaarPlusPlus' : "BazaarPlusPlus"}
  </h2>
  <p class="update-summary">
    {$locale === 'zh'
      ? '本次更新主要覆盖野怪掉落预览、附魔与升级预览、战斗状态展示、安装器体验，以及当前已知问题。'
      : 'This update focuses on monster drop previews, enchant and upgrade previews, the combat status display, installer improvements, and current known issues.'}
  </p>
</section>

<div class="update-feature-list">
  {#each sections as section}
    <article class={`update-feature-card ${section.tone ? `tone-${section.tone}` : ''}`}>
      <div class="update-feature-icon">{section.icon}</div>
      <div class="update-feature-copy">
        <h3>{$locale === 'zh' ? section.title.zh : section.title.en}</h3>
        <ul class="update-feature-points">
          {#each section.bullets as bullet}
            <li>{$locale === 'zh' ? bullet.zh : bullet.en}</li>
          {/each}
        </ul>
      </div>
    </article>
  {/each}
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

  .update-title {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: clamp(1rem, 2.6vw, 1.4rem);
    letter-spacing: 0.06em;
    color: rgba(239, 223, 188, 0.95);
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

  .tone-support {
    border-color: rgba(118, 174, 120, 0.24);
    background:
      linear-gradient(180deg, rgba(118, 174, 120, 0.1), rgba(118, 174, 120, 0.03)),
      rgba(12, 8, 4, 0.82);
  }

  .tone-warning {
    border-color: rgba(200, 104, 82, 0.26);
    background:
      linear-gradient(180deg, rgba(200, 104, 82, 0.11), rgba(200, 104, 82, 0.03)),
      rgba(12, 8, 4, 0.82);
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

  .tone-support .update-feature-icon {
    border-color: rgba(154, 201, 146, 0.3);
    background: radial-gradient(circle at 30% 30%, rgba(154, 201, 146, 0.26), rgba(63, 105, 63, 0.18));
    color: rgba(214, 241, 206, 0.92);
  }

  .tone-warning .update-feature-icon {
    border-color: rgba(228, 150, 121, 0.3);
    background: radial-gradient(circle at 30% 30%, rgba(228, 150, 121, 0.26), rgba(128, 45, 33, 0.2));
    color: rgba(255, 222, 213, 0.94);
  }

  .update-feature-copy {
    display: grid;
    gap: 0.35rem;
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

  .update-feature-points {
    margin: 0;
    padding-left: 1.1rem;
    display: grid;
    gap: 0.3rem;
    color: rgba(228, 216, 191, 0.78);
  }

  .update-feature-points li {
    font-size: 0.84rem;
    line-height: 1.58;
  }

  @media (max-width: 520px) {
    .update-hero,
    .update-feature-card {
      padding-left: 0.85rem;
      padding-right: 0.85rem;
    }
  }
</style>
