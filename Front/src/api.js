let token;
export async function csrf() {
  const response = await fetch("/api/csrf", { credentials: "same-origin" });
  if (!response.ok)
    throw new Error("Cannot connect to your workspace. Please try again.");
  token = (await response.json()).token;
}
export function resetCsrf() {
  token = undefined;
}
export async function api(path, options = {}) {
  const method = options.method || "GET";
  if (method !== "GET" && !token) await csrf();
  const response = await fetch("/api" + path, {
    ...options,
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      ...(method !== "GET" ? { "X-CSRF-TOKEN": token } : {}),
      ...options.headers,
    },
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const error = new Error(
      problem.errors
        ? Object.values(problem.errors).flat().join(" ")
        : problem.detail ||
            (response.status === 401
              ? "Please sign in to continue."
              : "Something went wrong. Please try again."),
    );
    error.status = response.status;
    throw error;
  }
  return response.status === 204 ? null : response.json();
}
