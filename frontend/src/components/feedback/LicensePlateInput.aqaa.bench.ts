import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { bench, describe } from 'vitest';
import '../../i18n';
import { LicensePlateInput } from './LicensePlateInput';
import type { PlateValue } from './plate';

const noop = () => {};
let sink = 0;

function plate(len: number, country: PlateValue['country']): PlateValue {
  const raw = 'ab1-ÄÖ9 x'.repeat(Math.ceil(len / 9)).slice(0, len);
  return { country, type: 'Electric', district: raw, letters: raw, digits: raw, free: raw };
}

const sizes = [8, 256, 8192];

describe('LicensePlateInput render (DE fields, value length)', () => {
  for (const n of sizes) {
    const value = plate(n, 'DE');
    bench(`len=${n}`, () => {
      sink += renderToStaticMarkup(createElement(LicensePlateInput, { value, onChange: noop })).length;
    });
  }
});

describe('LicensePlateInput render (OTHER free field, value length)', () => {
  for (const n of sizes) {
    const value = plate(n, 'OTHER');
    bench(`len=${n}`, () => {
      sink += renderToStaticMarkup(createElement(LicensePlateInput, { value, onChange: noop })).length;
    });
  }
});

export { sink };
