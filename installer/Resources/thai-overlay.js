(() => {
  "use strict";
  if (window.__arcaneThaiMenu) return;
  const catalog = __ARCANE_THAI_DICTIONARY__;
  const normalize = (value) => value.trim().replace(/\s+/g, " ");
  const dictionary = new Map(
    Object.entries(catalog).map(([en, th]) => [normalize(en), th.trim()]),
  );
  const skipped =
    "script,style,textarea,input,pre,code,kbd,svg,[contenteditable=true],.row__title,.row__name,.card__chosen,.card__rename,[data-no-translate]";
  const attributes = ["aria-label", "title", "placeholder"];
  let scheduled = false;
  let observer;

  function translated(value) {
    const key = normalize(value);
    const exact = dictionary.get(key);
    if (exact !== undefined)
      return value.match(/^\s*/)[0] + exact + value.match(/\s*$/)[0];
    let match = key.match(/^(\d+) server(?:\(s\)|s)? online$/i);
    if (match) return match[1] + " เซิร์ฟเวอร์ออนไลน์";
    match = key.match(/^(\d+) of (\d+) (files|mods ready)$/);
    if (match)
      return (
        match[1] +
        " จาก " +
        match[2] +
        (match[3] === "files" ? " ไฟล์" : " ม็อดพร้อมแล้ว")
      );
    return value;
  }

  function allowed(element) {
    if (!element || element.closest(skipped)) return false;
    // An option without an explicit value derives its value from its label.
    if (element.tagName === "OPTION" && !element.hasAttribute("value"))
      return false;
    return true;
  }

  function apply() {
    scheduled = false;
    observer.disconnect();
    try {
      const walker = document.createTreeWalker(
        document.body || document.documentElement,
        NodeFilter.SHOW_TEXT,
      );
      let node;
      while ((node = walker.nextNode())) {
        if (!allowed(node.parentElement) || !/[A-Za-z]/.test(node.nodeValue))
          continue;
        const next = translated(node.nodeValue);
        if (next !== node.nodeValue) node.nodeValue = next;
      }
      for (const element of document.querySelectorAll(
        "[aria-label],[title],[placeholder]",
      )) {
        if (element.closest("script,style,[data-no-translate]")) continue;
        for (const attribute of attributes) {
          const value = element.getAttribute(attribute);
          if (!value) continue;
          const next = translated(value);
          if (next !== value) element.setAttribute(attribute, next);
        }
      }
    } finally {
      observer.observe(document.documentElement, {
        childList: true,
        subtree: true,
        characterData: true,
        attributes: true,
        attributeFilter: attributes,
      });
    }
  }

  observer = new MutationObserver(() => {
    if (scheduled) return;
    scheduled = true;
    queueMicrotask(apply);
  });
  window.__arcaneThaiMenu = Object.freeze({ apply, installed: true });
  document.documentElement.lang = "th";
  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", apply, { once: true });
  else apply();
})();
