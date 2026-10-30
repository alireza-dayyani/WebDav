import { test } from "node:test";
import assert from "node:assert/strict";
import { allPages } from "../Front/src/api.js";
import { beforeUnloadGuard } from "../Front/src/unsavedChanges.js";

test("template pagination includes later pages and preserves filters", async () => {
  const previous = globalThis.fetch;
  const calls = [];
  globalThis.fetch = async url => {
    calls.push(url);
    const page = Number(new URL(url, "http://test").searchParams.get("page"));
    return { ok: true, status: 200, json: async () => ({ items: Array.from({ length: page === 1 ? 50 : 10 }, (_, i) => (page - 1) * 50 + i), hasMore: page === 1 }) };
  };
  try {
    assert.equal((await allPages("/templates/?kind=document")).length, 60);
    assert.deepEqual(calls, ["/api/templates/?kind=document&page=1", "/api/templates/?kind=document&page=2"]);
  } finally { globalThis.fetch = previous; }
});
test("letter form unload protection follows saved and unsaved states", () => {
  let dirty = false, prevented = 0;
  const guard = beforeUnloadGuard(() => dirty);
  const event = { preventDefault() { prevented++; } };
  guard(event); assert.equal(prevented, 0);
  dirty = true; guard(event); assert.equal(prevented, 1); assert.equal(event.returnValue, "");
  dirty = false; guard(event); assert.equal(prevented, 1);
});
