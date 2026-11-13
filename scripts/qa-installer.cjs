const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const root = path.resolve(__dirname, "..");
const dist = path.join(root, "dist");
const local = path.join(root, ".local");
const report = JSON.parse(
  fs
    .readFileSync(path.join(dist, "installer-tests.json"), "utf8")
    .replace(/^\uFEFF/, ""),
);
const patchedHtml = fs.readFileSync(report.MenuForBrowser, "utf8");
const originalHtml = fs.readFileSync(
  path.join(report.TestRoot, "original-menu.html"),
  "utf8",
);
assert.equal(
  patchedHtml.replace(
    /<!-- arcane-thai-menu:runtime-v2:start -->[\s\S]*?<!-- arcane-thai-menu:runtime-v2:end -->/,
    "",
  ),
  originalHtml,
);
const bytes = fs.readFileSync(report.PatchedDllForBrowser);
const start = bytes.indexOf("<!DOCTYPE html>");
assert(start >= 0);
const end = bytes.indexOf(0, start);
assert(end > start);
fs.mkdirSync(local, { recursive: true });
fs.writeFileSync(
  path.join(local, "installer-embedded.html"),
  bytes.subarray(start, end),
);

// Reuse the same settings and wire-protocol checks against the new EXE's actual payload.
let code = fs.readFileSync(path.join(__dirname, "qa.cjs"), "utf8");
const copyStart = code.indexOf("fs.copyFileSync(");
const mainStart = code.indexOf("async function main()");
assert(copyStart >= 0 && mainStart > copyStart);
code = code.slice(0, copyStart) + code.slice(mainStart);
code = code.replaceAll('"embedded-th.html"', '"installer-embedded.html"');
code = code.replaceAll('"preview-"', '"installer-menu-preview-"');
code = code.replace('"preview-720p.png"', '"installer-menu-preview-720p.png"');
code = code.replace('"qa-report.json"', '"installer-menu-qa.json"');
code = code
  .replace("'ผู้เล่นภาษาไทย'", "'Voice chat'")
  .replaceAll('"ผู้เล่นภาษาไทย"', '"Voice chat"');
const generated = path.join(__dirname, "../.local/qa-installer-generated.cjs");
// The shared QA resolves root from __dirname; .local is one level under the repo as scripts is.
fs.writeFileSync(generated, code);
require(generated);
