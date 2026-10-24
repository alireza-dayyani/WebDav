import { computed, ref, watch } from "vue";
import { languages, translate } from "./locales";
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
  if (error.status === 400 && context === "register")
    return t("registrationError");
  const key = {
    400: "invalid",
    401: context === "login" ? "authError" : "session",
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
