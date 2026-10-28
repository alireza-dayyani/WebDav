<script setup>
import { computed, onMounted, reactive, ref, watch } from "vue";
import { api, previewPdf } from "./api";
import { language, locale, t, errorMessage } from "./i18n";
import LanguagePicker from "./LanguagePicker.vue";
const props = defineProps({
  initialTab: { type: String, default: "letters" },
  initialLetterId: { type: String, default: "" },
});
const emit = defineEmits(["close", "open-document", "open-letter-editor"]);
const st = (key) => t("studio." + key);
const tab = ref(props.initialTab),
  busy = ref(false),
  error = ref(""),
  notice = ref("");
const templates = ref([]),
  senders = ref([]),
  images = ref([]),
  letters = ref([]),
  documents = ref([]);
const fields = ref([]),
  required = ref([]),
  bookmarkNames = ref([]),
  mappings = reactive({});
const current = ref(null),
  artifacts = ref([]),
  issues = ref([]),
  validated = ref(false),
  dirty = ref(false);
const directory = ref([]),
  directoryPage = ref(1),
  directoryMore = ref(false),
  directorySearch = ref("");
const generated = computed(() => current.value?.metadata.state === "generated");
const form = reactive({
  title: "",
  subject: "",
  language: language.value.tag,
  date: "",
  templateId: "",
  senderId: "",
  signatureId: "",
  logoId: "",
  bodyId: "",
  bodyText: "",
  number: "",
  priority: "normal",
  classification: "normal",
  includeQrCode: true,
  copyVariants: false,
  recipients: [],
  attachmentIds: [],
});
const senderForm = reactive({
  id: "",
  version: 0,
  fullName: "",
  title: "",
  organization: "",
  address: "",
});
const templateForm = reactive({
  id: "",
  version: 0,
  title: "",
  kind: "document",
  language: language.value.tag,
  sourceId: "",
  paper: "",
});
const imageTitle = ref(""),
  imageFile = ref(null),
  imageInput = ref(null);
