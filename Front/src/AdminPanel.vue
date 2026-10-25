<script setup>
import { onMounted, ref } from "vue";
import { api } from "./api";
import { t, language, errorMessage } from "./i18n";
import { updateFileLimits } from "./fileLimits";
import LanguagePicker from "./LanguagePicker.vue";

const emit = defineEmits(["close", "session-changed"]);
const settings = ref(null),
  limit = ref(10),
  saving = ref(false),
  error = ref(""),
  notice = ref("");
const users = ref([]),
  totalUsers = ref(0),
  page = ref(1),
  search = ref(""),
  loading = ref(false),
  roleBusy = ref("");
const selected = ref(null),
  documents = ref([]),
  documentPage = ref(1),
  documentTotal = ref(0),
  preview = ref(null),
  previewBusy = ref(""),
  downloading = ref(false),
  format = ref("docx");
const number = (value) =>
  new Intl.NumberFormat(language.value.tag).format(value);
const size = (bytes) =>
  `${new Intl.NumberFormat(language.value.tag, { maximumFractionDigits: 1 }).format(bytes / 1024 / 1024)} MiB`;

async function loadUsers(number = page.value) {
  loading.value = true;
  error.value = "";
  try {
    const result = await api(
      `/admin/users?page=${number}&search=${encodeURIComponent(search.value)}`,
    );
    users.value = result.items;
    totalUsers.value = result.total;
    page.value = result.page;
  } catch (e) {
    error.value = errorMessage(e, "admin");
  } finally {
    loading.value = false;
  }
}
async function initialize() {
  try {
    settings.value = await api("/admin/settings");
    limit.value = settings.value.maxUploadMiB;
    updateFileLimits(settings.value);
  } catch (e) {
    error.value = errorMessage(e, "admin");
  }
  await loadUsers(1);
}
async function saveLimit() {
  saving.value = true;
  error.value = "";
  notice.value = "";
  try {
    settings.value = await api("/admin/settings", {
      method: "PUT",
      body: { maxUploadMiB: limit.value, version: settings.value.version },
    });
    updateFileLimits(settings.value);
    notice.value = t("limitSaved");
  } catch (e) {
    error.value = errorMessage(e, "admin");
  } finally {
    saving.value = false;
  }
}
async function changeRole(user) {
  if (
    !window.confirm(
      t(user.isAdmin ? "revokeConfirm" : "grantConfirm", { email: user.email }),
    )
  )
    return;
  roleBusy.value = user.id;
  error.value = "";
  try {
    await api(`/admin/users/${encodeURIComponent(user.id)}/admin`, {
      method: "PUT",
      body: { isAdmin: !user.isAdmin, version: user.version },
    });
    await loadUsers();
    emit("session-changed");
  } catch (e) {
    error.value = errorMessage(e, "admin");
  } finally {
    roleBusy.value = "";
  }
}
async function selectUser(user, number = 1) {
  selected.value = user;
  preview.value = null;
  error.value = "";
  try {
    const result = await api(
      `/admin/users/${encodeURIComponent(user.id)}/documents?page=${number}`,
    );
    documents.value = result.items;
    documentPage.value = result.page;
    documentTotal.value = result.total;
  } catch (e) {
    error.value = errorMessage(e, "admin");
  }
}
async function showPreview(document) {
  previewBusy.value = document.id;
  error.value = "";
  try {
    preview.value = await api(
      `/admin/documents/${document.id}/preview?version=${document.version}`,
    );
  } catch (e) {
    error.value = errorMessage(e, "admin");
  } finally {
    previewBusy.value = "";
  }
}
async function download(document) {
  downloading.value = true;
  error.value = "";
  try {
    await api(
      `/admin/documents/${document.id}/download?format=${format.value}&version=${document.version}`,
      { download: true },
    );
  } catch (e) {
    error.value = errorMessage(e, "admin");
  } finally {
    downloading.value = false;
  }
}
onMounted(initialize);
</script>

