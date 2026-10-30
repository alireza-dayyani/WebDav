<script setup>
import { onBeforeUnmount, onMounted, ref } from "vue";
import { api } from "./api";
import { t, errorMessage, language } from "./i18n";
const props = defineProps({ document: { type: Object, required: true } });
const emit = defineEmits(["close", "restored"]);
const items = ref([]),
  page = ref(0),
  more = ref(true),
  busy = ref(false),
  error = ref("");
const panel = ref(null);
let previousFocus;
function keyboard(event) {
  if (event.key === "Escape" && !busy.value) emit("close");
  if (event.key !== "Tab") return;
  const buttons = [...panel.value.querySelectorAll("button:not(:disabled)")];
  const first = buttons[0],
    last = buttons.at(-1);
  if (!first) {
    event.preventDefault();
    return;
  }
  if (
    event.shiftKey &&
    (document.activeElement === first || document.activeElement === panel.value)
  ) {
    event.preventDefault();
    last.focus();
  } else if (
    !event.shiftKey &&
    (document.activeElement === last || document.activeElement === panel.value)
  ) {
    event.preventDefault();
    first.focus();
  }
}
const date = (value) =>
  new Intl.DateTimeFormat(language.value.tag, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(value);
async function load() {
  busy.value = true;
  error.value = "";
  try {
    const result = await api(
      `/documents/${props.document.id}/versions?version=${props.document.version}&page=${page.value + 1}`,
    );
    items.value.push(...result.items);
    page.value++;
    more.value = result.hasMore;
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
async function act(revision, restore = false) {
  if (restore && !window.confirm(t("restoreConfirm"))) return;
  busy.value = true;
  error.value = "";
  try {
    if (restore) {
      await api(`/documents/${props.document.id}/restore`, {
        method: "POST",
        body: {
          version: props.document.version,
          sourceVersion: revision.version,
          idempotencyKey: crypto.randomUUID(),
        },
      });
      emit("restored");
    } else
      await api(
        `/documents/${props.document.id}/versions/${revision.version}/download?version=${props.document.version}&format=docx`,
        { download: true },
      );
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
onMounted(() => {
  previousFocus = document.activeElement;
  panel.value.focus();
  load();
});
onBeforeUnmount(() => previousFocus?.focus());
</script>
<template>
  <div class="dialog-backdrop" @click.self="!busy && emit('close')">
    <section
      ref="panel"
      tabindex="-1"
      @keydown="keyboard"
      class="dialog history-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="history-title"
    >
      <h2 id="history-title">{{ t("history") }} — {{ document.title }}</h2>
      <p>{{ t("historyHelp") }}</p>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <ul class="history-list">
        <li v-for="revision in items" :key="revision.version">
          <span dir="auto"
            >{{ revision.title }} · {{ t("version") }} {{ revision.version }} ·
            {{ date(revision.createdAt) }}</span
          >
          <button :disabled="busy" @click="act(revision)">
            {{ t("download") }}
          </button>
          <button
            :disabled="busy || revision.version === document.version"
            @click="act(revision, true)"
          >
            {{ t("restore") }}
          </button>
        </li>
      </ul>
      <button v-if="more" :disabled="busy" @click="load">
        {{ t("loadMore") }}
      </button>
      <button :disabled="busy" @click="emit('close')">{{ t("close") }}</button>
    </section>
  </div>
</template>
