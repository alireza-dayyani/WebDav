import test from "node:test";
import assert from "node:assert/strict";
import { createHttpClient } from "../Front/src/httpCore.js";

test("createHttpClient honors custom auth messages", () => {
  const { buildError } = createHttpClient({
    unauthorizedMessage: "Sign in",
    genericMessage: "Try again",
  });
  assert.equal(buildError({}, 401).message, "Sign in");
  assert.equal(buildError({ detail: "Denied" }, 403).message, "Denied");
  assert.equal(buildError({}, 500).message, "Try again");
});