<template>
  <main class="admin-page">
    <header class="admin-header">
      <div>
        <div class="eyebrow muted">Inkstone</div>
        <h1>{{ t("administration") }}</h1>
      </div>
      <div class="header-tools">
        <LanguagePicker /><button class="secondary" @click="emit('close')">
          {{ t("myDocuments") }}
        </button>
      </div>
    </header>
    <p class="admin-note">{{ t("adminAccessNotice") }}</p>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <p v-if="notice" class="success" role="status">{{ notice }}</p>
    <section class="admin-card">
      <h2>{{ t("uploadSettings") }}</h2>
      <form
        v-if="settings"
        class="admin-limit-form"
        @submit.prevent="saveLimit"
      >
        <div>
          <label for="upload-limit">{{ t("maxUpload") }} (MiB)</label
          ><input
            id="upload-limit"
            v-model.number="limit"
            type="number"
            min="1"
            :max="settings.maxConfigurableUploadMiB"
            step="1"
            required
            dir="ltr"
          />
        </div>
        <button class="primary" :disabled="saving">
          {{ t(saving ? "saving" : "saveSettings") }}
        </button>
      </form>
      <p class="field-help">
        {{
          t("uploadLimitHelp", {
            max: number(settings?.maxConfigurableUploadMiB || 128),
          })
        }}
      </p>
    </section>
    <section class="admin-card">
      <div class="admin-section-header">
        <h2>
          {{ t("users") }} <span class="muted">{{ number(totalUsers) }}</span>
        </h2>
        <form class="admin-search" @submit.prevent="loadUsers(1)">
          <input
            v-model="search"
            type="search"
            :placeholder="t('searchUsers')"
            :aria-label="t('searchUsers')"
            maxlength="254"
          /><button class="secondary" :disabled="loading">
            {{ t("searchAction") }}
          </button>
        </form>
      </div>
      <div class="table-scroll">
        <table class="admin-table">
          <thead>
            <tr>
              <th>{{ t("email") }}</th>
              <th>{{ t("role") }}</th>
              <th>{{ t("files") }}</th>
              <th>{{ t("storage") }}</th>
              <th>{{ t("actions") }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id">
              <td dir="ltr">{{ user.email }}</td>
              <td>
                {{
                  t(
                    user.isSuperAdmin
                      ? "superAdministrator"
                      : user.isAdmin
                        ? "administrator"
                        : "member",
                  )
                }}
              </td>
              <td>{{ number(user.documentCount) }}</td>
              <td dir="ltr">{{ size(user.storedBytes) }}</td>
              <td>
                <div class="admin-actions">
                  <button class="text-button" @click="selectUser(user)">
                    {{ t("viewFiles") }}</button
                  ><button
                    v-if="!user.isSuperAdmin"
                    class="text-button"
                    :disabled="roleBusy === user.id"
                    @click="changeRole(user)"
                  >
                    {{ t(user.isAdmin ? "revokeAdmin" : "makeAdmin") }}</button
                  ><span v-else class="muted">{{ t("protectedAccount") }}</span>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="!loading && !users.length" class="muted">{{ t("noUsers") }}</p>
      <div class="pagination">
        <button
          class="secondary"
          :disabled="page === 1 || loading"
          @click="loadUsers(page - 1)"
        >
          {{ t("previous") }}</button
        ><span>{{ number(page) }}</span
        ><button
          class="secondary"
          :disabled="page * 25 >= totalUsers || loading"
          @click="loadUsers(page + 1)"
        >
          {{ t("next") }}
        </button>
      </div>
    </section>
    <section v-if="selected" class="admin-card">
      <div class="admin-section-header">
        <div>
          <h2>{{ t("userFiles") }}</h2>
          <bdi dir="ltr">{{ selected.email }}</bdi>
        </div>
        <select v-model="format" :aria-label="t('format')" dir="ltr">
          <option
            v-for="value in ['docx', 'pdf', 'txt', 'md', 'json']"
            :key="value"
            :value="value"
          >
            {{ value.toUpperCase() }}
          </option>
        </select>
      </div>
      <p class="field-help">{{ t("readOnlyAccess") }}</p>
      <div class="table-scroll">
        <table class="admin-table">
          <thead>
            <tr>
              <th>{{ t("title") }}</th>
              <th>{{ t("storage") }}</th>
              <th>{{ t("actions") }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="document in documents" :key="document.id">
              <td dir="auto">{{ document.title }}</td>
              <td dir="ltr">{{ size(document.size) }}</td>
              <td>
                <div class="admin-actions">
                  <button
                    class="text-button"
                    :disabled="previewBusy === document.id"
                    @click="showPreview(document)"
                  >
                    {{ t("preview") }}</button
                  ><button
                    class="text-button"
                    :disabled="downloading"
                    @click="download(document)"
                  >
                    {{ t("download") }}
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="!documents.length" class="muted">{{ t("noFiles") }}</p>
      <div class="pagination">
        <button
          class="secondary"
          :disabled="documentPage === 1"
          @click="selectUser(selected, documentPage - 1)"
        >
          {{ t("previous") }}</button
        ><span>{{ number(documentPage) }}</span
        ><button
          class="secondary"
          :disabled="documentPage * 50 >= documentTotal"
          @click="selectUser(selected, documentPage + 1)"
        >
          {{ t("next") }}
        </button>
      </div>
      <div v-if="preview" class="admin-preview">
        <h3 dir="auto">{{ preview.title }}</h3>
        <p v-if="preview.truncated" class="field-help">
          {{ t("previewTruncated") }}
        </p>
        <pre dir="auto">{{ preview.text || t("emptyDocument") }}</pre>
      </div>
    </section>
  </main>
</template>
