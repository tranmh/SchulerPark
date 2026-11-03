import { test, expect } from '@playwright/test';

// WCAG 1.4.1 (axe rule link-in-text-block): a link inside running text must be
// distinguishable without relying on colour alone, i.e. it is underlined (or
// otherwise styled) or its colour contrasts at least 3:1 with the surrounding text.
test.describe('Privacy page accessibility', () => {
  test('profile link in the rights section is distinguishable from surrounding text', async ({ page }) => {
    await page.goto('/privacy');

    const link = page.getByRole('link', { name: 'profile page' });
    await expect(link).toBeVisible();

    const style = await link.evaluate((el) => {
      const parse = (c: string) => {
        const m = c.match(/rgba?\(([^)]+)\)/);
        if (m) return m[1].split(/[ ,/]+/).filter(Boolean).slice(0, 3).map(Number);
        // Modern browsers may report oklch()/color(); resolve through a canvas.
        const ctx = document.createElement('canvas').getContext('2d')!;
        ctx.fillStyle = c;
        ctx.fillRect(0, 0, 1, 1);
        return Array.from(ctx.getImageData(0, 0, 1, 1).data.slice(0, 3));
      };
      const lum = ([r, g, b]: number[]) => {
        const ch = (v: number) => {
          const s = v / 255;
          return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
        };
        return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b);
      };
      const linkStyle = getComputedStyle(el);
      const parentStyle = getComputedStyle(el.parentElement!);
      const l1 = lum(parse(linkStyle.color));
      const l2 = lum(parse(parentStyle.color));
      const contrast = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
      return {
        underlined: linkStyle.textDecorationLine.includes('underline'),
        borderBottom: parseFloat(linkStyle.borderBottomWidth) > 0 && linkStyle.borderBottomStyle !== 'none',
        contrast,
      };
    });

    const distinguishable = style.underlined || style.borderBottom || style.contrast >= 3;
    expect(
      distinguishable,
      `link is neither underlined nor 3:1 contrast with surrounding text (contrast ${style.contrast.toFixed(2)}:1)`,
    ).toBe(true);
  });
});
