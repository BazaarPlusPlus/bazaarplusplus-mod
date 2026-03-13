export type Locale = 'en' | 'zh';

export type MessageKey =
  | 'htmlLang'
  | 'pageTitle'
  | 'kicker'
  | 'subtitle'
  | 'stepBpp'
  | 'stepDotnet'
  | 'stepBazaar'
  | 'stepActions'
  | 'statusInstalled'
  | 'statusChecking'
  | 'statusNotInstalled'
  | 'installedVersion'
  | 'detectInstalledHint'
  | 'updateAvailable'
  | 'updateHint'
  | 'actionCheckingUpdates'
  | 'actionUpdating'
  | 'actionCheckUpdates'
  | 'actionUpdate'
  | 'statusRuntimeMissing'
  | 'runtimeVersion'
  | 'runtimeCompatible'
  | 'runtimeNotFound'
  | 'runtimeIdle'
  | 'statusFound'
  | 'actionReenter'
  | 'actionBrowse'
  | 'placeholderGamePath'
  | 'actionCheck'
  | 'errorGamePath'
  | 'actionDetecting'
  | 'actionDetect'
  | 'actionInstalling'
  | 'actionReinstall'
  | 'actionInstall'
  | 'actionUninstalling'
  | 'actionUninstall'
  | 'footer'
  | 'aboutLabel'
  | 'aboutTitle'
  | 'aboutBack'
  | 'aboutOpenSource'
  | 'aboutInspiredBy'
  | 'aboutDependencies'
  | 'aboutDataSources'
  | 'aboutInfo'
  | 'aboutAuthors'
  | 'aboutAuthorRole'
  | 'aboutCocreatorRole'
  | 'aboutSupport'
  | 'aboutFrontendVersion'
  | 'aboutBackendVersion'
  | 'runtimeDownload'
  | 'settingsTitle'
  | 'settingsOpen'
  | 'sectionStreamerMode'
  | 'keyEnableNameOverride'
  | 'descEnableNameOverride'
  | 'sectionEnchantPreview'
  | 'keyEnchantPreviewAlwaysShow'
  | 'descEnchantPreviewAlwaysShow'
  | 'sectionCombatStatusBar'
  | 'keyCombatStatusBarEnabled'
  | 'descCombatStatusBarEnabled'
  | 'keyCombatStatusBarSpeedMultiplier'
  | 'descCombatStatusBarSpeedMultiplier'
  | 'toggleOn'
  | 'toggleOff'
  | 'modInstalledHint'
  | 'settingsMissingPathTitle'
  | 'settingsMissingPathBody'
  | 'settingsDefaultConfigTitle'
  | 'settingsDefaultConfigBody'
  | 'settingsLoadErrorTitle'
  | 'settingsLoadErrorBody'
  | 'settingsDecreaseSpeed'
  | 'settingsIncreaseSpeed';

export const defaultLocale: Locale = 'en';

