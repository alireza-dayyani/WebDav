import { readFileSync } from "node:fs";
import test from "node:test";
import assert from "node:assert/strict";
import { EDITOR_IFRAME_SANDBOX } from "../Front/src/editorEmbed.js";

test("document and letter editors embed the editor with a shared sandbox allowlist", () => {
  for (const file of ["App.vue", "LetterEditor.vue"]) {
    const source = readFileSync(
      new URL(`../Front/src/${file}`, import.meta.url),
      "utf8",
    );
    assert.match(source, /:sandbox="EDITOR_IFRAME_SANDBOX"/);
    assert.doesNotMatch(source, /<iframe[^>]*\ssandbox="/);
  }
  for (const token of [
    "allow-scripts",
    "allow-same-origin",
    "allow-downloads",
    "allow-popups",
  ]) {
    assert.match(EDITOR_IFRAME_SANDBOX, new RegExp(token));
  }
});