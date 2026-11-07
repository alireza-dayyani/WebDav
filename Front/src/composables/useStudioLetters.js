import { computed, reactive, ref, watch } from "vue";
import { api, previewPdf } from "../api.js";
import { language, t } from "../i18n.js";
import { STUDIO_BOOKMARK_ALIASES as aliases } from "./studioConstants.js";

export function useStudioLetters(workspace, emit, { st, navigate, fields, required }) {
  const current = ref(null);
  const artifacts = ref([]);
  const issues = ref([]);
  const validated = ref(false);
  const dirty = ref(false);
  const directory = ref([]);
  const directoryPage = ref(1);
  const directoryMore = ref(false);
  const directorySearch = ref("");

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

  const letterTemplates = computed(() =>
    workspace.templates.value.filter((x) => x.data.kind === "letter"),
  );
  const generated = computed(() => current.value?.metadata.state === "generated");

  watch(
    form,
    () => {
      dirty.value = true;
      validated.value = false;
    },
    { deep: true, flush: "sync" },
  );

  function resetLetterEditor() {
    current.value = null;
    artifacts.value = [];
    issues.value = [];
    validated.value = false;
  }

  function newLetter(templateId = "") {
    if (dirty.value && !window.confirm(t("leave"))) return;
    resetLetterEditor();
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
      senderId: workspace.senders.value[0]?.metadata.id || "",
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

  function draft() {
    return {
      title: form.title,
      subject: form.subject,
      language: form.language,
      date: form.date,
      template: workspace.reference(workspace.templates.value, form.templateId),
      sender: workspace.reference(workspace.senders.value, form.senderId),
      signature: workspace.reference(workspace.images.value, form.signatureId),
      logo: workspace.reference(workspace.images.value, form.logoId),
      bodyDocument: current.value?.metadata.hasEditor
        ? current.value.data.bodyDocument
        : workspace.reference(workspace.documents.value, form.bodyId),
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
        .map((id) => workspace.reference(workspace.documents.value, id))
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
    workspace.notice.value = st("saved");
    await workspace.refresh();
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

  async function findUsers(more = false) {
    const page = more ? directoryPage.value + 1 : 1;
    const result = await api(
      `/letters/directory?search=${encodeURIComponent(directorySearch.value)}&page=${page}`,
    );
    directory.value = more ? [...directory.value, ...result.items] : result.items;
    directoryPage.value = page;
    directoryMore.value = result.hasMore;
  }

  async function operation(action) {
    if (!current.value || dirty.value) {
      workspace.error.value = st("needsSave");
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
      await workspace.refresh();
    }
  }

  async function download(artifact, format) {
    await api(
      `/letters/${current.value.metadata.id}/artifacts/${artifact.id}/download?version=${current.value.metadata.version}&format=${format}`,
      { download: true },
    );
  }

  async function duplicate(item) {
    if (dirty.value && !window.confirm(t("leave"))) return;
    const name = window.prompt(st("documentName"), item.metadata.title);
    if (!name) return;
    const copy = await api(`/letters/${item.metadata.id}/duplicate`, {
      method: "POST",
      body: { version: item.metadata.version, title: name },
    });
    dirty.value = false;
    await workspace.refresh();
    await openLetter(copy);
  }

  async function removeLetter(item) {
    if (!window.confirm(st("deleteConfirm"))) return;
    await workspace.removeItem(item, "/letters", {
      currentId: current.value?.metadata.id,
      onCurrentRemoved: () => {
        dirty.value = false;
        newLetter();
      },
    });
  }

  async function uploadBodyDocument(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    const document = await api("/documents/upload", { method: "POST", body });
    await workspace.refresh();
    form.bodyId = document.id;
    form.bodyText = "";
  }

  return {
    aliases,
    form,
    current,
    artifacts,
    issues,
    validated,
    dirty,
    directory,
    directoryMore,
    directorySearch,
    letterTemplates,
    generated,
    fields,
    required,
    newLetter,
    saveDraft,
    openLetter,
    writeLetter,
    addRecipient,
    findUsers,
    operation,
    download,
    duplicate,
    removeLetter,
    uploadBodyDocument,
  };
}
