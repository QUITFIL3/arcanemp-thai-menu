const fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict"),
  { pathToFileURL } = require("node:url");
const { chromium } = require("playwright");
const root = path.resolve(__dirname, ".."),
  output = path.join(root, "dist"),
  local = path.join(root, ".local");
fs.mkdirSync(local, { recursive: true });
fs.copyFileSync(
  path.join(output, "menu-payload.bin"),
  path.join(local, "embedded-th.html"),
);
async function main() {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({
      viewport: { width: 1600, height: 900 },
    }),
    errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.route(/^https?:/, (r) =>
    r.fulfill({ status: 200, contentType: "application/json", body: "[]" }),
  );
  await page.goto(pathToFileURL(path.join(local, "embedded-th.html")).href);
  await page.getByRole("button", { name: "การตั้งค่า", exact: true }).click();
  await page.getByRole("radio", { name: "กดเพื่อพูด", exact: true }).waitFor();
  await page.evaluate(async () => {
    await document.fonts.load('15px "Source Sans 3"', "กำลังตั้งค่าผู้เล่น");
    await document.fonts.ready;
  });
  const checks = [];
  for (const [label, name] of [
    ["แชตเสียง", "voice"],
    ["เสียงเกม", "audio"],
    ["กราฟิก", "graphics"],
    ["เกี่ยวกับ", "about"],
  ]) {
    await page.getByRole("button", { name: label, exact: true }).click();
    await page.waitForTimeout(75);
    const detail = await page.evaluate(() => ({
      lang: document.documentElement.lang,
      fontLoaded: document.fonts.check('15px "Source Sans 3"', "กำลังตั้งค่าผู้เล่น"),
      overflow: document.documentElement.scrollWidth > innerWidth,
      body: document.body.innerText,
    }));
    assert.equal(detail.lang, "th");
    assert(detail.fontLoaded);
    assert(!detail.overflow);
    assert(/[ก-๙]/.test(detail.body));
    await page.screenshot({
      path: path.join(output, "preview-" + name + ".png"),
    });
    checks.push({
      tab: name,
      lang: detail.lang,
      fontLoaded: detail.fontLoaded,
      horizontalOverflow: detail.overflow,
    });
  }
  await page.getByRole("button", { name: "แชตเสียง", exact: true }).click();
  await page.getByRole("radio", { name: "ส่งเสียงอัตโนมัติ", exact: true }).click();
  await page.getByText("ระดับเริ่มส่งเสียง", { exact: true }).waitFor();
  await page.getByRole("radio", { name: "กดเพื่อพูด", exact: true }).click();
  await page.getByText("หน่วงเวลาหลังปล่อยปุ่ม", { exact: true }).waitFor();
  // Native bridge mode: every settings change must retain its original protocol key.
  await page.addInitScript(() => {
    window.__events = [];
    const settings = {
      context: "frontend",
      voice: {
        enabled: true,
        volume: 1,
        inputDevice: "Microphone Test",
        transmitMode: "pushToTalk",
        pushToTalkKey: 86,
        pushToTalkReleaseDelay: 100,
        activationThreshold: 0.02,
        noiseSuppression: false,
      },
      voiceInfo: {
        devices: ["Microphone Test"],
        hasMicrophone: false,
        keyLabel: "V",
        capturing: false,
      },
      audio: {
        effects: 1,
        music: 1,
        voice: 1,
        muted: false,
        mono: false,
        subtitles: true,
        menuReaderEnabled: false,
        menuReaderVolume: 1,
      },
      graphics: { skinCacheMemory: 512, crowdDetail: 4 },
      about: {
        version: "1.13.0",
        git: "b66abf3",
        framework: "41.3.0",
        ping: 0,
        logs: "F:\\ArcaneMP-1.12.2\\client\\logs",
      },
      server: null,
    };
    window.callEvent = (name, payload) => {
      const parsed = payload ? JSON.parse(payload) : null;
      window.__events.push({ name, payload: parsed });
      if (name === "menu:ready")
        setTimeout(
          () =>
            window.arcanemp.dispatch("state", {
              version: "1.13.0",
              build: "b66abf3",
              nickname: "ผู้เล่นภาษาไทย",
              nicknameEditable: false,
              lastAddress: "127.0.0.1:27015",
              favorites: [],
              history: [],
              masterlistUrl: "https://example.invalid",
              gameMode: "hogwartsmp",
              offline: false,
              status: "",
              statusIsError: false,
            }),
          0,
        );
      if (name === "settings:ready")
        setTimeout(
          () => window.arcanemp.dispatch("settings:state", settings),
          0,
        );
    };
  });
  await page.goto(pathToFileURL(path.join(local, "embedded-th.html")).href);
  await page.getByRole("button", { name: "การตั้งค่า", exact: true }).click();
  await page.getByRole("switch", { name: "ลดเสียงรบกวน", exact: true }).click();
  const wire = await page.evaluate(() =>
    __events.find((e) => e.name === "settings:set"),
  );
  assert.deepEqual(wire.payload, {
    key: "voice.noiseSuppression",
    value: true,
  });
  assert.equal(
    await page
      .getByRole("textbox", { name: "ชื่อผู้เล่น", exact: true })
      .inputValue(),
    "ผู้เล่นภาษาไทย",
  );
  await page.setViewportSize({ width: 1280, height: 720 });
  const reset = page.getByRole("button", {
    name: "คืนค่าการตั้งค่าแชตเสียง",
    exact: true,
  });
  await reset.scrollIntoViewIfNeeded();
  const resetBounds = await reset.boundingBox();
  assert(resetBounds.y >= 0 && resetBounds.y + resetBounds.height <= 720);
  await page.screenshot({ path: path.join(output, "preview-720p.png") });
  assert(
    !(await page.evaluate(
      () => document.documentElement.scrollWidth > innerWidth,
    )),
  );
  await browser.close();
  assert.deepEqual(errors, []);
  const report = {
    loaderDecompressionPassed: true,
    settingsTabs: checks,
    voiceModeSwitchPassed: true,
    nativeSettingsPayloadPreserved: wire,
    userNicknamePreserved: true,
    allVoiceControlsReachableAt720p: true,
    pageErrors: errors,
  };
  fs.writeFileSync(
    path.join(output, "qa-report.json"),
    JSON.stringify(report, null, 2),
  );
  console.log(JSON.stringify(report, null, 2));
}
main().catch((e) => {
  console.error(e);
  process.exit(1);
});
