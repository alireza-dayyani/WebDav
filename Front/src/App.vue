<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { api, allPages, resetCsrf, previewPdf } from "./api";
import DocumentHistory from "./DocumentHistory.vue";
import LibrarySettings from "./LibrarySettings.vue";
import CollaborationPanel from "./CollaborationPanel.vue";
import ReviewInbox from "./ReviewInbox.vue";
import AutomationPanel from "./AutomationPanel.vue";
import { locale, language, t, errorMessage } from "./i18n";
import LanguagePicker from "./LanguagePicker.vue";
import AppIcon from "./AppIcon.vue";
import ModalFrame from "./ModalFrame.vue";
import AdminPanel from "./AdminPanel.vue";
import StudioPanel from "./StudioPanel.vue";
import LetterEditor from "./LetterEditor.vue";
const activeLetter = ref(null),
  selectedLetterId = ref("");
function openLetterEditor(letter) {
  activeLetter.value = letter;
  history.replaceState(
    null,
    "",
    `/#/letters/${letter.id}/edit?version=${letter.version}`,
  );
}
function closeLetterEditor(letter) {
  activeLetter.value = null;
  selectedLetterId.value = letter.id;
  showStudio("letters");
  studioPanel.value?.resumeLetter(letter);
}
import { maxUploadBytes, updateFileLimits } from "./fileLimits";
const adminView = ref(false);
const studioView = ref("");
const studioPanel = ref(null),
  studioStarted = ref(false),
  studioTab = ref("letters");
const sectionTitle = computed(() =>
  adminView.value
    ? t("administration")
    : studioView.value
      ? t("studio." + studioTab.value)
      : t(trashView.value ? "trash" : "myDocuments"),
);
function studioNavigated(tab) {
  studioTab.value = tab;
  if (studioView.value) studioView.value = tab;
}
function showDocuments(trash = false) {
  adminView.value = false;
  studioView.value = "";
  trashView.value = trash;
  history.replaceState(null, "", "/");
  refresh().catch((e) => (error.value = errorMessage(e)));
}
const documentTemplates = ref([]),
  chosenTemplate = ref("");
const historyDocument = ref(null);
const librarySettings = ref(false),
  loadingMore = ref(false),
  libraryPage = ref(1),
  libraryMore = ref(false),
  libraryTotal = ref(0);
const shareDocument = ref(null),
  reviewInbox = ref(false);
const automationPanel = ref(false);
let searchTimer;
const trashView = ref(false);
let refreshTicket = 0;
async function toggleTrash() {
  trashView.value = !trashView.value;
  try {
    await refresh();
  } catch (e) {
    error.value = errorMessage(e);
  }
}
async function recoverDocument(document, purge = false) {
  if (purge && !window.confirm(t("purgeConfirm"))) return;
  error.value = "";
  try {
    await api(
      `/documents/${document.id}/${purge ? "purge?version=" + document.version : "recover"}`,
      purge
        ? { method: "DELETE" }
        : { method: "POST", body: { version: document.version } },
    );
    await refresh();
  } catch (e) {
    error.value = errorMessage(e);
  }
}
function showStudio(tab) {
  adminView.value = false;
  studioStarted.value = true;
  studioTab.value = tab;
  studioView.value = tab;
  history.replaceState(null, "", `/#/${tab}`);
}
function closeStudio() {
  studioView.value = "";
  history.replaceState(null, "", "/");
  refresh();
}
async function openStudioDocument(document) {
  studioView.value = "";
  await refresh();
  open(document);
}
const uploadLimit = computed(() =>
  new Intl.NumberFormat(language.value.tag).format(
    maxUploadBytes.value / 1024 / 1024,
  ),
);
async function syncSession() {
  user.value = await api("/account/me");
  if (!user.value.isAdmin) closeAdministration();
}
function showAdministration() {
  studioView.value = "";
  adminView.value = true;
  history.replaceState(null, "", "/#/admin");
}
function closeAdministration() {
  adminView.value = false;
  history.replaceState(null, "", "/");
}

const fileInput = ref(null),
  uploading = ref(false),
  downloading = ref(false),
  downloadFormat = ref("docx");

const user = ref(null),
  loading = ref(true),
  busy = ref(false),
  error = ref("");
