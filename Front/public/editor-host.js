/* Same-origin bridge between the Rust editor and the account API. No document data is kept in browser storage. */
(() => {
  const bindings = new Map();
  let initial,
    token,
    requested = false,
    saving = false,
    conflict = false,
    lastState = "",
    currentGeneration,
    preferenceScope = "",
    letterMode = false;
  function notify(type, data = {}) {
    window.parent.postMessage(
      { type: "inkstone:" + type, ...data },
      location.origin,
    );
  }
  async function request(path, options = {}) {
    if (options.method && !token) {
      const response = await fetch("/api/csrf", { credentials: "same-origin" });
      if (!response.ok)
        throw new Error(
          "Your session is unavailable. Sign in again in another tab, then retry Save.",
        );
      token = (await response.json()).token;
    }
    const response = await fetch("/api" + path, {
      ...options,
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-TOKEN": token || "",
        ...options.headers,
      },
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if (response.status === 409) conflict = true;
      if (response.status === 400) token = null;
      throw Object.assign(
        new Error(
          problem.detail ||
            (response.status === 401
              ? "Your session expired. Sign in again in another tab, then retry Save."
              : "Cannot save your work. Check your connection and try again."),
        ),
        {
          status: response.status,
          code: problem.code,
          maxBytes: problem.maxBytes,
        },
      );
    }
    return response.status === 204 ? null : response.json();
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
        const [snapshot, account] = await Promise.all([
          request(
            letterMode
              ? `/letters/${id}/editor?version=${version}`
              : "/documents/" + id + "/snapshot",
          ),
          request("/account/me"),
        ]);
        initial = snapshot.metadata;
        preferenceScope = account.email;
        return JSON.stringify({
          document: snapshot.document,
          title: initial.title,
          author: account.email,
          bookmark: snapshot.bodyBookmark,
        });
      } catch (error) {
        notify("error", {
          message: error.message,
          status: error.status,
          code: error.code,
          maxBytes: error.maxBytes,
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
        if (conflict)
          throw new Error(
            "This document changed in another tab. Download your work using File → Save As, then reopen the saved document.",
          );
        let document = bindings.get(generation);
        // New/Open in the editor creates a separate account document; never overwrite the previous one.
        if (!document) {
          if (letterMode)
            throw new Error(
              "Create another letter from Letters. Use File → Save As to keep this opened document separately, then reopen your saved letter.",
            );
          document = await request("/documents/", {
            method: "POST",
            body: JSON.stringify({
              title: title.slice(0, 160) || "Untitled document",
            }),
          });
          bindings.set(generation, document);
        }
        const saved = await request(
          letterMode
            ? `/letters/${document.id}/editor`
            : `/documents/${document.id}/content`,
          {
            method: "PUT",
            body: JSON.stringify({
              version: document.version,
              document: JSON.parse(json),
            }),
          },
        );
        bindings.set(generation, saved);
        if (generation === currentGeneration)
          notify("saved", { document: saved });
      } catch (error) {
        notify("error", {
          message: error.message,
          status: error.status,
          code: error.code,
          maxBytes: error.maxBytes,
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
      return !conflict;
    },
    state(dirty, pending, generation) {
      currentGeneration = generation;
      const state = `${dirty}/${pending}/${generation}`;
      if (state !== lastState) {
        lastState = state;
        notify("state", { dirty, pending });
      }
    },
  };
  window.addEventListener("message", (event) => {
    if (
      event.origin === location.origin &&
      event.source === window.parent &&
      event.data?.type === "inkstone:save-request"
    )
      requested = true;
  });
})();
