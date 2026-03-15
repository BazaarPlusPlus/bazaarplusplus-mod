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
  | 'modInstalledHint';

export const defaultLocale: Locale = 'zh';

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
    modInstalledHint: 'BazaarPlusPlus is installed and ready to use.',
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
    modInstalledHint: 'BazaarPlusPlus 已安装并可直接使用。',
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

  return defaultLocale;
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
