import { onBeforeUnmount, onMounted } from "vue";
export function useDialogFocus(panel, close) {
  let previous, previousOverflow;
  const siblings = [];
  const keyboard = (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      close();
    }
    if (event.key !== "Tab") return;
    const controls = [
      ...panel.value.querySelectorAll(
        "button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href]",
      ),
    ];
    const first = controls[0],
      last = controls.at(-1);
    if (!first) {
      event.preventDefault();
      return;
    }
    if (
      event.shiftKey &&
      (document.activeElement === first ||
        document.activeElement === panel.value)
    ) {
      event.preventDefault();
      last.focus();
    } else if (
      !event.shiftKey &&
      (document.activeElement === last ||
        document.activeElement === panel.value)
    ) {
      event.preventDefault();
      first.focus();
    }
  };
  onMounted(() => {
    previous = document.activeElement;
    previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    for (const element of document.querySelector("#app")?.children ?? [])
      if (!element.contains(panel.value)) {
        siblings.push([element, element.inert]);
        element.inert = true;
      }
    panel.value.addEventListener("keydown", keyboard);
    panel.value.focus();
  });
  onBeforeUnmount(() => {
    panel.value?.removeEventListener("keydown", keyboard);
    for (const [element, inert] of siblings) element.inert = inert;
    document.body.style.overflow = previousOverflow;
    previous?.focus({ preventScroll: true });
  });
}
