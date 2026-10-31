import { ref, watch } from "vue";
let saved;
try {
  saved = localStorage.getItem("inkstone.theme");
} catch {
  /* Private browsing. */
}
export const theme = ref(saved === "light" ? "light" : "dark");
watch(
  theme,
  (value) => {
    document.documentElement.dataset.theme = value;
    document.documentElement.style.colorScheme = value;
    document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute("content", value === "dark" ? "#0b1020" : "#f4f7fc");
    try {
      localStorage.setItem("inkstone.theme", value);
    } catch {
      /* Preference is session-only. */
    }
  },
  { immediate: true },
);
