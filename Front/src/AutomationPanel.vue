<script setup>
import { onBeforeUnmount, onMounted, ref } from "vue";
import { api, allPages } from "./api";
import { t, errorMessage, language } from "./i18n";
import { formatDate } from "./workspaceFormat";
import { useDialogFocus } from "./dialogFocus";
import { createJobSubmission, mergeJob, updateJob } from "./automationState";
const emit = defineEmits(["close"]),
  panel = ref(null),
  tab = ref("jobs"),
  busy = ref(false),
  error = ref(""),
  jobs = ref([]),
  page = ref(0),
  more = ref(true),
  detail = ref(null),
  tokens = ref([]),
  secret = ref("");
const documents = ref([]),
  templates = ref([]),
  sourceId = ref(""),
  templateId = ref(""),
  fields = ref([]),
  records = ref([]),
  format = ref("pdf");
const tokenName = ref(""),
  scopes = ref(["documents:read"]),
  expires = ref(30),
  allowedScopes = [
    "documents:read",
    "documents:write",
    "templates:read",
    "templates:write",
    "jobs:read",
    "jobs:write",
  ];
let polling = false;
let jobsGeneration = 0;
let timer,
  disposed = false;
useDialogFocus(panel, () => {
  if (!busy.value) emit("close");
});
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
async function loadJobs(reset = false) {
  const generation = reset ? ++jobsGeneration : jobsGeneration;
  const nextPage = reset ? 1 : page.value + 1;
  const response = await api(`/document-jobs/?page=${nextPage}`);
  if (disposed || generation !== jobsGeneration) return;
  if (reset) {
    // Preserve newer polling responses and already-loaded reports.
    jobs.value = response.items.map((item) =>
      mergeJob(jobs.value.find((existing) => existing.id === item.id), item),
    );
  } else {
    for (const item of response.items) {
      if (jobs.value.some((existing) => existing.id === item.id))
        jobs.value = updateJob(jobs.value, item);
      else jobs.value.push(item);
    }
  }
  page.value = nextPage;
  more.value = response.hasMore;
  if (detail.value) {
    const current = jobs.value.find((job) => job.id === detail.value.id);
    detail.value = current ? mergeJob(detail.value, current) : null;
  }
}
function applyJobUpdate(update) {
  jobs.value = updateJob(jobs.value, update);
  if (detail.value?.id === update.id)
    detail.value = mergeJob(detail.value, update);
}
async function poll() {
  if (disposed || busy.value || polling) return;
  polling = true;
  const generation = jobsGeneration;
  try {
    for (const job of jobs.value.filter((x) =>
      ["queued", "running"].includes(x.status),
    )) {
      const update = await api(
        `/document-jobs/${job.id}?version=${job.version}`,
      );
      if (disposed || generation !== jobsGeneration) return;
      applyJobUpdate(update);
    }
  } catch (e) {
    if (!disposed && generation === jobsGeneration) error.value = errorMessage(e);
  } finally {
    polling = false;
  }
}
async function loadResources() {
  [documents.value, templates.value] = await Promise.all([
    api("/documents/"),
    allPages("/templates/"),
  ]);
  templates.value = templates.value.filter((x) => x.data.kind === "document");
}
async function loadTokens() {
  tokens.value = await api("/automation-tokens/");
}
const submit = createJobSubmission((payload) =>
  api("/document-jobs/", { method: "POST", body: payload }),
);
async function exportDocument() {
  const source = documents.value.find((x) => x.id === sourceId.value);
  await submit({
    kind: "export",
    source: { id: source.id, version: source.version },
    format: format.value,
  });
  tab.value = "jobs";
  await loadJobs(true);
}
async function chooseTemplate() {
  const template = templates.value.find(
    (x) => x.metadata.id === templateId.value,
  );
  fields.value = [];
  records.value = [];
  if (!template) return;
  const result = await api(
    `/templates/${template.metadata.id}/bookmarks?version=${template.metadata.version}`,
  );
  fields.value = result.bookmarks
    .filter((x) => x.replaceable)
    .map((x) => x.name);
  addRecord();
}
function addRecord() {
  records.value.push({
    key: crypto.randomUUID(),
    title: `${t("document")} ${records.value.length + 1}`,
    values: Object.fromEntries(fields.value.map((name) => [name, ""])),
  });
}
async function bulk() {
  const template = templates.value.find(
    (x) => x.metadata.id === templateId.value,
  );
  await submit({
    kind: "renderBatch",
    template: { id: template.metadata.id, version: template.metadata.version },
    records: records.value,
    format: format.value,
  });
  tab.value = "jobs";
  await loadJobs(true);
}
async function cancel(job) {
  const result = await api(`/document-jobs/${job.id}/cancel`, {
    method: "POST",
    body: { version: job.version },
  });
  applyJobUpdate(result);
}
async function inspect(job) {
  const update = await api(`/document-jobs/${job.id}?version=${job.version}`);
  applyJobUpdate(update);
  detail.value = mergeJob(jobs.value.find((item) => item.id === job.id), update);
}
async function createToken() {
  const result = await api("/automation-tokens/", {
    method: "POST",
    body: {
      name: tokenName.value,
      scopes: scopes.value,
      expiresInDays: expires.value,
    },
  });
  secret.value = result.token;
  tokenName.value = "";
  await loadTokens();
}
async function revoke(token) {
  await api(`/automation-tokens/${token.id}?version=${token.version}`, {
    method: "DELETE",
  });
  secret.value = "";
  await loadTokens();
}
onMounted(() => {
  task(async () => {
    await Promise.all([loadJobs(true), loadResources(), loadTokens()]);
  });
  timer = setInterval(poll, 3000);
});
onBeforeUnmount(() => {
  disposed = true;
  clearInterval(timer);
  secret.value = "";
});
</script>
<template>
  <div class="dialog-backdrop">
    <section
      ref="panel"
      tabindex="-1"
      class="dialog history-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="automation-title"
    >
      <h2 id="automation-title">{{ t("ws.automation") }}</h2>
      <div class="workspace-tabs">
        <button
          v-for="name in ['jobs', 'bulk', 'tokens']"
          :key="name"
          :aria-pressed="tab === name"
          @click="tab = name"
        >
          {{ t("ws." + name) }}
        </button>
      </div>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <template v-if="tab === 'jobs'">
        <p>{{ t("ws.jobsHelp") }}</p>
        <form class="workspace-form" @submit.prevent="task(exportDocument)">
          <fieldset :disabled="busy" class="workspace-controls">
            <label
              >{{ t("ws.sourceDocument")
              }}<select v-model="sourceId" required>
                <option value="" disabled>{{ t("ws.choose") }}</option>
                <option v-for="doc in documents" :key="doc.id" :value="doc.id">
                  {{ doc.title }} · v{{ doc.version }}
                </option>
              </select></label
            >
            <label
              >{{ t("ws.outputFormat")
              }}<select v-model="format">
                <option
                  v-for="value in ['pdf', 'docx', 'txt', 'md', 'json']"
                  :key="value"
                >
                  {{ value }}
                </option>
              </select></label
            ><button :disabled="busy">{{ t("ws.queueExport") }}</button>
          </fieldset>
        </form>
        <article v-for="job in jobs" :key="job.id" class="workspace-record">
          <p>
            {{ t("ws.kind_" + job.kind) }} ·
            {{ t("ws.status_" + job.status) }} ·
            {{ formatDate(job.createdAt, language.tag) }}
          </p>
          <p v-if="job.error" class="error">{{ job.error }}</p>
          <button :disabled="busy" @click="task(() => inspect(job))">
            {{ t("ws.details") }}</button
          ><button
            v-if="job.status === 'succeeded'"
            :disabled="busy"
            @click="
              task(() =>
                api(
                  `/document-jobs/${job.id}/download?version=${job.version}`,
                  { download: true },
                ),
              )
            "
          >
            {{ t("download") }}</button
          ><button
            v-if="['queued', 'running'].includes(job.status)"
            :disabled="busy"
            @click="task(() => cancel(job))"
          >
            {{ t("ws.cancelJob") }}
          </button>
        </article>
        <details v-if="detail" open>
          <summary>{{ t("ws.jobReport") }}</summary>
          <pre class="automation-report">{{
            JSON.stringify(detail.report, null, 2)
          }}</pre>
        </details>
        <button v-if="more" :disabled="busy" @click="task(() => loadJobs())">
          {{ t("loadMore") }}</button
        ><button
          :disabled="busy"
          @click="
            task(async () => {
              await loadJobs(true);
              await loadResources();
            })
          "
        >
          {{ t("ws.refresh") }}
        </button>
      </template>
      <form
        v-else-if="tab === 'bulk'"
        class="workspace-form"
        @submit.prevent="task(bulk)"
      >
        <fieldset :disabled="busy" class="workspace-controls">
          <p>{{ t("ws.bulkHelp") }}</p>
          <label
            >{{ t("ws.template")
            }}<select
              v-model="templateId"
              required
              @change="task(chooseTemplate)"
            >
              <option value="" disabled>{{ t("ws.choose") }}</option>
              <option
                v-for="template in templates"
                :key="template.metadata.id"
                :value="template.metadata.id"
              >
                {{ template.metadata.title }}
              </option>
            </select></label
          >
          <label
            >{{ t("ws.outputFormat")
            }}<select v-model="format">
              <option
                v-for="value in ['pdf', 'docx', 'txt', 'md', 'json']"
                :key="value"
              >
                {{ value }}
              </option>
            </select></label
          >
          <fieldset v-for="(record, index) in records" :key="record.key">
            <legend>{{ t("document") }} {{ index + 1 }}</legend>
            <label
              >{{ t("ws.resultTitle")
              }}<input v-model="record.title" maxlength="160" required /></label
            ><label v-for="field in fields" :key="field"
              >{{ field
              }}<input
                v-model="record.values[field]"
                maxlength="10000" /></label
            ><button
              type="button"
              :disabled="busy || records.length === 1"
              @click="records.splice(index, 1)"
            >
              {{ t("ws.removeRecord") }}
            </button>
          </fieldset>
          <button
            type="button"
            :disabled="busy || !templateId || records.length >= 100"
            @click="addRecord"
          >
            {{ t("ws.addRecord") }}</button
          ><button :disabled="busy || !records.length">
            {{ t("ws.generateBatch") }}
          </button>
        </fieldset>
      </form>
      <template v-else>
        <p>{{ t("ws.tokensHelp") }}</p>
        <form class="workspace-form" @submit.prevent="task(createToken)">
          <fieldset :disabled="busy" class="workspace-controls">
            <label
              >{{ t("ws.tokenName")
              }}<input v-model="tokenName" maxlength="80" required /></label
            ><label
              >{{ t("ws.expiryDays")
              }}<input
                v-model.number="expires"
                type="number"
                min="1"
                max="365"
                required
            /></label>
            <fieldset>
              <legend>{{ t("ws.tokenScopes") }}</legend>
              <label v-for="scope in allowedScopes" :key="scope"
                ><input v-model="scopes" type="checkbox" :value="scope" />{{
                  t("ws.scope_" + scope)
                }}</label
              >
            </fieldset>
            <button :disabled="busy || !scopes.length">
              {{ t("ws.createToken") }}
            </button>
          </fieldset>
        </form>
        <label v-if="secret"
          >{{ t("ws.secretOnce")
          }}<textarea
            :value="secret"
            readonly
            spellcheck="false"
            autocomplete="off"
            @focus="$event.target.select()"
          ></textarea
          ><button @click="secret = ''">
            {{ t("ws.dismissSecret") }}
          </button></label
        >
        <ul class="history-list">
          <li v-for="token in tokens" :key="token.id">
            <span
              >{{ token.name }} ·
              {{ formatDate(token.expiresAt, language.tag, true) }}<br />{{
                token.scopes
                  .split(" ")
                  .map((scope) => t("ws.scope_" + scope))
                  .join(", ")
              }}</span
            ><button :disabled="busy" @click="task(() => revoke(token))">
              {{ t("ws.revoke") }}
            </button>
          </li>
        </ul>
      </template>
      <button :disabled="busy" @click="emit('close')">{{ t("close") }}</button>
    </section>
  </div>
</template>
