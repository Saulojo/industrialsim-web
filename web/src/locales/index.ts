import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ptBR from './pt-BR/translation.json';

// D16: textos em arquivos de recurso. Por ora só pt-BR (idioma de referência);
// en e es entram com a detecção de idioma do navegador (RF11, Fase 2).
export const defaultLanguage = 'pt-BR';
export const defaultNS = 'translation';

export const resources = {
  'pt-BR': { translation: ptBR },
} as const;

export async function initI18n(language: string = defaultLanguage): Promise<typeof i18n> {
  if (!i18n.isInitialized) {
    await i18n.use(initReactI18next).init({
      resources,
      lng: language,
      fallbackLng: defaultLanguage,
      defaultNS,
      interpolation: { escapeValue: false }, // o React já escapa
    });
  }
  return i18n;
}
