<script setup>
import { onMounted, ref } from "vue";
import { api } from "./api";
import { t, errorMessage, language } from "./i18n";
import { formatDate } from "./workspaceFormat";
import { useDialogFocus } from "./dialogFocus";
const props = defineProps({ document: { type: Object, required: true } }),
  emit = defineEmits(["close"]);
const panel = ref(null),
  doc = ref(props.document),
  shares = ref([]),
  events = ref([]),
  email = ref(""),
  permission = ref("read"),
  busy = ref(false),
  error = ref(""),
  notice = ref("");
function close() {
  if (!busy.value) emit("close");
}
useDialogFocus(panel, close);
async function load() {
  doc.value = await api(`/documents/${doc.value.id}/access`);
  [shares.value, events.value] = await Promise.all([
    api(`/documents/${doc.value.id}/shares?version=${doc.value.version}`),
    api(`/documents/${doc.value.id}/audit?version=${doc.value.version}`).then(
      (x) => x.items,
    ),
  ]);
}
async function task(action) {
  if (busy.value) return;
  busy.value = true;
  error.value = "";
  notice.value = "";
  try {
    await action();
    await load();
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
async function save() {
  const existing = shares.value.find(
    (x) => x.email.toLowerCase() === email.value.trim().toLowerCase(),
  );
  await api(`/documents/${doc.value.id}/shares`, {
    method: "PUT",
    body: {
      version: doc.value.version,
      email: email.value,
      permission: permission.value,
      grantVersion: existing?.version ?? 0,
    },
  });
  email.value = "";
}
async function revoke(share) {
  await api(
    `/documents/${doc.value.id}/shares/${encodeURIComponent(share.userId)}?version=${doc.value.version}&grantVersion=${share.version}`,
    { method: "DELETE" },
  );
}
async function review(share) {
  await api(`/documents/${doc.value.id}/reviews`, {
    method: "POST",
    body: { version: doc.value.version, reviewerId: share.userId },
  });
  notice.value = t("ws.reviewSubmitted");
}
onMounted(() => task(async () => {}));
</script>
<template>
  <div class="dialog-backdrop">
    <section
      ref="panel"
      tabindex="-1"
      class="dialog history-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="sharing-title"
    >
      <h2 id="sharing-title">{{ t("ws.sharing") }} — {{ doc.title }}</h2>
      <p>{{ t("ws.sharingHelp") }}</p>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <p v-if="notice" role="status">{{ notice }}</p>
      <form class="workspace-form" @submit.prevent="task(save)">
        <fieldset :disabled="busy" class="workspace-controls">
          <label
            >{{ t("email")
            }}<input v-model="email" type="email" maxlength="254" required
          /></label>
          <label
            >{{ t("ws.permission")
            }}<select v-model="permission">
              <option value="read">{{ t("ws.reader") }}</option>
              <option value="edit">{{ t("ws.editor") }}</option>
            </select></label
          >
          <button :disabled="busy">{{ t("ws.saveAccess") }}</button>
        </fieldset>
      </form>
      <ul class="history-list">
        <li v-for="share in shares" :key="share.userId">
          <span
            >{{ share.email }} ·
            {{
              t(share.permission === "edit" ? "ws.editor" : "ws.reader")
            }}</span
          ><button :disabled="busy" @click="task(() => review(share))">
            {{ t("ws.requestReview") }}</button
          ><button :disabled="busy" @click="task(() => revoke(share))">
            {{ t("ws.revoke") }}
          </button>
        </li>
      </ul>
      <h3>{{ t("ws.recentActivity") }}</h3>
      <ul class="history-list">
        <li v-for="event in events" :key="event.id">
          <span
            >{{ t("ws.audit_" + event.action) }} · {{ t("version") }}
            {{ event.documentVersion }} ·
            {{ formatDate(event.createdAt, language.tag) }}</span
          >
        </li>
      </ul>
      <button :disabled="busy" @click="task(async () => {})">
        {{ t("ws.refresh") }}
      </button>
      <button :disabled="busy" @click="close">{{ t("close") }}</button>
    </section>
  </div>
</template>