const letterTemplates = computed(() =>
  templates.value.filter((x) => x.data.kind === "letter"),
);
watch(locale, () => {
  notice.value = "";
  error.value = "";
});
watch(
  () => props.initialTab,
  (value) => {
    tab.value = value;
  },
);
watch(
  form,
  () => {
    dirty.value = true;
    validated.value = false;
  },
  { deep: true, flush: "sync" },
);
function navigate(value) {
  tab.value = value;
  history.replaceState(
    null,
    "",
    value === "templates" ? "/#/templates" : "/#/letters",
  );
}
async function task(action) {
  busy.value = true;
  error.value = "";
  notice.value = "";
  try {
    await action();
  } catch (e) {
    error.value = errorMessage(e);
    if (e.issues) issues.value = e.issues;
  } finally {
    busy.value = false;
  }
}
async function all(path) {
  const result = [];
  let page = 1,
    response;
  do {
    response = await api(`${path}?page=${page++}`);
    result.push(...response.items);
  } while (response.hasMore && page <= 100);
  return result;
}
async function refresh() {
  [
    templates.value,
    senders.value,
    images.value,
    letters.value,
    documents.value,
  ] = await Promise.all([
    all("/templates/"),
    all("/letters/senders/"),
    all("/letters/images/"),
    all("/letters/"),
    api("/documents/"),
  ]);
}
async function findUsers(more = false) {
  const page = more ? directoryPage.value + 1 : 1;
  const result = await api(
    `/letters/directory?search=${encodeURIComponent(directorySearch.value)}&page=${page}`,
  );
  directory.value = more ? [...directory.value, ...result.items] : result.items;
  directoryPage.value = page;
  directoryMore.value = result.hasMore;
}
function newLetter(templateId = "") {
  if (dirty.value && !window.confirm(t("leave"))) return;
  current.value = null;
  artifacts.value = [];
  issues.value = [];
  validated.value = false;
  const now = new Date();
  const chosen =
    letterTemplates.value.find((x) => x.metadata.id === templateId) ||
    letterTemplates.value[0];
  now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
  Object.assign(form, {
    title: "",
    subject: "",
    language: chosen?.data.language || language.value.tag,
    date: now.toISOString().slice(0, 10),
    templateId: templateId || letterTemplates.value[0]?.metadata.id || "",
    senderId: senders.value[0]?.metadata.id || "",
    signatureId: "",
    logoId: "",
    bodyId: "",
    bodyText: "",
    number: "",
    priority: "normal",
    classification: "normal",
    includeQrCode: true,
    copyVariants: false,
    recipients: [],
    attachmentIds: [],
  });
  dirty.value = false;
  navigate("letters");
}
const reference = (items, id) => {
  const item = items.find((x) => (x.metadata?.id || x.id) === id);
  return item ? { id, version: item.metadata?.version || item.version } : null;
};
function draft() {
  return {
    title: form.title,
    subject: form.subject,
    language: form.language,
    date: form.date,
    template: reference(templates.value, form.templateId),
    sender: reference(senders.value, form.senderId),
    signature: reference(images.value, form.signatureId),
    logo: reference(images.value, form.logoId),
    bodyDocument: current.value?.metadata.hasEditor
      ? current.value.data.bodyDocument
      : reference(documents.value, form.bodyId),
    bodyText: current.value?.metadata.hasEditor
      ? current.value.data.bodyText
      : form.bodyId
        ? ""
        : form.bodyText,
    recipients: form.recipients.map(({ userId, kind, printOnLetter }) => ({
      userId,
      kind,
      printOnLetter,
    })),
    attachments: form.attachmentIds
      .map((id) => reference(documents.value, id))
      .filter(Boolean),
    number: form.number,
    priority: form.priority,
    classification: form.classification,
    includeQrCode: form.includeQrCode,
    copyVariants: form.copyVariants,
  };
}
async function saveDraft() {
  const value = draft();
  current.value = current.value
    ? await api(`/letters/${current.value.metadata.id}`, {
        method: "PUT",
        body: { version: current.value.metadata.version, draft: value },
      })
    : await api("/letters/", { method: "POST", body: value });
  dirty.value = false;
  validated.value = false;
  notice.value = st("saved");
  await refresh();
}
async function openLetter(item) {
  if (dirty.value && !window.confirm(t("leave"))) return;
  const full = await api(
    `/letters/${item.metadata.id}?version=${item.metadata.version}`,
  );
  current.value = full;
  const d = full.data;
  Object.assign(form, {
    title: d.title,
    subject: d.subject,
    language: d.language,
    date: d.date,
    templateId: d.template?.id || "",
    senderId: d.sender?.id || "",
    signatureId: d.signature?.id || "",
    logoId: d.logo?.id || "",
    bodyId: d.bodyDocument?.id || "",
    bodyText: d.bodyText,
    number: d.number,
    priority: d.priority,
    classification: d.classification,
    includeQrCode: d.includeQrCode,
    copyVariants: d.copyVariants,
    recipients: d.recipients.map((x) => ({
      ...x,
      label:
        full.snapshot?.recipients?.find((r) => r.userId === x.userId)?.email ||
        directory.value.find((r) => r.id === x.userId)?.email ||
        x.userId,
    })),
    attachmentIds: d.attachments.map((x) => x.id),
  });
  dirty.value = false;
  issues.value = [];
  validated.value = false;
  artifacts.value = await api(
    `/letters/${item.metadata.id}/artifacts?version=${item.metadata.version}`,
  );
}
async function writeLetter() {
  if (!form.title.trim())
    form.title =
      letterTemplates.value.find((x) => x.metadata.id === form.templateId)
        ?.metadata.title || st("newLetter");
  if (!current.value || dirty.value) await saveDraft();
  current.value = await api(`/letters/${current.value.metadata.id}/editor`, {
    method: "POST",
    body: { version: current.value.metadata.version },
  });
  dirty.value = false;
  emit("open-letter-editor", current.value.metadata);
}
function addRecipient(user) {
  if (!form.recipients.some((x) => x.userId === user.id))
    form.recipients.push({
      userId: user.id,
      kind: "to",
      printOnLetter: true,
      label: user.email,
    });
}
async function operation(action) {
  if (!current.value || dirty.value) {
    error.value = st("needsSave");
    return;
  }
  const base = `/letters/${current.value.metadata.id}`;
  const body = { version: current.value.metadata.version };
  if (action === "validate") {
    const report = await api(base + "/validate", { method: "POST", body });
    issues.value = report.issues;
    validated.value = report.valid;
  } else if (action === "preview")
    await previewPdf(base + "/preview", { method: "POST", body });
  else {
    // Retain the key across network retries. Never finalize a second copy after a lost response.
    const key = `inkstone.generation.${current.value.metadata.id}.${body.version}`;
    let nonce = sessionStorage.getItem(key);
    if (!nonce) {
      nonce = crypto.randomUUID();
      sessionStorage.setItem(key, nonce);
    }
    const result = await api(base + "/generate", {
      method: "POST",
      body: { ...body, idempotencyKey: nonce },
    });
    current.value = await api(
      `/letters/${result.metadata.id}?version=${result.metadata.version}`,
    );
    artifacts.value = result.artifacts;
    dirty.value = false;
    await refresh();
  }
}
async function download(artifact, format) {
  await api(
    `/letters/${current.value.metadata.id}/artifacts/${artifact.id}/download?version=${current.value.metadata.version}&format=${format}`,
    { download: true },
  );
}
async function duplicate(item) {
  const name = window.prompt(st("documentName"), item.metadata.title);
  if (!name) return;
  const copy = await api(`/letters/${item.metadata.id}/duplicate`, {
    method: "POST",
    body: { version: item.metadata.version, title: name },
  });
  dirty.value = false;
  await refresh();
  await openLetter(copy);
}
async function remove(item, path) {
  if (!window.confirm(st("deleteConfirm"))) return;
  await api(`${path}/${item.metadata.id}?version=${item.metadata.version}`, {
    method: "DELETE",
  });
  if (current.value?.metadata.id === item.metadata.id) {
    dirty.value = false;
    newLetter();
  }
  await refresh();
}
async function saveSender() {
  const { id, ...body } = senderForm;
  await api(id ? `/letters/senders/${id}` : "/letters/senders/", {
    method: id ? "PUT" : "POST",
    body,
  });
  Object.assign(senderForm, {
    id: "",
    version: 0,
    fullName: "",
    title: "",
    organization: "",
    address: "",
  });
  await refresh();
  notice.value = st("saved");
}
function editSender(item) {
  Object.assign(senderForm, item.data, {
    id: item.metadata.id,
    version: item.metadata.version,
  });
}
async function uploadImage() {
  const body = new FormData();
  body.append("title", imageTitle.value);
  body.append("file", imageFile.value);
  await api("/letters/images/", { method: "POST", body });
  imageTitle.value = "";
  imageFile.value = null;
  if (imageInput.value) imageInput.value.value = "";
  await refresh();
  notice.value = st("saved");
}
const aliases = {
  Date: "تاریخ",
  Number: "شماره_ثبت",
  Classification: "طبقه_بندی",
  Priority: "فوریت_و_اهمیت",
  Body: "متن_نامه",
  Subject: "موضوع",
  Recipients: "گیرندگان_اصلی",
  SenderName: "نام_و_نام_خانوادگی_فرستنده",
  SenderTitle: "نوع_جایگاه_امضاکننده_اصلی",
  Signature: "امضای_اصلی",
  Logo: "تصویر_واحد_سازمانی",
  Copies: "رونوشت",
  Attachments: "پیوست",
  Address: "آدرس",
  Tracking: "رهگیری",
  QRCode: "بارکد",
};
async function sourceChanged() {
  const source = documents.value.find((x) => x.id === templateForm.sourceId);
  bookmarkNames.value = [];
  for (const key of fields.value) delete mappings[key];
  if (!source) return;
  bookmarkNames.value = (
    await api(`/documents/${source.id}/bookmarks?version=${source.version}`)
  ).bookmarks
    .filter((x) => x.replaceable)
    .map((x) => x.name);
  for (const key of fields.value)
    mappings[key] = bookmarkNames.value.includes(key)
      ? key
      : bookmarkNames.value.includes(aliases[key])
        ? aliases[key]
        : "";
}
async function editTemplate(item) {
  Object.assign(templateForm, {
    id: item.metadata.id,
    version: item.metadata.version,
    title: item.metadata.title,
    kind: item.data.kind,
    language: item.data.language,
    sourceId: "",
  });
  bookmarkNames.value = (
    await api(
      `/templates/${item.metadata.id}/bookmarks?version=${item.metadata.version}`,
    )
  ).bookmarks
    .filter((x) => x.replaceable)
    .map((x) => x.name);
  for (const key of fields.value) mappings[key] = item.data.fields[key] || "";
}
function clearTemplate() {
  Object.assign(templateForm, { id: "", version: 0, title: "", sourceId: "" });
  bookmarkNames.value = [];
}
async function saveTemplate(starter = false) {
  const config = {
    ...templateForm,
    fields: Object.fromEntries(
      Object.entries(mappings).filter(([, value]) => value),
    ),
    requiredFields: templateForm.kind === "letter" ? required.value : [],
  };
  if (starter)
    await api("/templates/starter", {
      method: "POST",
      body: {
        title: config.title,
        kind: config.kind,
        language: config.language,
        paper: config.kind === "letter" ? config.paper || undefined : undefined,
      },
    });
  else if (config.id)
    await api(`/templates/${config.id}`, { method: "PUT", body: config });
  else
    await api("/templates/", {
      method: "POST",
      body: { ...config, source: reference(documents.value, config.sourceId) },
    });
  clearTemplate();
  await refresh();
  notice.value = st("saved");
}
async function createDocument(item) {
  const title = window.prompt(st("documentName"), item.metadata.title);
  if (!title) return;
  const document = await api(`/templates/${item.metadata.id}/render`, {
    method: "POST",
    body: { title, version: item.metadata.version, values: {} },
  });
  emit("open-document", document);
}
async function uploadDocument(event, target) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  const body = new FormData();
  body.append("file", file);
  const document = await api("/documents/upload", { method: "POST", body });
  await refresh();
  if (target === "template") {
    templateForm.sourceId = document.id;
    await sourceChanged();
  } else {
    form.bodyId = document.id;
    form.bodyText = "";
  }
}
function close() {
  if (!dirty.value || window.confirm(t("leave"))) emit("close");
}
onMounted(() =>
  task(async () => {
    await refresh();
    const info = await api("/templates/fields");
    fields.value = info.fields;
    required.value = info.required;
    await findUsers();
    const chosen = letters.value.find(
      (x) => x.metadata.id === props.initialLetterId,
    );
    if (chosen) await openLetter(chosen);
    else newLetter();
    navigate(props.initialTab);
  }),
);
</script>