const mode = ref("login"),
  email = ref(""),
  password = ref(""),
  search = ref(""),
  documents = ref([]);
const active = ref(null),
  editorFrame = ref(null),
  editorStatus = ref(t("openingDocument")),
  editorFailed = ref(false);
const editorDirty = ref(false),
  dialog = ref(null),
  title = ref(""),
  dialogBusy = ref(false),
  dialogError = ref("");
const editorSource = ref("");
const editorPending = ref(false);
watch(locale, () => {
  if (!editorFailed.value)
    editorStatus.value = t(
      editorPending.value ? "saving" : editorDirty.value ? "unsaved" : "saved",
    );
});
const visible = computed(() => documents.value);
watch(search, () => {
  clearTimeout(searchTimer);
  if (!user.value) return;
  searchTimer = setTimeout(
    () => refresh().catch((e) => (error.value = errorMessage(e))),
    250,
  );
});
async function loadMoreDocuments() {
  if (loadingMore.value) return;
  loadingMore.value = true;
  const ticket = refreshTicket;
  try {
    const result = await api(
      `/library?page=${libraryPage.value + 1}&trash=${trashView.value}&q=${encodeURIComponent(search.value)}`,
    );
    if (ticket !== refreshTicket) return;
    documents.value.push(...result.items);
    libraryPage.value++;
    libraryMore.value = result.hasMore;
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    loadingMore.value = false;
  }
}
const greeting = computed(
  () => user.value?.email?.split("@")[0] || t("writer"),
);
const today = computed(() =>
  new Intl.DateTimeFormat(language.value.tag, {
    weekday: "long",
    month: "long",
    day: "numeric",
  }).format(new Date()),
);
const date = (timestamp) =>
  new Intl.DateTimeFormat(language.value.tag, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(timestamp);
const size = (bytes) =>
  bytes
    ? t("kb", {
        value: new Intl.NumberFormat(language.value.tag).format(
          Math.max(1, Math.round(bytes / 1024)),
        ),
      })
    : t("new");
async function upload(event) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  error.value = "";
  uploading.value = true;
  try {
    updateFileLimits(await api("/documents/formats"));
    if (!file.size || file.size > maxUploadBytes.value) {
      error.value = t("tooLarge", { limit: uploadLimit.value });
      return;
    }
    const extension = file.name.split(".").pop().toLowerCase();
    if (!["docx", "odt", "rtf", "txt", "md", "json"].includes(extension)) {
      error.value = t("uploadType");
      return;
    }
    const body = new FormData();
    body.append("file", file);
    const document = await api("/documents/upload", { method: "POST", body });
    await refresh();
    open(document);
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    uploading.value = false;
  }
}

