import { COPY, type LocaleCopy } from "./catalog.ts";
import { translate, type SupportedLocale } from "../model/report.ts";

export interface CopyApi {
  copy: LocaleCopy;
  t: (key: string) => string;
}

export function copyForLocale(locale: SupportedLocale): CopyApi {
  const copy = COPY[locale] ?? COPY.en;
  return {
    copy,
    t: (key) => translate(copy, key),
  };
}