<template>
  <main class="studio-page">
    <header class="studio-header">
      <div>
        <button class="text-button" @click="close">← {{ st("back") }}</button>
        <h1>{{ st(tab) }}</h1>
      </div>
      <div class="header-tools">
        <button class="secondary" :disabled="busy" @click="task(refresh)">
          {{ st("refresh") }}</button
        ><LanguagePicker />
      </div>
    </header>
    <nav class="studio-tabs" :aria-label="st('letters')">
      <button
        v-for="name in ['letters', 'templates', 'senders', 'images']"
        :key="name"
        :class="{ selected: tab === name }"
        @click="navigate(name)"
      >
        {{ st(name) }}
      </button>
    </nav>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <p v-if="notice" class="success" role="status">{{ notice }}</p>
    <p v-if="busy" role="status">{{ st("busy") }}</p>

    <div v-if="tab === 'letters'" class="studio-layout">
      <aside class="studio-card studio-library">
        <div class="section-heading">
          <h2>{{ st("savedLetters") }}</h2>
          <button class="secondary" :disabled="busy" @click="newLetter()">
            ＋ {{ st("newLetter") }}
          </button>
        </div>
        <p v-if="!letters.length" class="muted">{{ st("empty") }}</p>
        <article
          v-for="item in letters"
          :key="item.metadata.id"
          class="studio-list-item"
        >
          <button class="text-button" @click="task(() => openLetter(item))">
            {{ item.metadata.title }}</button
          ><small
            >{{ st(item.metadata.state) }} · v{{ item.metadata.version }}</small
          >
          <div class="studio-actions">
            <button @click="task(() => duplicate(item))">
              {{ st("duplicate") }}</button
            ><button @click="task(() => remove(item, '/letters'))">
              {{ st("delete") }}
            </button>
          </div>
        </article>
      </aside>
      <section class="studio-card">
        <h2>{{ current ? current.metadata.title : st("newLetter") }}</h2>
        <p class="muted">{{ st("intro") }}</p>
        <p class="studio-note">{{ st("privateNote") }}</p>
        <p v-if="generated" class="success">{{ st("generatedNote") }}</p>
        <section v-if="generated" class="studio-frozen">
          <h3>{{ current.snapshot?.draft.subject }}</h3>
          <p>
            {{ st("number") }}:
            <span dir="ltr">{{ current.snapshot?.number }}</span>
          </p>
          <p>
            {{ st("date") }}:
            <span dir="ltr">{{ current.snapshot?.draft.date }}</span>
          </p>
          <p>
            {{ st("sender") }}:
            {{ current.snapshot?.sender.profile.fullName }} —
            {{ current.snapshot?.sender.profile.title }}
          </p>
          <p>{{ current.snapshot?.sender.profile.organization }}</p>
          <p>
            {{ st("template") }}:
            {{ current.snapshot?.template.title || st("template") }} · v{{
              current.snapshot?.template.version
            }}
          </p>
          <h3>{{ st("recipients") }}</h3>
          <ul>
            <li
              v-for="person in current.snapshot?.recipients"
              :key="person.userId"
            >
              <span dir="ltr">{{ person.email }}</span> — {{ st(person.kind) }}
            </li>
          </ul>
          <button
            class="secondary"
            :disabled="busy"
            @click="task(() => operation('preview'))"
          >
            {{ st("preview") }}
          </button>
        </section>
        <form v-else @submit.prevent="task(saveDraft)">
          <fieldset :disabled="busy || generated" class="studio-fields">
            <label
              >{{ st("title")
              }}<input v-model="form.title" maxlength="160" required
            /></label>
            <label
              >{{ st("subject") }}<input v-model="form.subject" maxlength="200"
            /></label>
            <label
              >{{ st("language")
              }}<select v-model="form.language" :aria-label="st('language')">
                <option value="en-US">English</option>
                <option value="fa-IR">فارسی</option>
              </select></label
            >
            <label
              >{{ st("date") }}<input v-model="form.date" type="date"
            /></label>
            <label
              >{{ st("template")
              }}<select
                v-model="form.templateId"
                :disabled="current?.metadata.hasEditor"
                :aria-label="st('template')"
                @change="
                  form.language =
                    letterTemplates.find(
                      (x) => x.metadata.id === form.templateId,
                    )?.data.language || form.language
                "
              >
                <option value="">{{ st("choose") }}</option>
                <option
                  v-for="item in letterTemplates"
                  :key="item.metadata.id"
                  :value="item.metadata.id"
                >
                  {{ item.metadata.title }} · v{{ item.metadata.version }}
                </option>
              </select></label
            >
            <label
              >{{ st("sender")
              }}<select v-model="form.senderId" :aria-label="st('sender')">
                <option value="">{{ st("choose") }}</option>
                <option
                  v-for="item in senders"
                  :key="item.metadata.id"
                  :value="item.metadata.id"
                >
                  {{ item.data.fullName }} — {{ item.data.title }}
                </option>
              </select></label
            >
            <label
              >{{ st("signature")
              }}<select
                v-model="form.signatureId"
                :aria-label="st('signature')"
              >
                <option value="">{{ st("choose") }}</option>
                <option
                  v-for="item in images"
                  :key="item.metadata.id"
                  :value="item.metadata.id"
                >
                  {{ item.metadata.title }}
                </option>
              </select></label
            >
            <label
              >{{ st("logo")
              }}<select v-model="form.logoId" :aria-label="st('logo')">
                <option value="">{{ st("none") }}</option>
                <option
                  v-for="item in images"
                  :key="item.metadata.id"
                  :value="item.metadata.id"
                >
                  {{ item.metadata.title }}
                </option>
              </select></label
            >
            <label
              >{{ st("number")
              }}<input
                v-model="form.number"
                maxlength="80"
                :placeholder="st('automatic')"
            /></label>
            <label
              >{{ st("priority")
              }}<select v-model="form.priority" :aria-label="st('priority')">
                <option
                  v-for="value in ['normal', 'urgent', 'immediate']"
                  :key="value"
                  :value="value"
                >
                  {{ st(value) }}
                </option>
              </select></label
            >
            <label
              >{{ st("classification")
              }}<select
                v-model="form.classification"
                :aria-label="st('classification')"
              >
                <option
                  v-for="value in ['normal', 'confidential', 'strict']"
                  :key="value"
                  :value="value"
                >
                  {{ st(value) }}
                </option>
              </select></label
            >
            <div class="studio-wide">
              <h3>{{ st("body") }}</h3>
              <p class="muted">
                {{
                  st(
                    current?.metadata.hasEditor
                      ? "editorBodySaved"
                      : "writeHelp",
                  )
                }}
              </p>
              <button
                type="button"
                class="primary"
                :disabled="busy || !form.templateId"
                @click="task(writeLetter)"
              >
                {{
                  st(
                    current?.metadata.hasEditor
                      ? "continueWriting"
                      : "writeLetter",
                  )
                }}
              </button>
              <label v-if="!current?.metadata.hasEditor"
                >{{ st("bodyDocument")
                }}<select
                  v-model="form.bodyId"
                  :aria-label="st('bodyDocument')"
                >
                  <option value="">{{ st("bodyText") }}</option>
                  <option
                    v-for="item in documents"
                    :key="item.id"
                    :value="item.id"
                  >
                    {{ item.title }} · v{{ item.version }}
                  </option>
                </select></label
              >
              <label v-if="!current?.metadata.hasEditor && !form.bodyId"
                >{{ st("bodyText")
                }}<textarea
                  v-model="form.bodyText"
                  rows="9"
                  dir="auto"
                ></textarea>
              </label>
              <label v-if="!current?.metadata.hasEditor" class="studio-upload"
                >{{ st("uploadBody")
                }}<input
                  type="file"
                  accept=".docx,.odt,.rtf,.txt,.md,.json"
                  @change="
                    (event) => task(() => uploadDocument(event, 'body'))
                  "
              /></label>
            </div>
            <div class="studio-wide">
              <h3>{{ st("recipients") }}</h3>
              <div class="studio-search">
                <input
                  v-model="directorySearch"
                  type="search"
                  :placeholder="st('searchUsers')"
                  :aria-label="st('searchUsers')"
                  @keydown.enter.prevent="task(() => findUsers())"
                /><button
                  type="button"
                  class="secondary"
                  @click="task(() => findUsers())"
                >
                  {{ st("search") }}
                </button>
              </div>
              <div class="studio-directory">
                <button
                  v-for="person in directory"
                  :key="person.id"
                  type="button"
                  :disabled="
                    form.recipients.some((x) => x.userId === person.id)
                  "
                  :aria-label="st('selectRecipient') + ': ' + person.email"
                  @click="addRecipient(person)"
                >
                  <span dir="ltr">{{ person.email }}</span> ＋</button
                ><button
                  v-if="directoryMore"
                  type="button"
                  @click="task(() => findUsers(true))"
                >
                  {{ st("more") }}
                </button>
              </div>
              <div
                v-for="person in form.recipients"
                :key="person.userId"
                class="studio-recipient"
              >
                <span dir="ltr">{{ person.label }}</span
                ><select v-model="person.kind" :aria-label="st('recipients')">
                  <option value="to">{{ st("to") }}</option>
                  <option value="cc">{{ st("cc") }}</option></select
                ><label v-if="person.kind === 'cc'" class="studio-check"
                  ><input v-model="person.printOnLetter" type="checkbox" />{{
                    st("printCopy")
                  }}</label
                ><button
                  type="button"
                  @click="
                    form.recipients = form.recipients.filter(
                      (x) => x.userId !== person.userId,
                    )
                  "
                >
                  {{ st("remove") }}
                </button>
              </div>
            </div>
            <div class="studio-wide">
              <h3>{{ st("attachments") }}</h3>
              <p class="muted">{{ st("attachmentHelp") }}</p>
              <div class="studio-directory">
                <label
                  v-for="item in documents"
                  :key="item.id"
                  class="studio-check"
                  ><input
                    v-model="form.attachmentIds"
                    type="checkbox"
                    :value="item.id"
                  />{{ item.title }}</label
                >
              </div>
            </div>
            <label class="studio-check studio-wide"
              ><input v-model="form.includeQrCode" type="checkbox" />{{
                st("qr")
              }}</label
            ><label class="studio-check studio-wide"
              ><input v-model="form.copyVariants" type="checkbox" />{{
                st("variants")
              }}</label
            >
          </fieldset>
          <div class="studio-actions">
            <button
              v-if="!generated"
              class="primary"
              :disabled="busy"
              type="submit"
            >
              {{ st("saveDraft") }}</button
            ><button
              v-if="!generated"
              class="secondary"
              :disabled="busy || !current || dirty"
              type="button"
              @click="task(() => operation('validate'))"
            >
              {{ st("validate") }}</button
            ><button
              class="secondary"
              :disabled="busy || !current || dirty"
              type="button"
              @click="task(() => operation('preview'))"
            >
              {{ st("preview") }}</button
            ><button
              v-if="!generated"
              class="primary"
              :disabled="busy || !current || dirty"
              type="button"
              @click="task(() => operation('generate'))"
            >
              {{ st("generate") }}
            </button>
          </div>
        </form>
        <p v-if="dirty" class="muted">{{ st("needsSave") }}</p>
        <p v-if="validated && !generated" class="success" role="status">
          {{ st("valid") }}
        </p>
        <ul v-if="issues.length" class="studio-issues" role="alert">
          <li v-for="(issue, index) in issues" :key="index">
            <strong
              >{{
                st(
                  issue.field in aliases || fields.includes(issue.field)
                    ? "field" + issue.field
                    : issue.field,
                )
              }}:</strong
            >
            {{ st(issue.code === "date" ? "dateIssue" : issue.code) }}
          </li>
        </ul>
        <section v-if="artifacts.length">
          <h3>{{ st("outputs") }}</h3>
          <div
            v-for="file in artifacts"
            :key="file.id"
            class="studio-list-item"
          >
            <strong>{{ file.title }}</strong>
            <div class="studio-actions">
              <button
                v-for="format in ['docx', 'pdf', 'json']"
                :key="format"
                :disabled="busy"
                :aria-label="`${t('download')} ${format.toUpperCase()}: ${file.title}`"
                @click="task(() => download(file, format))"
              >
                {{ format.toUpperCase() }} ↓
              </button>
            </div>
          </div>
        </section>
      </section>
    </div>

    <section v-else-if="tab === 'templates'" class="studio-card">
      <p class="muted">{{ st("templateHelp") }}</p>
      <form @submit.prevent="task(() => saveTemplate(false))">
        <fieldset :disabled="busy" class="studio-fields">
          <label
            >{{ st("templateName")
            }}<input
              v-model="templateForm.title"
              maxlength="160"
              required /></label
          ><label
            >{{ st("kind")
            }}<select
              v-model="templateForm.kind"
              :aria-label="st('kind')"
              :disabled="!!templateForm.id"
            >
              <option value="document">{{ st("document") }}</option>
              <option value="letter">{{ st("letter") }}</option>
            </select></label
          >
          <label
            >{{ st("language")
            }}<select
              v-model="templateForm.language"
              :aria-label="st('language')"
              :disabled="!!templateForm.id"
            >
              <option value="en-US">English</option>
              <option value="fa-IR">فارسی</option>
            </select></label
          >
          <label
            v-if="
              templateForm.kind === 'letter' &&
              !templateForm.id &&
              !templateForm.sourceId
            "
          >
            {{ st("paper")
            }}<select v-model="templateForm.paper" :aria-label="st('paper')">
              <option value="">{{ st("paperAuto") }}</option>
              <option value="A5">A5</option>
              <option value="A4">A4</option>
              <option value="Letter">US Letter</option></select
            ><small>{{ st("paperHelp") }}</small>
          </label>
          <label v-if="!templateForm.id"
            >{{ st("source")
            }}<select
              v-model="templateForm.sourceId"
              :aria-label="st('source')"
              @change="task(sourceChanged)"
            >
              <option value="">{{ st("choose") }}</option>
              <option v-for="item in documents" :key="item.id" :value="item.id">
                {{ item.title }} · v{{ item.version }}
              </option>
            </select></label
          >
          <label v-if="!templateForm.id" class="studio-wide studio-upload"
            >{{ st("uploadTemplate")
            }}<input
              type="file"
              accept=".docx,.odt,.rtf,.txt,.md,.json"
              @change="
                (event) => task(() => uploadDocument(event, 'template'))
              "
          /></label>
          <div
            v-if="
              templateForm.kind === 'letter' &&
              (templateForm.sourceId || templateForm.id)
            "
            class="studio-wide"
          >
            <h3>{{ st("mappings") }}</h3>
            <p class="muted">{{ st("mappingHelp") }}</p>
            <div class="studio-fields">
              <label v-for="field in fields" :key="field"
                >{{ st("field" + field) }}
                {{ required.includes(field) ? "*" : ""
                }}<select
                  v-model="mappings[field]"
                  :aria-label="st('field' + field)"
                  :required="required.includes(field)"
                >
                  <option value="">{{ st("none") }}</option>
                  <option
                    v-for="name in bookmarkNames"
                    :key="name"
                    :value="name"
                  >
                    {{ name }}
                  </option>
                </select></label
              >
            </div>
          </div>
        </fieldset>
        <div class="studio-actions">
          <button
            class="primary"
            :disabled="busy || (!templateForm.id && !templateForm.sourceId)"
            type="submit"
          >
            {{ st(templateForm.id ? "saveMappings" : "saveTemplate") }}</button
          ><button
            v-if="!templateForm.id"
            class="secondary"
            :disabled="busy || !templateForm.title.trim()"
            type="button"
            @click="task(() => saveTemplate(true))"
          >
            {{ st("starter") }}</button
          ><button v-if="templateForm.id" type="button" @click="clearTemplate">
            {{ st("cancel") }}
          </button>
        </div>
      </form>
      <div class="studio-template-grid">
        <article
          v-for="item in templates"
          :key="item.metadata.id"
          class="studio-list-item"
        >
          <h3>{{ item.metadata.title }}</h3>
          <small>{{ st(item.data.kind) }} · v{{ item.metadata.version }}</small>
          <div class="studio-actions">
            <button
              v-if="item.data.kind === 'document'"
              class="secondary"
              :disabled="busy"
              @click="task(() => createDocument(item))"
            >
              {{ st("createDocument") }}</button
            ><button
              v-else
              class="secondary"
              @click="newLetter(item.metadata.id)"
            >
              {{ st("useTemplate") }}</button
            ><button
              v-if="item.data.kind === 'letter'"
              @click="task(() => editTemplate(item))"
            >
              {{ st("editMappings") }}</button
            ><button
              @click="
                task(() =>
                  api(
                    `/templates/${item.metadata.id}/download?version=${item.metadata.version}`,
                    { download: true },
                  ),
                )
              "
            >
              DOCX ↓</button
            ><button @click="task(() => remove(item, '/templates'))">
              {{ st("delete") }}
            </button>
          </div>
        </article>
      </div>
      <p v-if="!templates.length" class="muted">{{ st("empty") }}</p>
    </section>

    <section v-else-if="tab === 'senders'" class="studio-card">
      <form @submit.prevent="task(saveSender)">
        <fieldset :disabled="busy" class="studio-fields">
          <label
            >{{ st("fullName")
            }}<input
              v-model="senderForm.fullName"
              maxlength="160"
              required /></label
          ><label
            >{{ st("senderTitle")
            }}<input
              v-model="senderForm.title"
              maxlength="160"
              required /></label
          ><label
            >{{ st("organization")
            }}<input v-model="senderForm.organization" maxlength="500" /></label
          ><label
            >{{ st("address")
            }}<textarea
              v-model="senderForm.address"
              maxlength="500"
              rows="3"
            ></textarea>
          </label>
        </fieldset>
        <button class="primary" :disabled="busy">{{ st("saveSender") }}</button>
      </form>
      <div class="studio-template-grid">
        <article
          v-for="item in senders"
          :key="item.metadata.id"
          class="studio-list-item"
        >
          <h3>{{ item.data.fullName }}</h3>
          <p>{{ item.data.title }}</p>
          <p class="muted">{{ item.data.organization }}</p>
          <div class="studio-actions">
            <button @click="editSender(item)">{{ st("edit") }}</button
            ><button @click="task(() => remove(item, '/letters/senders'))">
              {{ st("delete") }}
            </button>
          </div>
        </article>
      </div>
    </section>
    <section v-else class="studio-card">
      <p class="muted">{{ st("imageHelp") }}</p>
      <form @submit.prevent="task(uploadImage)">
        <fieldset :disabled="busy" class="studio-fields">
          <label
            >{{ st("imageTitle")
            }}<input v-model="imageTitle" maxlength="160" required /></label
          ><label
            >{{ st("imageFile")
            }}<input
              ref="imageInput"
              type="file"
              accept="image/png,image/jpeg"
              required
              @change="imageFile = $event.target.files?.[0] || null"
          /></label>
        </fieldset>
        <button class="primary" :disabled="busy || !imageFile">
          {{ st("uploadImage") }}
        </button>
      </form>
      <div class="studio-template-grid">
        <article
          v-for="item in images"
          :key="item.metadata.id"
          class="studio-list-item"
        >
          <img
            class="studio-image"
            :src="`/api/letters/images/${item.metadata.id}?version=${item.metadata.version}`"
            :alt="item.metadata.title"
          />
          <h3>{{ item.metadata.title }}</h3>
          <button @click="task(() => remove(item, '/letters/images'))">
            {{ st("delete") }}
          </button>
        </article>
      </div>
    </section>
  </main>
</template>
