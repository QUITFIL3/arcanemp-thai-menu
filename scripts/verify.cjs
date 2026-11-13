const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const zlib = require("node:zlib");
const acorn = require("acorn");
const root = path.resolve(__dirname, "..");
const output = path.join(root, "dist");
const manifest = JSON.parse(
  fs.readFileSync(path.join(output, "patch-manifest.json"), "utf8"),
);
const payload = fs.readFileSync(path.join(output, "menu-payload.bin"));
const html = fs.readFileSync(path.join(output, "menu-th.html"), "utf8");
const sha = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex");
assert.equal(payload.length, manifest.length);
assert.equal(sha(payload), manifest.payloadSha256);
const encoded = payload.toString("utf8").match(/atob\("([A-Za-z0-9+/=]+)"\)/);
assert(encoded, "Compressed HTML loader was not found.");
assert.equal(
  zlib.gunzipSync(Buffer.from(encoded[1], "base64")).toString("utf8"),
  html,
);
assert(html.includes('<html lang="th">'));

function stripTranslatedText(node) {
  if (node === null || typeof node !== "object") return node;
  if (Array.isArray(node)) return node.map(stripTranslatedText);
  const result = {};
  for (const [key, value] of Object.entries(node)) {
    if (["start", "end", "raw"].includes(key)) continue;
    const text =
      (node.type === "Literal" &&
        typeof node.value === "string" &&
        key === "value") ||
      (node.type === "TemplateElement" && key === "value");
    result[key] = text ? "<translated text>" : stripTranslatedText(value);
  }
  return result;
}

function scripts(source) {
  return [...source.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/gi)].map(
    (match) => match[1],
  );
}

const before = scripts(
  fs.readFileSync(path.join(root, ".local", "original-menu.html"), "utf8"),
);
const after = scripts(html);
assert.equal(before.length, after.length);
for (let index = 0; index < before.length; index++) {
  assert.deepEqual(
    stripTranslatedText(
      acorn.parse(before[index], {
        ecmaVersion: "latest",
        sourceType: "module",
      }),
    ),
    stripTranslatedText(
      acorn.parse(after[index], {
        ecmaVersion: "latest",
        sourceType: "module",
      }),
    ),
    "The patch must preserve the JavaScript structure.",
  );
}

console.log(
  "Verified payload checksum, gzip round-trip and embedded JavaScript structure.",
);
