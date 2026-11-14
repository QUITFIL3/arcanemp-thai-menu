const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");
const root = path.resolve(__dirname, "..");

(async () => {
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    // Offline preview: no account, real server list, or player information is fetched.
    await page.route(/^https?:/, (route) => route.fulfill({ status: 200, contentType: "application/json", body: "[]" }));
    await page.goto(pathToFileURL(path.join(root, ".local/installer-embedded.html")).href);
    await page.getByRole("button", { name: "การตั้งค่า", exact: true }).waitFor();
    await page.evaluate(async () => { await document.fonts.load('15px "Source Sans 3"', "ภาษาไทย"); await document.fonts.ready; });
    for (const [label, name] of [["เล่น", "play"], ["ไปรษณีย์นกฮูก", "owl-post"]]) {
      await page.getByRole("button", { name: label, exact: true }).click();
      if (name === "play") {
        await page.locator(".row__name").first().waitFor();
        await page.locator(".row__name").filter({ hasText: "The Great Hall" }).click();
      }
      await page.waitForTimeout(150);
      assert.equal(await page.locator("html").getAttribute("lang"), "th");
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
      await page.screenshot({ path: path.join(root, "docs", `preview-${name}.png`) });
    }
    assert.deepEqual(errors, []);
    console.log("Saved Thai Play and Owl Post previews from the embedded menu HTML.");
  } finally { await browser.close(); }
})().catch((error) => { console.error(error); process.exitCode = 1; });
