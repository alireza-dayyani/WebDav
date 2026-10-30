import { computed, ref, watch } from "vue";
import { languages, translate } from "./locales";
import { maxUploadBytes, maxDocumentBytes } from "./fileLimits";
let preferred;
try {
  preferred = localStorage.getItem("inkstone.locale");
} catch {
  /* Storage may be disabled. */
}
const detected = navigator.languages
  ?.map((tag) => tag.split("-")[0])
  .find((code) => languages.some((l) => l.code === code));
export const locale = ref(
  languages.some((l) => l.code === preferred) ? preferred : detected || "en",
);
export const language = computed(() =>
  languages.find((l) => l.code === locale.value),
);
export const t = (key, values) => translate(locale.value, key, values);
watch(
  locale,
  (value) => {
    document.documentElement.lang = value;
    document.documentElement.dir = language.value.dir;
    try {
      localStorage.setItem("inkstone.locale", value);
    } catch {
      /* Storage may be disabled. */
    }
  },
  { immediate: true },
);
export function errorMessage(error, context) {
  if (error.code === "account_changed") return t("accountChanged");
  if (error.code === "image_too_large") return t("studio.imageHelp");
  if (error.status === 400 && context === "register")
    return t("registrationError");
  const limit = new Intl.NumberFormat(language.value.tag).format(
    (error.maxBytes || maxUploadBytes.value) / 1024 / 1024,
  );
  if (error.code === "model_too_large")
    return t("modelTooLarge", {
      limit: new Intl.NumberFormat(language.value.tag).format(
        (error.maxBytes || maxDocumentBytes.value) / 1024 / 1024,
      ),
    });
  if (error.code === "archive_too_large") return t("archiveTooLarge");
  if (error.code === "preview_blocked") return t("previewBlocked");
  if (error.status === 413) return t("tooLarge", { limit });
  if (context === "admin" && error.status === 409) return t("recordChanged");
  const key = {
    400: "invalid",
    401: context === "login" ? "authError" : "session",
    403: "forbidden",
    404: "unavailable",
    409: "conflict",
    413: "tooLarge",
    415: "uploadType",
    422: "conversion",
    429: "rateLimit",
    503: "converterUnavailable",
  }[error.status];
  return key
    ? t(key)
    : error instanceof TypeError
      ? t("network")
      : locale.value === "en"
        ? error.message || t("error")
        : t("error");
}
