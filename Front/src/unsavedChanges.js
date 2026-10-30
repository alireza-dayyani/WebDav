import { onBeforeUnmount, onMounted } from "vue";

export function beforeUnloadGuard(isDirty) {
  return function guard(event) {
    if (!isDirty()) return;
    event.preventDefault();
    event.returnValue = "";
  };
}
export function useUnsavedChanges(isDirty) {
  const guard = beforeUnloadGuard(isDirty);
  onMounted(() => window.addEventListener("beforeunload", guard));
  onBeforeUnmount(() => window.removeEventListener("beforeunload", guard));
}
