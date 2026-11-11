import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { HealthStatus } from './HealthStatus';

export function App() {
  const { t } = useTranslation();

  useEffect(() => {
    document.title = t('app.title');
  }, [t]);

  return (
    <div className="app">
      <header className="app__header">
        <h1 className="app__title">{t('app.title')}</h1>
        <p className="app__subtitle">{t('app.subtitle')}</p>
      </header>
      <main>
        <HealthStatus />
      </main>
    </div>
  );
}
