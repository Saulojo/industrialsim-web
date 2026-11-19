import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { HealthStatus } from './HealthStatus';
import { navigate, plantIdFromPath, usePath } from './navigation';
import { PlantPage } from './plant/PlantPage';
import { ScenarioPicker } from './ScenarioPicker';

export function App() {
  const { t } = useTranslation();
  const path = usePath();
  const plantId = plantIdFromPath(path);

  useEffect(() => {
    document.title = t('app.title');
  }, [t]);

  return (
    <div className={plantId ? 'app app--wide' : 'app'}>
      <header className="app__header">
        <h1 className="app__title">
          <a
            className="app__home"
            href="/"
            onClick={(event) => {
              event.preventDefault();
              navigate('/');
            }}
          >
            {t('app.title')}
          </a>
        </h1>
        {!plantId && <p className="app__subtitle">{t('app.subtitle')}</p>}
      </header>
      <main>
        {plantId ? (
          <PlantPage key={plantId} plantId={plantId} />
        ) : (
          <div className="stack">
            <ScenarioPicker />
            <HealthStatus />
          </div>
        )}
      </main>
    </div>
  );
}
