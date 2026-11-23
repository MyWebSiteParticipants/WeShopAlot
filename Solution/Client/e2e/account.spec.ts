import { test, expect } from '@playwright/test';

// Sumaiya's test
/*
test('checkout requires login', async ({ page }) => {
  await page.goto('/checkout');

  await expect(page).toHaveURL(/\/account\/login\?returnUrl=%2Fcheckout/);
});
*/

/* Yash's test
test('login form rejects an invalid email', async ({ page }) => {
  await page.goto('/account/login');
  const signIn = page.getByRole('button', { name: 'Sign in' });
  await expect(signIn).toBeDisabled();

  await page.getByPlaceholder('Email Address').fill('not-an-email');
  await page.getByPlaceholder('Password').fill('Pa$$w0rd');
  await page.getByPlaceholder('Email Address').blur();   // marks the field touched, so the message shows

  await expect(page.getByText('Invalid email address')).toBeVisible();
  await expect(signIn).toBeDisabled();
});
*/