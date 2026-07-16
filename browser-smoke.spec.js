const { test, expect } = require('@playwright/test');

test('normal app pages remain the primary experience', async ({ page }) => {
  await page.goto('http://localhost:5176/Account/Login');
  await page.getByLabel('Username').fill('doctor');
  await page.getByLabel('Password').fill('doctor123');
  await Promise.all([
    page.waitForURL(url => !url.pathname.includes('/Account/Login')),
    page.getByRole('button', { name: 'Login' }).click()
  ]);

  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
  await expect(page.getByText('Online').first()).toBeVisible();

  await page.getByRole('link', { name: 'Patients' }).click();
  await expect(page).toHaveURL(/\/Patients$/);
  await expect(page.locator('.patient-card').first()).toBeVisible();

  await page.getByRole('link', { name: 'Check Ups' }).click();
  await expect(page).toHaveURL(/\/Records$/);
  await expect(page.getByRole('button', { name: 'New Checkup' })).toBeVisible();

  await page.goto('http://localhost:5176/offline.html');
  await expect(page).toHaveURL('http://localhost:5176/');
});
