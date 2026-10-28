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
  const form = options.body instanceof FormData;
  if (method !== "GET" && !token) await csrf();
  const response = await fetch("/api" + path, {
    ...options,
    credentials: "same-origin",
    headers: {
      ...(!form && options.body !== undefined
        ? { "Content-Type": "application/json" }
        : {}),
      ...(method !== "GET" ? { "X-CSRF-TOKEN": token } : {}),
      ...options.headers,
    },
    body:
      form || options.body === undefined
        ? options.body
        : JSON.stringify(options.body),
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
    error.code = problem.code;
    error.maxBytes = problem.maxBytes;
    error.issues = problem.issues;
    if (response.status === 400) resetCsrf();
    throw error;
  }
  if (options.blob) return response.blob();
  if (options.download) {
    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") || "";
    const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    const plain = disposition.match(/filename="?([^";]+)"?/i);
    let filename = plain?.[1] || "document";
    if (encoded) {
      try {
        filename = decodeURIComponent(encoded[1]);
      } catch {
        /* Use the fallback filename. */
      }
    }
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
    return;
  }
  return response.status === 204 ? null : response.json();
}

export async function previewPdf(path, options = {}) {
  // Reserve the tab during the click, before CSRF or PDF rendering awaits.
  const preview = window.open("about:blank", "_blank");
  if (!preview) {
    const error = new Error("Allow pop-ups for this site to preview PDFs.");
    error.code = "preview_blocked";
    throw error;
  }
  preview.opener = null;
  let url;
  try {
    const blob = await api(path, { ...options, download: false, blob: true });
    if (blob.type !== "application/pdf") {
      const error = new Error("The PDF preview could not be loaded.");
      error.status = 422;
      throw error;
    }
    if (preview.closed) return;
    url = URL.createObjectURL(blob);
    preview.location.replace(url);
    // Keep the PDF available for the viewer's print/save controls until closed.
    const timer = setInterval(() => {
      if (preview.closed) {
        clearInterval(timer);
        URL.revokeObjectURL(url);
      }
    }, 1000);
  } catch (error) {
    if (url) URL.revokeObjectURL(url);
    preview.close();
    throw error;
  }
}
