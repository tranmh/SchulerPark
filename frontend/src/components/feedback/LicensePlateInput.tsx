import { useTranslation } from 'react-i18next';
import type { PlateCountry, PlateType } from '../../types/feedback';
import { PLATE_SUFFIX as SUFFIX, type PlateValue } from './plate';

const COUNTRIES: { code: PlateCountry; stripes: string[] }[] = [
  { code: 'DE', stripes: ['bg-black', 'bg-red-600', 'bg-yellow-400'] },
  { code: 'AT', stripes: ['bg-red-600', 'bg-white', 'bg-red-600'] },
  { code: 'LU', stripes: ['bg-red-500', 'bg-white', 'bg-sky-400'] },
  { code: 'OTHER', stripes: [] },
];

const TYPES: PlateType[] = ['Standard', 'Historic', 'Electric'];

const fieldClass =
  'min-w-0 rounded-md border-2 border-brand-500/60 bg-white px-2 py-1.5 text-center text-[22px] font-bold uppercase tracking-wide text-ink-900 placeholder:text-ink-200 num focus:border-brand-500 focus:outline-none';

const lettersOnly = (s: string, max: number) => s.toUpperCase().replace(/[^A-ZÄÖÜ]/g, '').slice(0, max);
const digitsOnly = (s: string, max: number) => s.replace(/\D/g, '').slice(0, max);

interface Props {
  value: PlateValue;
  onChange: (value: PlateValue) => void;
}

/** Country + plate type + plate fields, styled after a real number plate. */
export function LicensePlateInput({ value, onChange }: Props) {
  const { t } = useTranslation();
  const set = (patch: Partial<PlateValue>) => onChange({ ...value, ...patch });
  const suffix = SUFFIX[value.type];

  return (
    <div className="rounded-lg border border-line-strong bg-white p-3 sm:p-4">
      <div className="text-center text-[12.5px] font-medium text-ink-700">{t('feedback.plateCountry')}</div>
      <div className="mt-2 flex justify-center gap-2" role="radiogroup" aria-label={t('feedback.plateCountry')}>
        {COUNTRIES.map(({ code, stripes }) => (
          <button
            key={code}
            type="button"
            role="radio"
            aria-checked={value.country === code}
            aria-label={t(`feedback.countries.${code}`)}
            title={t(`feedback.countries.${code}`)}
            onClick={() => set({ country: code })}
            className={`h-9 w-14 overflow-hidden rounded-md ring-offset-2 transition-shadow ${
              value.country === code ? 'ring-2 ring-ink-900' : 'ring-1 ring-line-strong hover:ring-ink-400'
            }`}
          >
            {stripes.length ? (
              <span className="flex h-full w-full flex-col" aria-hidden="true">
                {stripes.map((c, i) => (
                  <span key={i} className={`flex-1 ${c}`} />
                ))}
              </span>
            ) : (
              <span className="grid h-full w-full place-items-center bg-surface-sunken text-[16px] font-bold text-ink-400" aria-hidden="true">
                ?
              </span>
            )}
          </button>
        ))}
      </div>

      <div className="mt-3 text-center text-[12.5px] font-medium text-ink-700">{t('feedback.plateType')}</div>
      <div className="mx-auto mt-2 flex max-w-sm rounded-lg border border-line-strong p-0.5" role="radiogroup" aria-label={t('feedback.plateType')}>
        {TYPES.map((type) => (
          <button
            key={type}
            type="button"
            role="radio"
            aria-checked={value.type === type}
            onClick={() => set({ type })}
            className={`flex-1 rounded-md px-2 py-1.5 text-[12px] font-medium ${
              value.type === type ? 'bg-ink-900 text-white' : 'text-ink-500 hover:bg-line/60'
            }`}
          >
            {t(`feedback.plateTypes.${type}`)}
            {SUFFIX[type] && (
              <span className={`ml-1 rounded px-1 text-[10.5px] ${value.type === type ? 'bg-white/20' : 'bg-line'}`}>
                {SUFFIX[type]}
              </span>
            )}
          </button>
        ))}
      </div>

      <div className="mt-3 flex items-stretch overflow-hidden rounded-lg border-2 border-ink-900 bg-white shadow-sm">
        <div className="flex w-8 shrink-0 flex-col items-center justify-end bg-blue-700 pb-1 text-[13px] font-bold text-white" aria-hidden="true">
          {value.country === 'DE' ? 'D' : value.country === 'AT' ? 'A' : value.country === 'LU' ? 'L' : ''}
        </div>
        {value.country === 'DE' ? (
          <div className="flex min-w-0 flex-1 items-center gap-1.5 p-1.5">
            <input
              aria-label={t('feedback.plateDistrict')}
              value={value.district}
              onChange={(e) => set({ district: lettersOnly(e.target.value, 3) })}
              placeholder="XX"
              className={`${fieldClass} w-0 flex-[3]`}
            />
            <span className="h-3 w-3 shrink-0 rounded-full border border-line-strong bg-surface-sunken" aria-hidden="true" />
            <input
              aria-label={t('feedback.plateLetters')}
              value={value.letters}
              onChange={(e) => set({ letters: lettersOnly(e.target.value, 2) })}
              placeholder="A"
              className={`${fieldClass} w-0 flex-[2]`}
            />
            <input
              aria-label={t('feedback.plateDigits')}
              value={value.digits}
              onChange={(e) => set({ digits: digitsOnly(e.target.value, 4) })}
              inputMode="numeric"
              placeholder="1234"
              className={`${fieldClass} w-0 flex-[4]`}
            />
            {suffix && <span className="shrink-0 text-[22px] font-bold text-ink-900">{suffix}</span>}
          </div>
        ) : (
          <div className="flex min-w-0 flex-1 p-1.5">
            <input
              aria-label={t('feedback.plateFree')}
              value={value.free}
              onChange={(e) => set({ free: e.target.value.toUpperCase().slice(0, 20) })}
              placeholder={t('feedback.plateFree')}
              className={`${fieldClass} w-full`}
            />
          </div>
        )}
      </div>
    </div>
  );
}
