<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { api, resetCsrf } from "./api";

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
  editorStatus = ref("Opening document…"),
  editorFailed = ref(false);
const editorDirty = ref(false),
  dialog = ref(null),
  title = ref(""),
  dialogBusy = ref(false),
  dialogError = ref("");
const editorSource = ref("");
const editorPending = ref(false);
const visible = computed(() =>
  documents.value.filter((d) =>
    d.title.toLocaleLowerCase().includes(search.value.toLocaleLowerCase()),
  ),
);
const greeting = computed(() => user.value?.email?.split("@")[0] || "writer");
const today = new Intl.DateTimeFormat(undefined, {
  weekday: "long",
  month: "long",
  day: "numeric",
}).format(new Date());
const date = (timestamp) =>
  new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(timestamp);
const size = (bytes) =>
  bytes ? `${Math.max(1, Math.round(bytes / 1024))} KB` : "New document";
async function refresh() {
  documents.value = await api("/documents/");
}
async function initialize() {
  try {
    user.value = await api("/account/me");
    await refresh();
    await openFromHash();
  } catch (e) {
    if (e.status !== 401) error.value = e.message;
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
    error.value = e.message;
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
    resetCsrf();
    history.replaceState(null, "", "/");
  } catch (e) {
    error.value = e.message;
  } finally {
    busy.value = false;
  }
}
function canLeave() {
  return (
    !editorDirty.value ||
    window.confirm(
      "Your document has unsaved changes. Leave without saving them?",
    )
  );
}
function open(document) {
  active.value = document;
  editorStatus.value = "Opening document…";
  editorDirty.value = false;
  editorPending.value = false;
  editorFailed.value = false;
  editorSource.value = `/editor/index.html?id=${document.id}`;
  history.replaceState(null, "", `/#/documents/${document.id}`);
}
async function openFromHash() {
  const match = location.hash.match(/^#\/documents\/([a-f\d-]+)$/i);
  if (!match) return;
  const document = documents.value.find((d) => d.id === match[1]);
  if (document) open(document);
  else {
    error.value = "This document is unavailable in your account.";
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
    error.value = e.message;
  }
}
function showDialog(kind, document = null) {
  dialog.value = { kind, document };
  title.value = document?.title || "";
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
        body: { title: title.value },
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
    dialogError.value = e.message;
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
        ? "Saving…"
        : message.dirty
          ? "Unsaved changes"
          : "All changes saved";
  } else if (message.type === "inkstone:saved") {
    active.value = { ...active.value, ...message.document };
    editorStatus.value = "All changes saved";
    editorFailed.value = false;
    history.replaceState(null, "", `/#/documents/${message.document.id}`);
  } else if (message.type === "inkstone:error") {
    editorStatus.value = message.message;
    editorFailed.value = true;
  } else if (message.type === "inkstone:ready")
    editorStatus.value = "All changes saved";
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
    <p>Opening your workspace…</p>
  </div>
  <div v-else-if="!user" class="auth-layout">
    <aside class="auth-story">
      <a class="brand brand-light" href="/"
        ><img src="/icon.svg" alt="" />Inkstone<span class="brand-dot"
          >·</span
        ></a
      >
      <div class="story-copy">
        <div class="eyebrow">ROOM TO THINK. SPACE TO WRITE.</div>
        <h1>Good things<br />start with<br /><em>a blank page.</em></h1>
        <p>
          A quiet home for your ideas, first drafts,<br class="desktop-break" />
          and everything they become.
        </p>
      </div>
      <div class="story-bottom">
        <span class="story-rule"></span
        ><span>YOUR WORDS, IN YOUR OWN SPACE.</span><span>01 — ∞</span>
      </div>
      <div class="paper-art" aria-hidden="true">
        <span>Ideas worth keeping.</span>
        <div></div>
        <div></div>
        <div></div>
        <i>i.</i>
      </div>
    </aside>
    <main class="auth-main">
      <div class="auth-top">A place for your words.</div>
      <form class="auth-form" @submit.prevent="authenticate">
        <div class="eyebrow muted">YOUR PERSONAL WORKSPACE</div>
        <h2>
          {{ mode === "login" ? "Welcome back." : "Make yourself at home." }}
        </h2>
        <p class="auth-intro">
          {{
            mode === "login"
              ? "Pick up where your thoughts left off."
              : "Create an account and give your ideas a home."
          }}
        </p>
        <div class="auth-tabs" role="group" aria-label="Account action">
          <button
            type="button"
            :class="{ selected: mode === 'login' }"
            @click="
              mode = 'login';
              error = '';
            "
          >
            Sign in</button
          ><button
            type="button"
            :class="{ selected: mode === 'register' }"
            @click="
              mode = 'register';
              error = '';
            "
          >
            Create account
          </button>
        </div>
        <label for="email">Email address</label
        ><input
          id="email"
          v-model="email"
          type="email"
          placeholder="you@example.com"
          autocomplete="email"
          maxlength="254"
          required
        />
        <label for="password">Password</label
        ><input
          id="password"
          v-model="password"
          type="password"
          :autocomplete="mode === 'login' ? 'current-password' : 'new-password'"
          :minlength="mode === 'register' ? 10 : 1"
          maxlength="128"
          placeholder="Enter your password"
          required
        />
        <p v-if="mode === 'register'" class="field-help">
          10+ characters, with uppercase, lowercase, and a number.
        </p>
        <p v-if="error" class="error" role="alert">{{ error }}</p>
        <button class="primary auth-submit" :disabled="busy">
          {{
            busy
              ? "One moment…"
              : mode === "login"
                ? "Enter your workspace"
                : "Create your workspace"
          }}<span aria-hidden="true">↗</span>
        </button>
        <p class="privacy-note">
          <span aria-hidden="true">◇</span> Your documents belong to you. Only
          you can access them.
        </p>
      </form>
      <footer class="auth-footer">
        <span>MADE FOR THE WORDS THAT MATTER.</span
        ><span>Inkstone · {{ new Date().getFullYear() }}</span>
      </footer>
    </main>
  </div>
  <div v-else-if="active" class="editor-layout">
    <header class="editor-header">
      <button class="back-button" @click="closeEditor">
        ← <span>My documents</span>
      </button>
      <div class="editor-title">
        <strong>{{ active.title }}</strong
        ><span :class="{ 'save-error': editorFailed }" role="status">{{
          editorStatus
        }}</span>
      </div>
      <button class="primary" :disabled="editorPending" @click="requestSave">
        {{ editorPending ? "Saving…" : "Save document" }}
      </button>
    </header>
    <iframe
      ref="editorFrame"
      :src="editorSource"
      title="Document editor"
      allow="clipboard-read; clipboard-write; fullscreen"
      class="editor-frame"
    ></iframe>
    <div v-if="editorFailed" class="editor-warning" role="alert">
      {{ editorStatus }} Use File → Save As to download a copy if you need to
      keep unsaved work.
    </div>
  </div>
  <div v-else class="workspace">
    <aside class="sidebar">
      <a class="brand" href="/"
        ><img src="/icon.svg" alt="" />Inkstone<span class="brand-dot"
          >·</span
        ></a
      >
      <div class="sidebar-heading">WORKSPACE</div>
      <button class="nav-item active">
        <span aria-hidden="true">▤</span> My documents
        <span class="count">{{ documents.length }}</span>
      </button>
      <div class="sidebar-note">
        <span class="note-mark">✳</span>
        <h3>A little space.<br />A lot of possibility.</h3>
        <p>Your next great idea<br />starts right here.</p>
        <button @click="showDialog('create')">
          Start writing <span>↗</span>
        </button>
      </div>
      <div class="account">
        <span class="avatar">{{ greeting.slice(0, 1).toUpperCase() }}</span>
        <div>
          <strong>{{ greeting }}</strong
          ><span :title="user.email">{{ user.email }}</span>
        </div>
        <button
          class="icon-button"
          title="Sign out"
          aria-label="Sign out"
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
          >My workspace <span class="crumb">/</span>
          <strong>Documents</strong></span
        ><span class="private-badge"
          ><span aria-hidden="true">◇</span> Private workspace</span
        >
      </header>
      <div class="content-body">
        <div class="workspace-greeting">
          <div>
            <div class="eyebrow muted">{{ today }}</div>
            <h1>A place for your words<span>.</span></h1>
            <p>Make something worth coming back to, {{ greeting }}.</p>
          </div>
          <button class="primary" @click="showDialog('create')">
            <span aria-hidden="true">＋</span> New document
          </button>
        </div>
        <div class="start-card">
          <div class="blank-page" aria-hidden="true"><span>＋</span></div>
          <div>
            <div class="eyebrow muted">A FRESH START</div>
            <h2>Every idea deserves a page.</h2>
            <p>No distractions. Just you and what comes next.</p>
            <button class="text-button" @click="showDialog('create')">
              Create a blank document <span aria-hidden="true">↗</span>
            </button>
          </div>
          <span class="card-decoration" aria-hidden="true">Aa</span>
        </div>
        <section class="documents-section">
          <div class="section-heading">
            <h2>
              Your documents <span>{{ documents.length }}</span>
            </h2>
            <label class="search"
              ><span aria-hidden="true">⌕</span
              ><input
                v-model="search"
                type="search"
                placeholder="Find a document…"
                aria-label="Search documents"
            /></label>
          </div>
          <p v-if="error" class="error" role="alert">{{ error }}</p>
          <div v-if="!visible.length" class="empty-state">
            <span aria-hidden="true">▤</span>
            <h3>
              {{
                search ? "No matching documents" : "Your first page is waiting."
              }}
            </h3>
            <p>
              {{
                search
                  ? "Try another title."
                  : "Create a document and it will be right here whenever you return."
              }}
            </p>
            <button
              v-if="!search"
              class="text-button"
              @click="showDialog('create')"
            >
              Write something new ↗
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
                :aria-label="`Open ${document.title}`"
                @click="open(document)"
              >
                <div class="preview-paper" aria-hidden="true">
                  <b>{{ document.title }}</b
                  ><span></span><span></span><span></span
                  ><span class="short-line"></span>
                </div>
                <span class="document-type">DOCUMENT</span>
              </button>
              <div class="document-info">
                <button class="document-name" @click="open(document)">
                  {{ document.title }}
                </button>
                <div class="document-meta">
                  <span>{{ date(document.updatedAt) }}</span
                  ><span>{{ size(document.size) }}</span>
                </div>
                <div class="document-actions">
                  <button @click="showDialog('rename', document)">Rename</button
                  ><button
                    class="delete-link"
                    @click="showDialog('delete', document)"
                  >
                    Delete
                  </button>
                </div>
              </div>
            </article>
          </div>
        </section>
        <footer class="workspace-footer">
          <span>YOUR SPACE. YOUR PACE. YOUR WORDS.</span
          ><span>Saved privately, ready when you are.</span>
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
        <div class="eyebrow muted">YOUR WORKSPACE</div>
        <h2 id="dialog-title">
          {{
            dialog.kind === "create"
              ? "Something new starts here."
              : dialog.kind === "rename"
                ? "Give it a new name."
                : "Delete this document?"
          }}
        </h2>
        <p v-if="dialog.kind === 'delete'">
          “{{ dialog.document.title }}” will be permanently deleted.
        </p>
        <template v-else
          ><label for="title">Document title</label
          ><input
            id="title"
            v-model="title"
            placeholder="Untitled document"
            maxlength="160"
            required
            autofocus
        /></template>
        <p v-if="dialogError" class="error" role="alert">{{ dialogError }}</p>
        <div class="dialog-actions">
          <button
            type="button"
            class="secondary"
            :disabled="dialogBusy"
            @click="dialog = null"
          >
            Cancel</button
          ><button
            :class="dialog.kind === 'delete' ? 'danger' : 'primary'"
            :disabled="
              dialogBusy || (dialog.kind !== 'delete' && !title.trim())
            "
          >
            {{
              dialogBusy
                ? "One moment…"
                : dialog.kind === "create"
                  ? "Create document ↗"
                  : dialog.kind === "rename"
                    ? "Save name"
                    : "Delete document"
            }}
          </button>
        </div>
      </form>
    </section>
  </div>
</template>
