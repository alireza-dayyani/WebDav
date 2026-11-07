(function() {
  "use strict";
  function createHttpClient({
    basePath = "/api",
    accountHeader,
    csrfUnavailableMessage = "Cannot connect to your workspace. Please try again.",
    unauthorizedMessage = "Please sign in to continue.",
    genericMessage = "Something went wrong. Please try again."
  } = {}) {
    let token;
    async function ensureCsrf(force = false) {
      if (!force && token !== void 0) return;
      const response = await fetch(`${basePath}/csrf`, {
        credentials: "same-origin"
      });
      if (!response.ok) throw new Error(csrfUnavailableMessage);
      token = (await response.json()).token;
    }
    function resetCsrf() {
      token = void 0;
    }
    function buildError(problem, status) {
      const error = new Error(
        problem.errors ? Object.values(problem.errors).flat().join(" ") : problem.detail || (status === 401 ? unauthorizedMessage : genericMessage)
      );
      error.status = status;
      error.code = problem.code;
      error.maxBytes = problem.maxBytes;
      error.issues = problem.issues;
      return error;
    }
    async function requestJson(path, options = {}) {
      const method = options.method || "GET";
      const form = typeof FormData !== "undefined" && options.body instanceof FormData;
      const scope = accountHeader?.();
      const response = await fetch(basePath + path, {
        ...options,
        credentials: "same-origin",
        headers: {
          ...!form && options.body !== void 0 ? { "Content-Type": "application/json" } : {},
          ...method !== "GET" && method !== "HEAD" ? { "X-CSRF-TOKEN": token } : {},
          ...scope ? { "X-Inkstone-Account": scope } : {},
          ...options.headers
        },
        body: form || options.body === void 0 ? options.body : typeof options.body === "string" ? options.body : JSON.stringify(options.body)
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        if (response.status === 400) resetCsrf();
        throw buildError(problem, response.status);
      }
      return response;
    }
    async function requestData2(path, options = {}) {
      const method = options.method || "GET";
      if (method !== "GET" && method !== "HEAD") await ensureCsrf(false);
      const response = await requestJson(path, options);
      return response.status === 204 ? null : response.json();
    }
    return { ensureCsrf, resetCsrf, requestJson, requestData: requestData2, buildError };
  }
  const bindings = /* @__PURE__ */ new Map();
  const conflicts = /* @__PURE__ */ new Set();
  let initial, requested = false, saving = false, lastState = "", currentGeneration, preferenceScope = "", letterMode = false;
  const { requestData } = createHttpClient({
    accountHeader: () => preferenceScope,
    csrfUnavailableMessage: "Your session is unavailable. Sign in again in another tab, then retry Save.",
    unauthorizedMessage: "Your session expired. Sign in again in another tab, then retry Save.",
    genericMessage: "Cannot save your work. Check your connection and try again."
  });
  function notify(type, data = {}) {
    window.parent.postMessage(
      { type: "inkstone:" + type, ...data },
      location.origin
    );
  }
  window.inkstoneHost = {
    async load() {
      try {
        const parameters = new URLSearchParams(location.search);
        letterMode = parameters.has("letter");
        const id = parameters.get(letterMode ? "letter" : "id");
        if (!/^[a-f\d-]{36}$/i.test(id || ""))
          throw new Error("Select a document from your workspace.");
        const version = parameters.get("version");
        if (letterMode && !/^[1-9]\d{0,18}$/.test(version || ""))
          throw new Error("Open this letter from its saved revision.");
        const account = await requestData("/account/me");
        preferenceScope = account.email;
        const snapshot = await requestData(
          letterMode ? `/letters/${id}/editor?version=${version}` : "/documents/" + id + "/snapshot"
        );
        initial = snapshot.metadata;
        return JSON.stringify({
          document: snapshot.document,
          title: initial.title,
          author: account.email,
          bookmark: snapshot.bodyBookmark
        });
      } catch (error) {
        notify("error", {
          message: error.message,
          status: error.status,
          code: error.code,
          maxBytes: error.maxBytes
        });
        throw error;
      }
    },
    preferenceScope() {
      return preferenceScope;
    },
    bind(generation) {
      bindings.set(generation, initial);
      currentGeneration = generation;
      notify("ready", { document: initial });
    },
    async save(json, generation, title) {
      if (saving) throw new Error("A save is already in progress.");
      saving = true;
      try {
        if (conflicts.has(generation))
          throw new Error(
            "This document changed in another tab. Download your work using File → Save As, then reopen the saved document."
          );
        const account = await requestData("/account/me");
        if (account.email !== preferenceScope)
          throw Object.assign(
            new Error(
              "The signed-in account changed. Sign in to the original account before saving this document."
            ),
            { code: "account_changed" }
          );
        let document = bindings.get(generation);
        if (!document) {
          if (letterMode)
            throw new Error(
              "Create another letter from Letters. Use File → Save As to keep this opened document separately, then reopen your saved letter."
            );
          document = await requestData("/documents/", {
            method: "POST",
            body: JSON.stringify({
              title: title.slice(0, 160) || "Untitled document"
            })
          });
          bindings.set(generation, document);
        }
        const saved = await requestData(
          letterMode ? `/letters/${document.id}/editor` : `/documents/${document.id}/content`,
          {
            method: "PUT",
            body: JSON.stringify({
              version: document.version,
              document: JSON.parse(json)
            })
          }
        );
        bindings.set(generation, saved);
        if (generation === currentGeneration) notify("saved", { document: saved });
      } catch (error) {
        if (error.status === 409) conflicts.add(generation);
        if (generation === currentGeneration)
          notify("error", {
            message: error.message,
            status: error.status,
            code: error.code,
            maxBytes: error.maxBytes
          });
        throw error;
      } finally {
        saving = false;
      }
    },
    takeSaveRequest() {
      const value = requested;
      requested = false;
      return value;
    },
    canAutosave() {
      return !conflicts.has(currentGeneration);
    },
    state(dirty, pending, generation) {
      currentGeneration = generation;
      const state = `${dirty}/${pending}/${generation}`;
      if (state !== lastState) {
        lastState = state;
        notify("state", { dirty, pending });
      }
    }
  };
  window.addEventListener("message", (event) => {
    if (event.origin === location.origin && event.source === window.parent && event.data?.type === "inkstone:save-request")
      requested = true;
  });
})();