export const messages: Record<Locale, Record<MessageKey, string>> = {
  en: {
    htmlLang: 'en',
    pageTitle: 'BazaarPlusPlus Installer',
    kicker: 'Born of Passion',
    subtitle: 'Mod Installation',
    stepBpp: 'BazaarPlusPlus',
    stepDotnet: '.NET Runtime',
    stepBazaar: 'The Bazaar',
    stepActions: 'Actions',
    statusInstalled: 'Installed',
    statusChecking: 'Checking...',
    statusNotInstalled: 'Not installed',
    installedVersion: 'Installed version: {version}',
    detectInstalledHint: 'Run detection to inspect the installed BazaarPlusPlus version.',
    updateAvailable: 'Update available: {version}',
    updateHint: 'Check whether a newer BazaarPlusPlus version is available.',
    actionCheckingUpdates: 'Checking...',
    actionUpdating: 'Updating...',
    actionCheckUpdates: 'Check updates',
    actionUpdate: 'Update',
    statusRuntimeMissing: 'Required component missing',
    runtimeVersion: 'Runtime: {version}',
    runtimeCompatible: 'Compatible .NET runtime detected',
    runtimeNotFound: 'A compatible .NET runtime is required before installation.',
    runtimeIdle: 'Run detection to inspect the local .NET environment',
    statusFound: 'Found',
    actionReenter: 'Choose again',
    actionBrowse: 'Browse',
    placeholderGamePath: 'Game install path...',
    actionCheck: 'Check',
    errorGamePath: 'The Bazaar was not found in this folder. Verify the install path.',
    actionDetecting: 'Detecting',
    actionDetect: 'Detect',
    actionInstalling: 'Installing...',
    actionReinstall: 'Reinstall',
    actionInstall: 'Install',
    actionUninstalling: 'Uninstalling...',
    actionUninstall: 'Uninstall',
    footer: 'BazaarPlusPlus · Born of Passion',
    aboutLabel: 'About',
    aboutTitle: 'About',
    aboutBack: 'Back',
    aboutOpenSource: 'Open Source Software',
    aboutInspiredBy: 'Inspired By',
    aboutDependencies: 'Dependencies',
    aboutDataSources: 'Data Sources',
    aboutInfo: 'Information',
    aboutAuthors: 'Authors',
    aboutAuthorRole: 'Author',
    aboutCocreatorRole: 'Co-creator',
    aboutSupport: 'Support',
    aboutFrontendVersion: 'Frontend',
    aboutBackendVersion: 'Backend',
    runtimeDownload: 'Download .NET',
    settingsTitle: 'Settings',
    settingsOpen: 'Settings',
    sectionStreamerMode: 'Streamer Mode',
    keyEnableNameOverride: 'Anonymous Display',
    descEnableNameOverride: "Set your in-game display name to Anonymous.",
    sectionEnchantPreview: 'Enchant Preview',
    keyEnchantPreviewAlwaysShow: 'Always Show',
    descEnchantPreviewAlwaysShow: 'Show enchant preview text by default. Turn this off to require holding Ctrl.',
    sectionCombatStatusBar: 'Combat Status Bar',
    keyCombatStatusBarEnabled: 'Enabled',
    descCombatStatusBarEnabled: 'Show elapsed time and speed controls during combat',
    keyCombatStatusBarSpeedMultiplier: 'Speed Multiplier',
    descCombatStatusBarSpeedMultiplier: 'Default playback speed for combat simulation',
    toggleOn: 'ON',
    toggleOff: 'OFF',
    modInstalledHint: 'Open Settings to configure and customize BazaarPlusPlus',
    settingsMissingPathTitle: 'Game Path Required',
    settingsMissingPathBody: 'Return to the installer and run detection before opening Settings.',
    settingsDefaultConfigTitle: 'Using Default Settings',
    settingsDefaultConfigBody: 'No config file was found yet. Any change you make here will create BazaarPlusPlus.cfg automatically.',
    settingsLoadErrorTitle: 'Unable to Read Settings',
    settingsLoadErrorBody: 'Make sure BazaarPlusPlus is installed correctly, then try opening Settings again.',
    settingsDecreaseSpeed: 'Decrease speed',
    settingsIncreaseSpeed: 'Increase speed',
  },
  zh: {
    htmlLang: 'zh-CN',
    pageTitle: 'BazaarPlusPlus 安装器',
    kicker: '因热爱而生',
    subtitle: '模组安装',
    stepBpp: 'BazaarPlusPlus',
    stepDotnet: '.NET 运行时',
    stepBazaar: 'The Bazaar',
    stepActions: '操作',
    statusInstalled: '已安装',
    statusChecking: '检查中...',
    statusNotInstalled: '未安装',
    installedVersion: '已安装版本：{version}',
    detectInstalledHint: '点击检测以查看当前已安装的 BazaarPlusPlus 版本。',
    updateAvailable: '发现新版本：{version}',
    updateHint: '检查 BazaarPlusPlus 是否有可用更新。',
    actionCheckingUpdates: '检查中...',
    actionUpdating: '更新中...',
    actionCheckUpdates: '检查更新',
    actionUpdate: '更新',
    statusRuntimeMissing: '缺少必需组件',
    runtimeVersion: '运行时：{version}',
    runtimeCompatible: '已检测到兼容的 .NET 运行时',
    runtimeNotFound: '需要先安装兼容的 .NET 运行时，才能继续安装。',
    runtimeIdle: '运行检测以检查本机 .NET 环境',
    statusFound: '已找到',
    actionReenter: '重新选择',
    actionBrowse: '浏览',
    placeholderGamePath: '游戏安装路径...',
    actionCheck: '检查',
    errorGamePath: '该目录中未找到 The Bazaar，请确认安装路径是否正确。',
    actionDetecting: '检测中',
    actionDetect: '检测',
    actionInstalling: '安装中...',
    actionReinstall: '重新安装',
    actionInstall: '安装',
    actionUninstalling: '卸载中...',
    actionUninstall: '卸载',
    footer: 'BazaarPlusPlus · 因热爱而生',
    aboutLabel: '关于',
    aboutTitle: '关于',
    aboutBack: '返回',
    aboutOpenSource: '开源软件',
    aboutInspiredBy: '灵感来源',
    aboutDependencies: '依赖项目',
    aboutDataSources: '数据来源',
    aboutInfo: '信息',
    aboutAuthors: '作者',
    aboutAuthorRole: '作者',
    aboutCocreatorRole: '联创',
    aboutSupport: '支持我们',
    aboutFrontendVersion: '前端',
    aboutBackendVersion: '后端',
    runtimeDownload: '下载 .NET',
    settingsTitle: '设置',
    settingsOpen: '设置',
    sectionStreamerMode: '主播模式',
    keyEnableNameOverride: '匿名显示',
    descEnableNameOverride: '将你的游戏内显示名称设为 Anonymous。',
    sectionEnchantPreview: '附魔预览',
    keyEnchantPreviewAlwaysShow: '默认显示',
    descEnchantPreviewAlwaysShow: '默认显示附魔预览文本。关闭后改为按住 Ctrl 才显示。',
    sectionCombatStatusBar: '战斗状态栏',
    keyCombatStatusBarEnabled: '启用',
    descCombatStatusBarEnabled: '战斗中显示耗时与速度控制栏',
    keyCombatStatusBarSpeedMultiplier: '速度倍率',
    descCombatStatusBarSpeedMultiplier: '战斗回放的默认速度倍率',
    toggleOn: '开',
    toggleOff: '关',
    modInstalledHint: '打开设置以配置和自定义 BazaarPlusPlus',
    settingsMissingPathTitle: '需要游戏路径',
    settingsMissingPathBody: '请先返回安装器执行检测，再打开设置页面。',
    settingsDefaultConfigTitle: '\u4f7f\u7528\u9ed8\u8ba4\u8bbe\u7f6e',
    settingsDefaultConfigBody: '\u5c1a\u672a\u68c0\u6d4b\u5230\u914d\u7f6e\u6587\u4ef6\u3002\u4f60\u5728\u8fd9\u91cc\u4f5c\u51fa\u7684\u4efb\u4f55\u4fee\u6539\u90fd\u4f1a\u81ea\u52a8\u521b\u5efa BazaarPlusPlus.cfg\u3002',
    settingsLoadErrorTitle: '无法读取设置',
    settingsLoadErrorBody: '请确认 BazaarPlusPlus 已正确安装，然后重新打开设置页面。',
    settingsDecreaseSpeed: '降低速度',
    settingsIncreaseSpeed: '提高速度',
  }
};

export function resolveInitialLocale(): Locale {
  if (typeof window === 'undefined') {
    return defaultLocale;
  }

  const saved = window.localStorage.getItem('locale');
  if (saved === 'en' || saved === 'zh') {
    return saved;
  }

  return window.navigator.language.toLowerCase().startsWith('zh') ? 'zh' : 'en';
}

export function formatMessage(
  locale: Locale,
  key: MessageKey,
  params?: Record<string, string | number>
): string {
  let text = messages[locale][key];
  if (!params) {
    return text;
  }

  for (const [name, value] of Object.entries(params)) {
    text = text.replaceAll(`{${name}}`, String(value));
  }

  return text;
}
