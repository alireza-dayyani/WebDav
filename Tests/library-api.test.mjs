import test from "node:test";
import assert from "node:assert/strict";
import { libraryPath } from "../Front/src/libraryApi.js";

test("libraryPath encodes search and preserves paging flags", () => {
  assert.equal(libraryPath(1, false, ""), "/library?page=1&trash=false&q=");
  assert.equal(libraryPath(2, true, "hello world"), "/library?page=2&trash=true&q=hello%20world");
  assert.equal(libraryPath(3, false, "کتاب"), "/library?page=3&trash=false&q=%DA%A9%D8%AA%D8%A7%D8%A8");
});
