import { writable } from 'svelte/store';
import { resolveInitialLocale, messages, formatMessage, type Locale, type MessageKey, defaultLocale } from './i18n';

function createLocaleStore() {
  const { subscribe, set, update } = writable<Locale>(defaultLocale);

  function apply(nextLocale: Locale) {
    set(nextLocale);
    if (typeof document !== 'undefined') {
      document.documentElement.lang = messages[nextLocale].htmlLang;
    }
    if (typeof window !== 'undefined') {
      window.localStorage.setItem('locale', nextLocale);
    }
  }

  function toggle() {
    update((current) => {
      const next: Locale = current === 'zh' ? 'en' : 'zh';
      apply(next);
      return next;
    });
  }

  function init() {
    apply(resolveInitialLocale());
  }

  return { subscribe, apply, toggle, init };
}

export const locale = createLocaleStore();

export function handleLocaleToggle(event: MouseEvent) {
  event.preventDefault();
  event.stopPropagation();
  locale.toggle();
}

export function t(currentLocale: Locale, key: MessageKey, params?: Record<string, string | number>): string {
  return formatMessage(currentLocale, key, params);
}
