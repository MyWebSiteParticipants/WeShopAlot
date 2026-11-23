import { test, expect } from '@playwright/test';

test.describe('Shop', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/shop');
  });

  // Abdullah's test
  test('shows product cards', async ({ page }) => {
    const cards = page.locator('app-product-item');
    await expect(cards.first()).toBeVisible();
    expect(await cards.count()).toBeGreaterThan(0);
  }); 


 test('search finds a single product by name', async ({ page }) => {
    await page.getByPlaceholder('Search').fill('Blondie');
    await page.getByRole('button', { name: 'Search', exact: true }).click();

    const cards = page.locator('app-product-item');
    await expect(cards).toHaveCount(1);
    await expect(cards.first()).toContainText('Blondie');
  });


  // Nihal's test
/*
  test('adding a product updates the basket badge', async ({ page }) => {
    await page.getByRole('button', { name: 'Add to basket' }).first().click();

    await expect(page.getByRole('link', { name: 'Basket, 1 items' })).toBeVisible();
  });
*/  
});
