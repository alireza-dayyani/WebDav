export function createHttpClient({
  basePath = "/api",
  accountHeader,
  csrfUnavailableMessage = "Cannot connect to your workspace. Please try again.",
  unauthorizedMessage = "Please sign in to continue.",
  genericMessage = "Something went wrong. Please try again.",
} = {}) {
  let token;

  async function ensureCsrf(force = false) {
    if (!force && token !== undefined) return;
    const response = await fetch(`${basePath}/csrf`, {
      credentials: "same-origin",
    });
    if (!response.ok) throw new Error(csrfUnavailableMessage);
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
            (status === 401 ? unauthorizedMessage : genericMessage),
    );
    error.status = status;
    error.code = problem.code;
    error.maxBytes = problem.maxBytes;
    error.issues = problem.issues;
    return error;
  }

  async function requestJson(path, options = {}) {
    const method = options.method || "GET";
    const form =
      typeof FormData !== "undefined" && options.body instanceof FormData;
    const scope = accountHeader?.();
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
        ...(scope ? { "X-Inkstone-Account": scope } : {}),
        ...options.headers,
      },
      body:
        form || options.body === undefined
          ? options.body
          : typeof options.body === "string"
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

  async function requestData(path, options = {}) {
    const method = options.method || "GET";
    if (method !== "GET" && method !== "HEAD") await ensureCsrf(false);
    const response = await requestJson(path, options);
    return response.status === 204 ? null : response.json();
  }

  return { ensureCsrf, resetCsrf, requestJson, requestData, buildError };
}
