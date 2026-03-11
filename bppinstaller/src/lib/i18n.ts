export type Locale = 'en' | 'zh';

export type MessageKey =
  | 'htmlLang'
  | 'pageTitle'
  | 'kicker'
  | 'subtitle'
  | 'localeSwitch'
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
  | 'statusOptionalNotFound'
  | 'runtimeVersion'
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
  | 'footer';

export const defaultLocale: Locale = 'zh';

export const messages: Record<Locale, Record<MessageKey, string>> = {
  en: {
    htmlLang: 'en',
    pageTitle: 'BazaarPlusPlus Installer',
    kicker: 'Arcane Foundry',
    subtitle: 'Mod Installation Rite',
    localeSwitch: '中文',
    stepBpp: 'BazaarPlusPlus',
    stepDotnet: '.NET Runtime',
    stepBazaar: 'The Bazaar',
    stepActions: 'Actions',
    statusInstalled: 'Installed',
    statusChecking: 'Checking...',
    statusNotInstalled: 'Not installed',
    installedVersion: 'Installed version: {version}',
    detectInstalledHint: 'Detect to inspect the installed BazaarPlusPlus payload.',
    updateAvailable: 'Update available: {version}',
    updateHint: 'Check the latest remote version for BazaarPlusPlus.',
    actionCheckingUpdates: 'Checking...',
    actionUpdating: 'Updating...',
    actionCheckUpdates: 'Check updates',
    actionUpdate: 'Update',
    statusOptionalNotFound: 'Optional - not found',
    runtimeVersion: 'Runtime: {version}',
    runtimeNotFound: 'No compatible .NET runtime was detected',
    runtimeIdle: 'Run detection to inspect the local runtime',
    statusFound: 'Found',
    actionReenter: 'Re-enter',
    actionBrowse: 'Browse',
    placeholderGamePath: 'Game install path...',
    actionCheck: 'Check',
    errorGamePath: 'TheBazaar was not found in this folder. Verify the install path.',
    actionDetecting: 'Detecting',
    actionDetect: 'Detect',
    actionInstalling: 'Installing...',
    actionReinstall: 'Reinstall',
    actionInstall: 'Install',
    actionUninstalling: 'Uninstalling...',
    actionUninstall: 'Uninstall',
    footer: 'BazaarPlusPlus · Arcane Foundry'
  },
  zh: {
    htmlLang: 'zh-CN',
    pageTitle: 'BazaarPlusPlus 安装器',
    kicker: '奥术工坊',
    subtitle: '模组安装仪式',
    localeSwitch: 'EN',
    stepBpp: 'BazaarPlusPlus',
    stepDotnet: '.NET 运行时',
    stepBazaar: 'The Bazaar',
    stepActions: '操作',
    statusInstalled: '已安装',
    statusChecking: '检查中...',
    statusNotInstalled: '未安装',
    installedVersion: '已安装版本：{version}',
    detectInstalledHint: '点击检测以检查当前 BazaarPlusPlus 安装状态。',
    updateAvailable: '发现更新：{version}',
    updateHint: '检查 BazaarPlusPlus 的远程最新版本。',
    actionCheckingUpdates: '检查中...',
    actionUpdating: '更新中...',
    actionCheckUpdates: '检查更新',
    actionUpdate: '更新',
    statusOptionalNotFound: '可选 - 未找到',
    runtimeVersion: '运行时：{version}',
    runtimeNotFound: '未检测到兼容的 .NET 运行时',
    runtimeIdle: '运行检测以检查本机 .NET 运行时',
    statusFound: '已找到',
    actionReenter: '重新选择',
    actionBrowse: '浏览',
    placeholderGamePath: '游戏安装路径...',
    actionCheck: '检查',
    errorGamePath: '该目录中未找到 TheBazaar，请确认路径是否正确。',
    actionDetecting: '检测中',
    actionDetect: '检测',
    actionInstalling: '安装中...',
    actionReinstall: '重新安装',
    actionInstall: '安装',
    actionUninstalling: '卸载中...',
    actionUninstall: '卸载',
    footer: 'BazaarPlusPlus · 奥术工坊'
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
