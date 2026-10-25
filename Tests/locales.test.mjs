import { test } from "node:test";
import assert from "node:assert/strict";
import { messages, languages, translate, normalizeSearch } from "../Front/src/locales.js";

test("every site language covers the full catalog and preserves placeholders", () => {
  const keys = Object.keys(messages.en).sort();
  for (const { code } of languages) {
    assert.deepEqual(Object.keys(messages[code]).sort(), keys, code);
    for (const key of keys) {
      assert.ok(messages[code][key].trim(), `${code}.${key}`);
      assert.deepEqual(messages[code][key].match(/\{\w+\}/g), messages.en[key].match(/\{\w+\}/g), `${code}.${key}`);
    }
  }
  assert.equal(languages.find(l => l.code === "fa").dir, "rtl");
  assert.deepEqual(languages.map(l => l.code), ["en", "fa"]);
  assert.equal(messages.ar, undefined, "Arabic is not a website interface locale");
  assert.equal(translate("fa", "open", { title: "سند من" }), "باز کردن سند من");
});

test("Persian title search accepts Arabic and Persian keyboard variants", () => {
  assert.equal(normalizeSearch("كتاب يكم"), normalizeSearch("کتاب یکم"));
  assert.equal(normalizeSearch("DRAFT"), normalizeSearch("draft"));
});
