<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { api } from "./api";
import { locale, t, errorMessage } from "./i18n";
import LanguagePicker from "./LanguagePicker.vue";
const props = defineProps({ letter: { type: Object, required: true } });
const emit = defineEmits(["close"]);
const item = ref({ ...props.letter }),
  frame = ref(null),
  dirty = ref(false),
  pending = ref(false),
  failed = ref(false),
  busy = ref(false),
  error = ref("");
const status = ref(t("openingDocument")),
  format = ref("docx");
const sourceVersion = ref(item.value.version);
// Changing the shell language must never reload a frame containing unsaved edits.
const sourceLocale = locale.value;
const source = computed(
  () =>
    `/editor/index.html?letter=${item.value.id}&version=${sourceVersion.value}&lang=${sourceLocale}`,
);
const st = (key) => t("studio." + key);
watch(locale, () => {
  if (!failed.value)
    status.value = t(
      pending.value ? "saving" : dirty.value ? "unsaved" : "saved",
    );
});
function save() {
  frame.value?.contentWindow?.postMessage(
    { type: "inkstone:save-request" },
    location.origin,
  );
}
function close() {
  if ((!dirty.value && !pending.value) || window.confirm(t("leave")))
    emit("close", item.value);
}
function record(metadata) {
  item.value = { ...item.value, ...metadata };
  history.replaceState(
    null,
    "",
    `/#/letters/${item.value.id}/edit?version=${item.value.version}`,
  );
}
function message(event) {
  if (
    event.origin !== location.origin ||
    event.source !== frame.value?.contentWindow
  )
    return;
  const data = event.data;
  if (data?.type === "inkstone:state") {
    dirty.value = data.dirty;
    pending.value = data.pending;
    if (!failed.value)
      status.value = t(
        pending.value ? "saving" : dirty.value ? "unsaved" : "saved",
      );
  } else if (
    data?.type === "inkstone:ready" ||
    data?.type === "inkstone:saved"
  ) {
    record(data.document);
    failed.value = false;
    status.value = t("saved");
  } else if (data?.type === "inkstone:error") {
    failed.value = true;
    status.value = errorMessage(data);
  }
}
async function fill() {
  busy.value = true;
  error.value = "";
  try {
    const result = await api(`/letters/${item.value.id}/editor/fields`, {
      method: "POST",
      body: { version: item.value.version },
    });
    record(result.metadata);
    sourceVersion.value = result.metadata.version;
    status.value = t("openingDocument");
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
async function download() {
  busy.value = true;
  error.value = "";
  try {
    await api(
      `/letters/${item.value.id}/editor/download?version=${item.value.version}&format=${format.value}`,
      { download: true },
    );
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
function unload(event) {
  if (dirty.value || pending.value) {
    event.preventDefault();
    event.returnValue = "";
  }
}
onMounted(() => {
  window.addEventListener("message", message);
  window.addEventListener("beforeunload", unload);
});
onBeforeUnmount(() => {
  window.removeEventListener("message", message);
  window.removeEventListener("beforeunload", unload);
});
</script>
<template>
  <div class="editor-layout">
    <header class="editor-header">
      <button class="back" :disabled="busy" @click="close">
        ← {{ st("backToLetter") }}
      </button>
      <div class="editor-title">
        <strong>{{ item.title }}</strong
        ><small :class="{ error: failed }" role="status">{{ status }}</small>
      </div>
      <div class="header-tools">
        <LanguagePicker />
        <button
          class="secondary"
          :disabled="busy || dirty || pending || failed"
          @click="fill"
        >
          {{ st("fillFields") }}
        </button>
        <select v-model="format" :aria-label="t('format')">
          <option
            v-for="value in ['docx', 'pdf', 'json']"
            :key="value"
            :value="value"
          >
            {{ value.toUpperCase() }}
          </option>
        </select>
        <button
          class="secondary"
          :disabled="busy || dirty || pending || failed"
          @click="download"
        >
          {{ t("download") }}
        </button>
        <button class="primary" :disabled="busy || pending" @click="save">
          {{ t("save") }}
        </button>
      </div>
    </header>
    <p class="studio-editor-note">{{ st("editorHelp") }}</p>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <iframe
      ref="frame"
      :src="source"
      :inert="busy"
      :title="st('writeLetter')"
      allow="clipboard-read; clipboard-write; fullscreen"
      class="editor-frame"
    ></iframe>
    <div v-if="failed" class="editor-warning" role="alert">
      {{ status }} {{ t("recovery") }}
    </div>
  </div>
</template>
