import { expect, test } from '@playwright/test';

test('the home page shows the API and the database healthy', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'API health' })).toBeVisible();
  // The database may still be starting; the page checks again, so allow a little time.
  await expect(page.getByTestId('api')).toHaveText('OK', { timeout: 60_000 });
  await expect(page.getByTestId('database')).toHaveText('OK', { timeout: 60_000 });
});

test('the mortgage calculator works end to end', async ({ page }) => {
  await page.goto('/calculator');

  await page.getByRole('button', { name: 'Calculate' }).click();

  // The page posts the default loan to the API and shows when it would be paid off.
  const summary = page.getByRole('region', { name: 'Summary' });
  await expect(summary.getByText('Paid off')).toBeVisible();
  await expect(summary.getByText('Interest saved')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Payment schedule' })).toBeVisible();
});
