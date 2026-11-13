const fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict"),
  crypto = require("node:crypto"),
  zlib = require("node:zlib");
const acorn = require("acorn"),
  subsetFont = require("subset-font");
const root = path.resolve(__dirname, ".."),
  out = path.join(root, "dist"),
  local = path.join(root, ".local");
fs.mkdirSync(local, { recursive: true });
const hash = (b) => crypto.createHash("sha256").update(b).digest("hex");
const clientArg = process.argv.indexOf("--client");
const client =
  clientArg >= 0
    ? process.argv[clientArg + 1]
    : process.env.ARCANEMP_CLIENT_DIR;
if (!client)
  throw new Error(
    "Usage: npm run build -- --client <ArcaneMP client directory>",
  );
const original = fs.readFileSync(path.resolve(client, "ArcaneMPClient.dll"));
const originalSha256 =
  "15aec613550d2017293416300d514dace14b3cad5ca76a0bf21f29cf589a335f";
assert.equal(
  hash(original),
  originalSha256,
  "Client binary changed; rebuild against the new version instead.",
);
const offset = original.indexOf("<!DOCTYPE html>");
assert.equal(offset, 14185216);
let end = original.indexOf("</html>", offset) + 7;
if (original.subarray(end, end + 2).equals(Buffer.from("\r\n"))) end += 2;
const originalHtml = original.subarray(offset, end).toString("utf8");
fs.writeFileSync(path.join(local, "original-menu.html"), originalHtml);
const dict = new Map(
  fs
    .readFileSync(path.join(root, "src/translations.tsv"), "utf8")
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => {
      const t = line.indexOf("\t");
      assert(t > 0);
      return [line.slice(0, t), line.slice(t + 1)];
    }),
);
const edits = [];
let count = 0,
  used = new Set();
