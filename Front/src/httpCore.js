export function createHttpClient({ basePath = "/api" } = {}) {
  let token;

  async function ensureCsrf(force = false) {
    if (!force && token !== undefined) return;
    const response = await fetch(`${basePath}/csrf`, {
      credentials: "same-origin",
    });
    if (!response.ok)
      throw new Error("Cannot connect to your workspace. Please try again.");
    token = (await response.json()).token;
  }

  function resetCsrf() {
    token = undefined;
  }

  function buildError(problem, status) {
    const error = new Error(
      problem.errors
        ? Object.values(problem.errors).flat().join(" ")
        : problem.detail ||
            (status === 401
              ? "Please sign in to continue."
              : "Something went wrong. Please try again."),
    );
    error.status = status;
    error.code = problem.code;
    error.maxBytes = problem.maxBytes;
    error.issues = problem.issues;
    return error;
  }

  async function requestJson(path, options = {}) {
    const method = options.method || "GET";
    const form = options.body instanceof FormData;
    const response = await fetch(basePath + path, {
      ...options,
      credentials: "same-origin",
      headers: {
        ...(!form && options.body !== undefined
          ? { "Content-Type": "application/json" }
          : {}),
        ...(method !== "GET" && method !== "HEAD"
          ? { "X-CSRF-TOKEN": token }
          : {}),
        ...options.headers,
      },
      body:
        form || options.body === undefined
          ? options.body
          : JSON.stringify(options.body),
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if (response.status === 400) resetCsrf();
      throw buildError(problem, response.status);
    }
    return response;
  }

  return { ensureCsrf, resetCsrf, requestJson, buildError };
}
