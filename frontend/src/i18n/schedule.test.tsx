import { describe, it, expect, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Trans } from 'react-i18next';
import i18n from './index';

/**
 * Phase 20 WP4: every user-facing mention of the lottery time and the confirmation
 * deadlines is interpolated from the server window — no locale may hardcode a time.
 */
describe('schedule texts', () => {
  const schedule = { lottery: '21:00', morning: '07:00', afternoon: '13:00' };

  afterEach(() => {
    void i18n.changeLanguage('en');
  });

  it.each(['en', 'de'])('%s: lotteryInfo / next2 / next3 render the interpolated times', async (lng) => {
    await i18n.changeLanguage(lng);
    render(
      <div>
        <p data-testid="info"><Trans i18nKey="booking.lotteryInfo" values={schedule} components={{ b: <b /> }} /></p>
        <p data-testid="next2"><Trans i18nKey="booking.next2" values={schedule} components={{ b: <b /> }} /></p>
        <p data-testid="next3"><Trans i18nKey="booking.next3" values={schedule} components={{ b: <b /> }} /></p>
      </div>
    );
    const info = screen.getByTestId('info').textContent ?? '';
    expect(info).toContain('21:00');
    expect(info).toContain('07:00');
    expect(info).toContain('13:00');
    expect(info).not.toMatch(/\{\{|22:00|06:00/);
    expect(screen.getByTestId('next2').textContent).toContain('21:00');
    expect(screen.getByTestId('next3').textContent).toContain('07:00');
  });

  it.each(['en', 'de'])('%s: no locale string still hardcodes 22:00 or 06:00', (lng) => {
    const bundle = JSON.stringify(i18n.getResourceBundle(lng, 'translation'));
    expect(bundle).not.toMatch(/22:00|06:00 Uhr|<b>06:00<\/b>/);
    expect(i18n.t('components.timeSlot.lotteryPending', { lng, time: '21:00' })).toContain('21:00');
  });
});