function translateScript(code) {
  const start = code.indexOf("const Ms=");
  assert(start > 0);
  const tree = acorn.parse(code, {
    ecmaVersion: "latest",
    sourceType: "module",
  });
  const local = [];
  function walk(n, parent) {
    if (!n || typeof n !== "object") return;
    if (
      n.type === "Literal" &&
      typeof n.value === "string" &&
      n.start >= start &&
      dict.has(n.value)
    ) {
      // Keep keyboard identifiers, object keys and member lookups in the bridge unchanged.
      const key = parent?.type === "Property" && parent.key === n,
        member = parent?.type === "MemberExpression" && parent.property === n,
        keyboard =
          n.value === "Enter" &&
          (parent?.type !== "Property" ||
            (parent.key.name ?? parent.key.value) !== "label");
      if (!key && !member && !keyboard) {
        const translated = dict.get(n.value);
        local.push({
          start: n.start,
          end: n.end,
          value: JSON.stringify(translated),
        });
        used.add(n.value);
        edits.push({
          source: n.value,
          translated,
          context: code.slice(Math.max(0, n.start - 40), n.end + 30),
        });
        count++;
      }
    }
    if (n.type === "TemplateElement" && n.start >= start) {
      const templateText = new Map([
        [" of ", " จาก "],
        [" files", " ไฟล์"],
        [" mods ready", " ม็อดพร้อมแล้ว"],
        ["Server: ", "เซิร์ฟเวอร์: "],
        ["Reason: ", "เหตุผล: "],
        ["Your client: ", "ไคลเอนต์ของคุณ: "],
        ["Server build: ", "บิลด์เซิร์ฟเวอร์: "],
        ["The masterlist answered ", "รายชื่อส่วนกลางตอบกลับ: "],
        ["Remove ", "นำ "],
        [" from favorites", " ออกจากรายการโปรด"],
        ["Add ", "เพิ่ม "],
        [" to favorites", " ในรายการโปรด"],
        [" open ", " เซิร์ฟเวอร์เปิดอยู่ "],
        [" a different ArcaneMP than your ", " ใช้ ArcaneMP ต่างจากเวอร์ชันของคุณ "],
        [" letters", " ฉบับ"],
        ["Keep ", "เก็บ "],
        [" in the server list's Favorites.", " ไว้ในรายการโปรดของรายชื่อเซิร์ฟเวอร์"],
      ]);
      let raw = n.value.raw;
      for (const [en, th] of templateText) raw = raw.replaceAll(en, th);
      if (raw !== n.value.raw) {
        local.push({ start: n.start, end: n.end, value: raw });
        count++;
      }
    }
    for (const [key, value] of Object.entries(n)) {
      if (["start", "end", "loc"].includes(key)) continue;
      if (Array.isArray(value)) value.forEach((v) => walk(v, n));
      else if (value && typeof value === "object") walk(value, n);
    }
  }
  walk(tree, null);
  local.sort((a, b) => b.start - a.start);
  for (const e of local)
    code = code.slice(0, e.start) + e.value + code.slice(e.end);
  acorn.parse(code, { ecmaVersion: "latest", sourceType: "module" });
  return code;
}
async function main() {
  let html = originalHtml
    .replace('<html lang="en">', '<html lang="th">')
    .replace(
      /<script([^>]*)>([\s\S]*?)<\/script>/gi,
      (all, attrs, code) =>
        "<script" + attrs + ">" + translateScript(code) + "</script>",
    );
  let characters = "";
  for (let cp = 0x0e00; cp <= 0x0e7f; cp++)
    characters += String.fromCodePoint(cp);
  characters += "\u200b\u25cc";
  const font = await subsetFont(
    fs.readFileSync(path.join(root, "assets/NotoSansThai-Variable.ttf")),
    characters,
    {
      targetFormat: "woff2",
      variationAxes: { wdth: 100, wght: { min: 400, max: 700, default: 400 } },
    },
  );
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, "ArcaneThai.woff2"), font);
  const fontUrl = "data:font/woff2;base64," + font.toString("base64");
  const css = `\n/* Thai-only font faces retain the original Latin lettering and all controls. */\n@font-face{font-family:"Source Sans 3";font-style:normal;font-weight:400 700;font-display:swap;src:url(${fontUrl}) format("woff2");unicode-range:U+0E00-0E7F,U+200B,U+25CC}\n@font-face{font-family:Marcellus;font-style:normal;font-weight:400 700;font-display:swap;src:url(${fontUrl}) format("woff2");unicode-range:U+0E00-0E7F,U+200B,U+25CC}\nhtml:lang(th) :is(h1,h2,h3,button,label,select,.tab,.list__title,.card__label,.stack__label,.menu__keys,.or){letter-spacing:normal;line-height:1.6}html:lang(th) :is(p,.menu__help,.settings__lede){line-height:1.7}html:lang(th) input{line-height:1.6}html:lang(th) .menu__row{gap:18px}html:lang(th) .menu__row>div:first-child{min-width:0}html:lang(th) .settings__title{line-height:1.6}\n`;
  html = html
    .replace("</style>", css + "</style>")
    .replace('new Intl.DisplayNames(["en"]', 'new Intl.DisplayNames(["th"]')
    .replace(
      'new Intl.RelativeTimeFormat("en"',
      'new Intl.RelativeTimeFormat("th"',
    );
  fs.writeFileSync(path.join(out, "menu-th.html"), html);
  const compressed = zlib.gzipSync(Buffer.from(html), { level: 9 });
  assert.equal(zlib.gunzipSync(compressed).toString(), html);
  const bootstrap = `<!DOCTYPE html><html lang="th"><head><meta charset="utf-8"><title>ArcaneMP</title></head><body><script>(async()=>{const b=Uint8Array.from(atob("${compressed.toString("base64")}"),c=>c.charCodeAt(0));const h=await new Response(new Blob([b]).stream().pipeThrough(new DecompressionStream("gzip"))).text();document.open();document.write(h);document.close()})().catch(e=>{document.body.textContent="โหลดเมนูภาษาไทยไม่สำเร็จ: "+e.message})</script></body></html>`;
  const bytes = Buffer.from(bootstrap),
    length = end - offset;
  assert(
    bytes.length < length,
    `Translated payload ${bytes.length} exceeds ${length}`,
  );
  const payload = Buffer.alloc(length, 0x20);
  bytes.copy(payload);
  fs.writeFileSync(path.join(out, "menu-payload.bin"), payload);
  const patched = Buffer.from(original);
  payload.copy(patched, offset);
  assert.equal(patched.length, original.length);
  assert.deepEqual(patched.subarray(0, offset), original.subarray(0, offset));
  assert.deepEqual(patched.subarray(end), original.subarray(end));
  const report = {
    patchVersion: "0.1",
    clientVersion: "1.13.0 b66abf3",
    originalSha256,
    patchedSha256: hash(patched),
    offset,
    length,
    bootstrapBytes: bytes.length,
    translatedHtmlBytes: Buffer.byteLength(html),
    fontBytes: font.length,
    translations: count,
    uniqueTranslations: used.size,
    codeOutsideEmbeddedHtmlUnchanged: true,
    originalHtmlSha256: hash(original.subarray(offset, end)),
    payloadSha256: hash(payload),
    compressedRoundTrip: true,
  };
  fs.writeFileSync(
    path.join(out, "patch-manifest.json"),
    JSON.stringify(report, null, 2),
  );
  fs.writeFileSync(
    path.join(local, "edits.json"),
    JSON.stringify(edits, null, 2),
  );
  fs.copyFileSync(
    path.join(root, "assets/OFL-NotoSansThai.txt"),
    path.join(out, "OFL-NotoSansThai.txt"),
  );
  for (const name of ["Install-ThaiMenu.ps1", "Restore-OriginalMenu.ps1"])
    fs.copyFileSync(path.join(root, "templates", name), path.join(out, name));
  console.log(JSON.stringify(report, null, 2));
}
main().catch((e) => {
  console.error(e);
  process.exit(1);
});
