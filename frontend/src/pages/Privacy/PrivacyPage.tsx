import { Link } from 'react-router-dom';
import { Trans, useTranslation } from 'react-i18next';
import { LanguageToggle } from '../../components/LanguageToggle';

export function PrivacyPage() {
  const { t } = useTranslation();
  return (
    <div className="relative mx-auto max-w-3xl px-6 py-12">
      <div className="absolute right-6 top-6">
        <LanguageToggle variant="light" />
      </div>
      <h1 className="text-3xl font-bold text-gray-900">{t('privacy.title')}</h1>
      <p className="mt-2 text-sm text-gray-500">{t('privacy.subtitle')}</p>

      <div className="mt-8 space-y-8 text-sm text-gray-700 leading-relaxed">
        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section1')}</h2>
          <p className="mt-2">
            <Trans i18nKey="privacy.section1Body" components={{ strong: <strong /> }} />
          </p>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section2')}</h2>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li><Trans i18nKey="privacy.section2_1" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section2_2" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section2_3" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section2_4" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section2_5" components={{ strong: <strong /> }} /></li>
          </ul>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section3')}</h2>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li><Trans i18nKey="privacy.section3_1" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section3_2" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section3_3" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section3_4" components={{ strong: <strong /> }} /></li>
          </ul>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section4')}</h2>
          <p className="mt-2">
            <Trans i18nKey="privacy.section4Body" components={{ strong: <strong /> }} />
          </p>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section5')}</h2>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li>
              <Trans
                i18nKey="privacy.section5_1"
                components={{
                  strong: <strong />,
                  profileLink: <Link to="/profile" className="text-blue-600 hover:underline" />,
                }}
              />
            </li>
            <li><Trans i18nKey="privacy.section5_2" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section5_3" components={{ strong: <strong /> }} /></li>
            <li><Trans i18nKey="privacy.section5_4" components={{ strong: <strong /> }} /></li>
          </ul>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section6')}</h2>
          <p className="mt-2">{t('privacy.section6Body')}</p>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">{t('privacy.section7')}</h2>
          <p className="mt-2">
            <Trans i18nKey="privacy.section7Body" components={{ strong: <strong />, br: <br /> }} />
          </p>
        </section>
      </div>
    </div>
  );
}
