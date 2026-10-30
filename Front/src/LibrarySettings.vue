<script setup>
import { onMounted, ref, watch } from "vue";
import { api } from "./api";
import { t, errorMessage, language } from "./i18n";
import { formatNumber } from "./workspaceFormat";
import { useDialogFocus } from "./dialogFocus";
const emit = defineEmits(["close"]);
const panel = ref(null);
useDialogFocus(panel, () => {
  if (!busy.value) emit("close");
});
const policy = ref(null),
  storage = ref(null),
  preview = ref(null),
  busy = ref(false),
  error = ref("");
watch(
  () => [policy.value?.enabled, policy.value?.days, policy.value?.keepLatest],
  () => {
    preview.value = null;
  },
  { flush: "sync" },
);
async function load() {
  [policy.value, storage.value] = await Promise.all([
    api("/retention/"),
    api("/storage"),
  ]);
}
async function task(action) {
  if (busy.value) return;
  busy.value = true;
  error.value = "";
  try {
    await action();
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
async function save() {
  policy.value = await api("/retention/", {
    method: "PUT",
    body: {
      version: policy.value.version,
      enabled: policy.value.enabled,
      keepLatest: policy.value.keepLatest,
      days: policy.value.days,
    },
  });
  preview.value = await api("/retention/preview", {
    method: "POST",
    body: { version: policy.value.version },
  });
}
async function apply() {
  if (!window.confirm(t("ws.pruneConfirm"))) return;
  await api("/retention/apply", {
    method: "POST",
    body: { version: policy.value.version },
  });
  preview.value = null;
  await load();
}
onMounted(() => task(load));
</script>
<template>
  <div class="dialog-backdrop">
    <section
      ref="panel"
      tabindex="-1"
      class="dialog history-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="storage-title"
    >
      <h2 id="storage-title">{{ t("ws.storage") }}</h2>
      <p v-if="error" role="alert" class="error">{{ error }}</p>
      <p v-if="storage">
        {{
          t("ws.storageBytes", {
            bytes: formatNumber(storage.totalBytes, language.tag),
          })
        }}
        ·
        {{
          t("ws.historyBytes", {
            bytes: formatNumber(storage.historyBytes, language.tag),
          })
        }}
      </p>
      <p>{{ t("ws.retentionHelp") }}</p>
      <form v-if="policy" @submit.prevent="task(save)">
        <fieldset :disabled="busy" class="workspace-controls">
          <label
            ><input v-model="policy.enabled" type="checkbox" />
            {{ t("ws.enableRetention") }}</label
          >
          <label
            >{{ t("ws.keepLatest") }}
            <input
              v-model.number="policy.keepLatest"
              type="number"
              min="5"
              max="1000"
              required
          /></label>
          <label
            >{{ t("ws.keepDays") }}
            <input
              v-model.number="policy.days"
              type="number"
              min="7"
              max="3650"
              required
          /></label>
          <button :disabled="busy">{{ t("ws.savePreview") }}</button>
        </fieldset>
      </form>
      <p v-if="preview">
        {{
          t("ws.prunePreview", { count: preview.count, bytes: preview.bytes })
        }}
      </p>
      <button
        v-if="preview?.enabled && preview.count"
        :disabled="busy"
        @click="task(apply)"
      >
        {{ t("ws.pruneNow") }}
      </button>
      <button :disabled="busy" @click="emit('close')">{{ t("close") }}</button>
    </section>
  </div>
</template>
