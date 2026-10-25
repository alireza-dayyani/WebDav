<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { api, resetCsrf } from "./api";
import { locale, language, t, errorMessage } from "./i18n";
import { normalizeSearch } from "./locales";
import LanguagePicker from "./LanguagePicker.vue";
import AdminPanel from "./AdminPanel.vue";
import { maxUploadBytes, updateFileLimits } from "./fileLimits";
const adminView = ref(false);
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
  adminView.value = true;
  history.replaceState(null, "", "/#/admin");
}
function closeAdministration() {
  adminView.value = false;
  history.replaceState(null, "", "/");
}

const documentLanguage = ref(language.value.tag),
  fileInput = ref(null),
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
const visible = computed(() =>
  documents.value.filter((d) =>
    normalizeSearch(d.title).includes(normalizeSearch(search.value)),
  ),
);
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
  const [items, formats] = await Promise.all([
    api("/documents/"),
    api("/documents/formats"),
  ]);
  documents.value = items;
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
  if (!canLeave()) return;
  busy.value = true;
  try {
    await api("/account/logout", { method: "POST" });
    user.value = null;
    documents.value = [];
    active.value = null;
    adminView.value = false;
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
  active.value = document;
  editorStatus.value = t("openingDocument");
  editorDirty.value = false;
  editorPending.value = false;
  editorFailed.value = false;
  editorSource.value = `/editor/index.html?id=${document.id}&lang=${locale.value}`;
  history.replaceState(null, "", `/#/documents/${document.id}`);
}
async function openFromHash() {
  if (location.hash === "#/admin" && user.value.isAdmin) {
    adminView.value = true;
    return;
  }
  const match = location.hash.match(/^#\/documents\/([a-f\d-]+)$/i);
  if (!match) return;
  const document = documents.value.find((d) => d.id === match[1]);
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
  if (kind === "create") documentLanguage.value = language.value.tag;
  dialogError.value = "";
}
async function submitDialog() {
  dialogBusy.value = true;
  dialogError.value = "";
  try {
    const current = dialog.value;
    if (current.kind === "create") {
      const document = await api("/documents/", {
        method: "POST",
        body: { title: title.value, language: documentLanguage.value },
      });
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
  <AdminPanel
    v-else-if="adminView && user.isAdmin"
    @close="closeAdministration"
    @session-changed="syncSession"
  />
  <div v-else class="workspace">
    <aside class="sidebar">
      <a class="brand" href="/" dir="ltr"
        ><img src="/icon.svg" alt="" />Inkstone<span class="brand-dot"
          >·</span
        ></a
      >
      <div class="sidebar-heading">{{ t("workspace") }}</div>
      <button class="nav-item active">
        <span aria-hidden="true">▤</span>{{ t("myDocuments")
        }}<span class="count">{{
          new Intl.NumberFormat(language.tag).format(documents.length)
        }}</span>
      </button>
      <div class="sidebar-note">
        <span class="note-mark">✳</span>
        <h3>{{ t("sidebarTitle") }}</h3>
        <p>{{ t("sidebarText") }}</p>
        <button @click="showDialog('create')">{{ t("startWriting") }} ↗</button>
      </div>
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
          <strong>{{ t("myDocuments") }}</strong></span
        >
        <div class="header-tools">
          <button
            v-if="user.isAdmin"
            class="secondary admin-entry"
            @click="showAdministration"
          >
            {{ t("administration") }}
          </button>
          <span class="private-badge">◇ {{ t("private") }}</span
          ><LanguagePicker />
        </div>
      </header>
      <div class="content-body">
        <div class="workspace-greeting">
          <div>
            <div class="eyebrow muted">{{ today }}</div>
            <h1>{{ t("place") }}</h1>
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
        <div class="start-card">
          <div class="blank-page" aria-hidden="true"><span>＋</span></div>
          <div>
            <div class="eyebrow muted">{{ t("fresh") }}</div>
            <h2>{{ t("page") }}</h2>
            <p>{{ t("pageText") }}</p>
            <button class="text-button" @click="showDialog('create')">
              {{ t("createBlank") }} ↗
            </button>
          </div>
          <span class="card-decoration" aria-hidden="true">{{
            locale === "en" ? "Aa" : "آ"
          }}</span>
        </div>
        <section class="documents-section">
          <div class="section-heading">
            <h2>
              {{ t("documents") }}
              <span>{{
                new Intl.NumberFormat(language.tag).format(documents.length)
              }}</span>
            </h2>
            <label class="search"
              ><span aria-hidden="true">⌕</span
              ><input
                v-model="search"
                type="search"
                :placeholder="t('find')"
                :aria-label="t('find')"
            /></label>
          </div>
          <p v-if="error" class="error" role="alert">{{ error }}</p>
          <div v-if="!visible.length" class="empty-state">
            <span aria-hidden="true">▤</span>
            <h3>{{ t(search ? "noMatches" : "empty") }}</h3>
            <p>{{ t(search ? "tryTitle" : "emptyText") }}</p>
            <button
              v-if="!search"
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
                  @click="open(document)"
                >
                  {{ document.title }}
                </button>
                <div class="document-meta">
                  <span>{{ date(document.updatedAt) }}</span
                  ><span>{{ size(document.size) }}</span>
                </div>
                <div class="document-actions">
                  <button @click="showDialog('rename', document)">
                    {{ t("rename") }}</button
                  ><button
                    :disabled="downloading"
                    @click="download(document, 'docx')"
                  >
                    {{ t("download") }} DOCX</button
                  ><button
                    class="delete-link"
                    @click="showDialog('delete', document)"
                  >
                    {{ t("delete") }}
                  </button>
                </div>
              </div>
            </article>
          </div>
        </section>
        <footer class="workspace-footer">
          <span>{{ t("pace") }}</span
          ><span>{{ t("savedPrivate") }}</span>
        </footer>
      </div>
    </main>
  </div>
  <div
    v-if="dialog"
    class="dialog-backdrop"
    @click.self="!dialogBusy && (dialog = null)"
  >
    <section
      class="dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="dialog-title"
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
          ><label for="document-language">{{ t("documentLanguage") }}</label
          ><select id="document-language" v-model="documentLanguage">
            <option value="en-US">{{ t("english") }}</option>
            <option value="fa-IR">{{ t("persian") }}</option>
            <option value="ar-SA">{{ t("arabic") }}</option>
            <option value="ur-PK">{{ t("urdu") }}</option>
          </select>
          <p class="field-help">{{ t("languageHelp") }}</p></template
        >
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
            :disabled="
              dialogBusy || (dialog.kind !== 'delete' && !title.trim())
            "
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
    </section>
  </div>
</template>
