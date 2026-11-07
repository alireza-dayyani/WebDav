import { createHttpClient } from "./httpCore.js";

const { requestJson, resetCsrf, ensureCsrf } = createHttpClient();

export async function csrf() {
  await ensureCsrf(true);
}
export { resetCsrf };
export async function allPages(path) {
  const items = [];
  for (let page = 1; ; page++) {
    const response = await api(
      `${path}${path.includes("?") ? "&" : "?"}page=${page}`,
    );
    items.push(...response.items);
    if (!response.hasMore) return items;
  }
}
export async function api(path, options = {}) {
  const method = options.method || "GET";
  const form = options.body instanceof FormData;
  if (method !== "GET" && method !== "HEAD") await ensureCsrf(false);
  const response = await requestJson(path, options);
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