async function download(document, format = downloadFormat.value) {
  downloading.value = true;
  error.value = "";
  try {
    await api(
      `/documents/${document.id}/download?format=${format}&version=${document.version}`,
      { download: true },
    );
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    downloading.value = false;
  }
}
async function refresh() {
  const ticket = ++refreshTicket;
  const [items, formats, templates] = await Promise.all([
    api(
      `/library?page=1&trash=${trashView.value}&q=${encodeURIComponent(search.value)}`,
    ),
    api("/documents/formats"),
    allPages("/templates/"),
  ]);
  if (ticket !== refreshTicket) return;
  documents.value = items.items;
  libraryPage.value = 1;
  libraryMore.value = items.hasMore;
  libraryTotal.value = items.total;
  documentTemplates.value = templates.filter((x) => x.data.kind === "document");
  updateFileLimits(formats);
}
async function initialize() {
  try {
    user.value = await api("/account/me");
    await refresh();
    await openFromHash();
  } catch (e) {
    if (e.status !== 401) error.value = errorMessage(e, mode.value);
  } finally {
    loading.value = false;
  }
}
async function authenticate() {
  busy.value = true;
  error.value = "";
  try {
    user.value = await api("/account/" + mode.value, {
      method: "POST",
      body: { email: email.value, password: password.value },
    });
    password.value = "";
    resetCsrf();
    await refresh();
    await openFromHash();
  } catch (e) {
    error.value = errorMessage(e, mode.value);
  } finally {
    busy.value = false;
  }
}
async function logout() {
  if (!canLeave() || (studioPanel.value && !studioPanel.value.canLeave()))
    return;
  busy.value = true;
  try {
    await api("/account/logout", { method: "POST" });
    user.value = null;
    search.value = "";
    refreshTicket++;
    documents.value = [];
    trashView.value = false;
    historyDocument.value = null;
    librarySettings.value = false;
    shareDocument.value = null;
    reviewInbox.value = false;
    automationPanel.value = false;
    active.value = null;
    adminView.value = false;
    studioView.value = "";
    studioStarted.value = false;
    resetCsrf();
    history.replaceState(null, "", "/");
  } catch (e) {
    error.value = errorMessage(e, mode.value);
  } finally {
    busy.value = false;
  }
}
function canLeave() {
  return !editorDirty.value || window.confirm(t("leave"));
}
function open(document) {
  if (document.permission === "read") {
    previewPdf(
      `/documents/${document.id}/download?format=pdf&version=${document.version}`,
    ).catch((e) => (error.value = errorMessage(e)));
    return;
  }
  active.value = document;
  editorStatus.value = t("openingDocument");
  editorDirty.value = false;
  editorPending.value = false;
  editorFailed.value = false;
  editorSource.value = `/editor/index.html?id=${document.id}&lang=${locale.value}`;
  history.replaceState(null, "", `/#/documents/${document.id}`);
}
async function openFromHash() {
  const letterMatch = location.hash.match(
    /^#\/letters\/([a-f\d-]+)\/edit\?version=(\d+)$/i,
  );
  if (letterMatch) {
    studioView.value = "letters";
    const result = await api(
      `/letters/${letterMatch[1]}?version=${letterMatch[2]}`,
    );
    openLetterEditor(result.metadata);
    return;
  }
  if (location.hash === "#/letters" || location.hash === "#/templates") {
    showStudio(location.hash.slice(2));
    return;
  }
  if (location.hash === "#/admin" && user.value.isAdmin) {
    adminView.value = true;
    return;
  }
  const match = location.hash.match(/^#\/documents\/([a-f\d-]+)$/i);
  if (!match) return;
  const document = await api(`/documents/${match[1]}/access`);
  if (document) open(document);
  else {
    error.value = t("unavailable");
    history.replaceState(null, "", "/");
  }
}
async function closeEditor() {
  if (!canLeave()) return;
  active.value = null;
  history.replaceState(null, "", "/");
  try {
    await refresh();
  } catch (e) {
    error.value = errorMessage(e, mode.value);
  }
}
function showDialog(kind, document = null) {
  dialog.value = { kind, document };
  title.value = document?.title || "";
  chosenTemplate.value = "";
  dialogError.value = "";
}
async function submitDialog() {
  dialogBusy.value = true;
  dialogError.value = "";
  try {
    const current = dialog.value;
    if (current.kind === "create") {
      const template = documentTemplates.value.find(
        (x) => x.metadata.id === chosenTemplate.value,
      );
      const document = await api(
        template ? `/templates/${template.metadata.id}/render` : "/documents/",
        {
          method: "POST",
          body: template
            ? {
                title: title.value,
                version: template.metadata.version,
                values: {},
              }
            : { title: title.value },
        },
      );
      dialog.value = null;
      open(document);
    } else if (current.kind === "rename") {
      await api("/documents/" + current.document.id, {
        method: "PATCH",
        body: { title: title.value, version: current.document.version },
      });
      dialog.value = null;
      await refresh();
    } else {
      await api(
        `/documents/${current.document.id}?version=${current.document.version}`,
        { method: "DELETE" },
      );
      dialog.value = null;
      await refresh();
    }
  } catch (e) {
    dialogError.value = errorMessage(e);
  } finally {
    dialogBusy.value = false;
  }
}
function requestSave() {
  editorFrame.value?.contentWindow?.postMessage(
    { type: "inkstone:save-request" },
    location.origin,
  );
}
function editorMessage(event) {
  if (
    event.origin !== location.origin ||
    event.source !== editorFrame.value?.contentWindow ||
    !event.data?.type?.startsWith("inkstone:")
  )
    return;
  const message = event.data;
  if (message.type === "inkstone:state") {
    editorPending.value = message.pending;
    editorDirty.value = message.dirty || message.pending;
    if (!editorFailed.value)
      editorStatus.value = message.pending
        ? t("saving")
        : message.dirty
          ? t("unsaved")
          : t("saved");
  } else if (message.type === "inkstone:saved") {
    active.value = { ...active.value, ...message.document };
    editorStatus.value = t("saved");
    editorFailed.value = false;
    history.replaceState(null, "", `/#/documents/${message.document.id}`);
  } else if (message.type === "inkstone:error") {
    editorStatus.value = errorMessage({
      status: message.status,
      message: message.message,
      code: message.code,
      maxBytes: message.maxBytes,
    });
    editorFailed.value = true;
  } else if (message.type === "inkstone:ready") {
    if (message.document)
      active.value = { ...active.value, ...message.document };
    editorStatus.value = t("saved");
  }
}
function unload(event) {
  if (editorDirty.value) {
    event.preventDefault();
    event.returnValue = "";
  }
}
onMounted(() => {
  window.addEventListener("message", editorMessage);
  window.addEventListener("beforeunload", unload);
  initialize();
});
onBeforeUnmount(() => {
  clearTimeout(searchTimer);
  window.removeEventListener("message", editorMessage);
  window.removeEventListener("beforeunload", unload);
});
</script>

<template>
  <div v-if="loading" class="loading">
    <img src="/icon.svg" alt="" width="48" />
    <p>{{ t("opening") }}</p>
  </div>
  <div v-else-if="!user" class="auth-layout">
    <aside class="auth-story">
      <a class="brand brand-light" href="/" dir="ltr"
        ><img src="/icon.svg" alt="" />Inkstone<span class="brand-dot"
          >·</span
        ></a
      >
      <div class="story-copy">
        <div class="eyebrow">{{ t("storyTag") }}</div>
        <h1>{{ t("storyTitle") }}</h1>
        <p>{{ t("storyText") }}</p>
      </div>
      <div class="story-bottom">
        <span class="story-rule"></span><span>{{ t("yourWords") }}</span
        ><span dir="ltr">01 — ∞</span>
      </div>
      <div class="paper-art" aria-hidden="true">
        <span>{{ t("page") }}</span>
        <div></div>
        <div></div>
        <div></div>
        <i>i.</i>
      </div>
    </aside>
    <main class="auth-main">
      <div class="auth-top">
        <span>{{ t("place") }}</span
        ><LanguagePicker />
      </div>
      <form class="auth-form" @submit.prevent="authenticate">
        <div class="eyebrow muted">{{ t("personal") }}</div>
        <h2>{{ t(mode === "login" ? "welcome" : "join") }}</h2>
        <p class="auth-intro">
          {{ t(mode === "login" ? "loginIntro" : "registerIntro") }}
        </p>
        <div class="auth-tabs" role="group" :aria-label="t('register')">
          <button
            type="button"
            :class="{ selected: mode === 'login' }"
            @click="
              mode = 'login';
              error = '';
            "
          >
            {{ t("login") }}
          </button>
          <button
            type="button"
            :class="{ selected: mode === 'register' }"
            @click="
              mode = 'register';
              error = '';
            "
          >
            {{ t("register") }}
          </button>
        </div>
        <label for="email">{{ t("email") }}</label
        ><input
          id="email"
          v-model="email"
          dir="ltr"
          type="email"
          placeholder="you@example.com"
          autocomplete="email"
          maxlength="254"
          required
        />
        <label for="password">{{ t("password") }}</label
        ><input
          id="password"
          v-model="password"
          dir="ltr"
          type="password"
          :autocomplete="mode === 'login' ? 'current-password' : 'new-password'"
          :minlength="mode === 'register' ? 10 : 1"
          maxlength="128"
          :placeholder="t('passwordPlaceholder')"
          required
        />
        <p v-if="mode === 'register'" class="field-help">
          {{ t("passwordHelp") }}
        </p>
        <p v-if="error" class="error" role="alert">{{ error }}</p>
        <button class="primary auth-submit" :disabled="busy">
          {{ t(busy ? "wait" : mode === "login" ? "enter" : "createAccount")
          }}<span aria-hidden="true">↗</span>
        </button>
        <p class="privacy-note">◇ {{ t("privacy") }}</p>
      </form>
      <footer class="auth-footer">
        <span>{{ t("footer") }}</span
        ><span dir="ltr">Inkstone · {{ new Date().getFullYear() }}</span>
      </footer>
    </main>
  </div>
  <LetterEditor
    v-else-if="activeLetter"
    :letter="activeLetter"
    @close="closeLetterEditor"
  />
  <div v-else-if="active" class="editor-layout">
    <header class="editor-header">
      <button class="back-button" @click="closeEditor">
        <span class="back-arrow">←</span> <span>{{ t("myDocuments") }}</span>
      </button>
      <div class="editor-title">
        <strong dir="auto">{{ active.title }}</strong
        ><span :class="{ 'save-error': editorFailed }" role="status">{{
          editorStatus
        }}</span>
      </div>
      <div class="editor-tools">
        <LanguagePicker /><select
          v-model="downloadFormat"
          :aria-label="t('format')"
          dir="ltr"
        >
          <option
            v-for="format in ['docx', 'pdf', 'txt', 'md', 'json']"
            :key="format"
            :value="format"
          >
            {{ format.toUpperCase() }}
          </option>
        </select>
        <button
          class="secondary"
          :disabled="
            downloading || editorDirty || editorPending || editorFailed
          "
          :title="editorDirty ? t('saveFirst') : t('download')"
          @click="download(active)"
        >
          {{ t(downloading ? "downloading" : "download") }}
        </button>
        <button class="primary" :disabled="editorPending" @click="requestSave">
          {{ t(editorPending ? "saving" : "save") }}
        </button>
      </div>
    </header>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <iframe
      ref="editorFrame"
      :src="editorSource"
      :title="t('editor')"
      allow="clipboard-read; clipboard-write; fullscreen"
      class="editor-frame"
    ></iframe>
    <div v-if="editorFailed" class="editor-warning" role="alert">
      {{ editorStatus }} {{ t("recovery") }}
    </div>
  </div>
  <div v-if="user" v-show="!active && !activeLetter" class="workspace">
    <aside class="sidebar">
      <a class="brand" href="/" dir="ltr" @click.prevent="showDocuments()"
        ><img src="/icon.svg" alt="" />Inkstone<span class="brand-dot"
          >·</span
        ></a
      >
      <div class="sidebar-heading">{{ t("workspace") }}</div>
      <nav class="workspace-nav" :aria-label="t('workspace')">
        <button
          class="nav-item"
          :class="{ active: !trashView && !studioView && !adminView }"
          @click="showDocuments()"
        >
          <AppIcon name="documents" />{{ t("myDocuments")
          }}<span class="count">{{
            new Intl.NumberFormat(language.tag).format(documents.length)
          }}</span>
        </button>
        <button
          class="nav-item"
          :class="{ active: studioView === 'templates' }"
          @click="showStudio('templates')"
        >
          <AppIcon name="template" />{{ t("studio.templates") }}
        </button>
        <button
          class="nav-item"
          :class="{ active: studioView && studioView !== 'templates' }"
          @click="showStudio('letters')"
        >
          <AppIcon name="mail" />{{ t("studio.letters") }}
        </button>
        <button class="nav-item" @click="automationPanel = true">
          <AppIcon name="bolt" />{{ t("ws.automation") }}
        </button>
        <button class="nav-item" @click="reviewInbox = true">
          <AppIcon name="review" />{{ t("ws.reviews") }}
        </button>
        <div class="nav-divider"></div>
        <button class="nav-item" @click="librarySettings = true">
          <AppIcon name="storage" />{{ t("ws.storage") }}
        </button>
        <button
          class="nav-item"
          :class="{ active: trashView && !studioView && !adminView }"
          @click="showDocuments(true)"
        >
          <AppIcon name="trash" />{{ t("trash") }}
        </button>
        <button
          v-if="user.isAdmin"
          class="nav-item"
          :class="{ active: adminView }"
          @click="showAdministration"
        >
          <AppIcon name="settings" />{{ t("administration") }}
        </button>
      </nav>
      <div class="account">
        <span class="avatar">{{ greeting.slice(0, 1).toUpperCase() }}</span>
        <div>
          <strong dir="auto">{{ greeting }}</strong
          ><span :title="user.email" dir="ltr">{{ user.email }}</span>
        </div>
        <button
          class="icon-button"
          :title="t('logout')"
          :aria-label="t('logout')"
          :disabled="busy"
          @click="logout"
        >
          ↪
        </button>
      </div>
    </aside>
    <main class="main-content">
      <header class="workspace-top">
        <span
          >{{ t("workspace") }} <span class="crumb">/</span>
          <strong>{{ sectionTitle }}</strong></span
        >
        <div class="header-tools">
          <span class="private-badge">◇ {{ t("private") }}</span
          ><LanguagePicker />
        </div>
      </header>
      <AdminPanel
        v-if="adminView && user.isAdmin"
        embedded
        @close="closeAdministration"
        @session-changed="syncSession"
      />
      <StudioPanel
        v-if="studioStarted"
        v-show="studioView && !adminView"
        ref="studioPanel"
        embedded
        :initial-tab="studioTab"
        :initial-letter-id="selectedLetterId"
        @tab-change="studioNavigated"
        @close="closeStudio"
        @open-document="openStudioDocument"
        @open-letter-editor="openLetterEditor"
      />
      <div v-show="!studioView && !adminView" class="content-body">
        <div class="workspace-greeting">
          <div>
            <div class="eyebrow muted">{{ today }}</div>
            <h1>{{ t(trashView ? "trash" : "myDocuments") }}</h1>
            <p>{{ t("greeting", { name: greeting }) }}</p>
          </div>
          <div class="file-tools">
            <button
              class="secondary"
              :disabled="uploading"
              @click="fileInput.click()"
            >
              ↑ {{ t(uploading ? "uploading" : "upload") }}</button
            ><button class="primary" @click="showDialog('create')">
              ＋ {{ t("new") }}
            </button>
          </div>
        </div>
        <input
          ref="fileInput"
          class="visually-hidden"
          type="file"
          accept=".docx,.odt,.rtf,.txt,.md,.json"
          :aria-label="t('upload')"
          @change="upload"
        />
        <p class="upload-help">{{ t("uploadHelp", { limit: uploadLimit }) }}</p>
        <section class="documents-section">
          <div class="section-heading">
            <h2>
              {{ t(trashView ? "trash" : "documents") }}
              <span>{{
                new Intl.NumberFormat(language.tag).format(libraryTotal)
              }}</span>
            </h2>
            <label class="search"
              ><AppIcon name="search" /><input
                v-model="search"
                type="search"
                maxlength="200"
                :placeholder="t('find')"
                :aria-label="t('find')"
            /></label>
          </div>
          <p v-if="error" class="error" role="alert">{{ error }}</p>
          <div v-if="!visible.length" class="empty-state">
            <span class="empty-symbol" aria-hidden="true"
              ><AppIcon name="documents"
            /></span>
            <h3>
              {{ t(search ? "noMatches" : trashView ? "emptyTrash" : "empty") }}
            </h3>
            <p>
              {{
                t(search ? "tryTitle" : trashView ? "trashHelp" : "emptyText")
              }}
            </p>
            <button
              v-if="!search && !trashView"
              class="text-button"
              @click="showDialog('create')"
            >
              {{ t("create") }} ↗
            </button>
          </div>
          <div v-else class="document-grid">
            <article
              v-for="document in visible"
              :key="document.id"
              class="document-card"
            >
              <button
                class="document-preview"
                :aria-label="t('open', { title: document.title })"
                :disabled="trashView"
                @click="open(document)"
              >
                <div class="preview-paper" aria-hidden="true">
                  <b dir="auto">{{ document.title }}</b
                  ><span></span><span></span><span></span
                  ><span class="short-line"></span>
                </div>
                <span class="document-type">{{ t("document") }}</span>
              </button>
              <div class="document-info">
                <button
                  class="document-name"
                  dir="auto"
                  :disabled="trashView"
                  @click="open(document)"
                >
                  {{ document.title }}
                </button>
                <div class="document-meta">
                  <span>{{ date(document.updatedAt) }}</span
                  ><span>{{ size(document.size) }}</span>
                </div>
                <div v-if="trashView" class="document-actions">
                  <button @click="recoverDocument(document)">
                    {{ t("restore") }}
                  </button>
                  <button
                    class="delete-link"
                    @click="recoverDocument(document, true)"
                  >
                    {{ t("purge") }}
                  </button>
                </div>
                <div v-else class="document-actions">
                  <button
                    v-if="
                      !document.permission || document.permission === 'owner'
                    "
                    @click="shareDocument = document"
                  >
                    {{ t("ws.sharing") }}
                  </button>
                  <button
                    v-if="
                      !document.permission || document.permission === 'owner'
                    "
                    @click="historyDocument = document"
                  >
                    {{ t("history") }}
                  </button>
                  <button
                    v-if="document.permission !== 'read'"
                    @click="showDialog('rename', document)"
                  >
                    {{ t("rename") }}</button
                  ><button
                    :disabled="downloading"
                    @click="download(document, 'docx')"
                  >
                    {{ t("download") }} DOCX</button
                  ><button
                    class="delete-link"
                    v-if="
                      !document.permission || document.permission === 'owner'
                    "
                    @click="showDialog('delete', document)"
                  >
                    {{ t("delete") }}
                  </button>
                </div>
              </div>
            </article>
          </div>
          <button
            v-if="libraryMore"
            :disabled="loadingMore"
            @click="loadMoreDocuments"
          >
            {{ t("loadMore") }}
          </button>
        </section>
        <footer class="workspace-footer">
          <span>{{ t("pace") }}</span
          ><span>{{ t("savedPrivate") }}</span>
        </footer>
      </div>
    </main>
  </div>
  <DocumentHistory
    v-if="user && historyDocument"
    :document="historyDocument"
    @close="historyDocument = null"
    @restored="
      historyDocument = null;
      refresh();
    "
  />
  <LibrarySettings
    v-if="user && librarySettings"
    @close="librarySettings = false"
  />
  <CollaborationPanel
    v-if="user && shareDocument"
    :document="shareDocument"
    @close="
      shareDocument = null;
      refresh();
    "
  />
  <ReviewInbox v-if="user && reviewInbox" @close="reviewInbox = false" />
  <AutomationPanel
    v-if="user && automationPanel"
    @close="
      automationPanel = false;
      refresh();
    "
  />
  <ModalFrame
    v-if="dialog"
    :busy="dialogBusy"
    labelledby="dialog-title"
    @close="dialog = null"
  >
    <form @submit.prevent="submitDialog">
      <div class="eyebrow muted">{{ t("workspace") }}</div>
      <h2 id="dialog-title">
        {{
          t(
            dialog.kind === "create"
              ? "createTitle"
              : dialog.kind === "rename"
                ? "renameTitle"
                : "deleteTitle",
          )
        }}
      </h2>
      <p v-if="dialog.kind === 'delete'">
        {{ t("deleteText", { title: dialog.document.title }) }}
      </p>
      <template v-else
        ><label for="title">{{ t("title") }}</label
        ><input
          id="title"
          v-model="title"
          dir="auto"
          :placeholder="t('untitled')"
          maxlength="160"
          required
          autofocus
      /></template>
      <template v-if="dialog.kind === 'create'"
        ><label for="document-template">{{ t("studio.templateSelect") }}</label>
        <select id="document-template" v-model="chosenTemplate">
          <option value="">{{ t("createBlank") }}</option>
          <option
            v-for="item in documentTemplates"
            :key="item.metadata.id"
            :value="item.metadata.id"
          >
            {{ item.metadata.title }} · v{{ item.metadata.version }}
          </option>
        </select>
      </template>
      <p v-if="dialogError" class="error" role="alert">{{ dialogError }}</p>
      <div class="dialog-actions">
        <button
          type="button"
          class="secondary"
          :disabled="dialogBusy"
          @click="dialog = null"
        >
          {{ t("cancel") }}</button
        ><button
          :class="dialog.kind === 'delete' ? 'danger' : 'primary'"
          :disabled="dialogBusy || (dialog.kind !== 'delete' && !title.trim())"
        >
          {{
            t(
              dialogBusy
                ? "wait"
                : dialog.kind === "create"
                  ? "create"
                  : dialog.kind === "rename"
                    ? "saveName"
                    : "deleteDocument",
            )
          }}
        </button>
      </div>
    </form>
  </ModalFrame>
</template>
