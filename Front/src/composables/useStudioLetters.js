import { computed, reactive, ref, watch } from "vue";
import { api, previewPdf } from "../api.js";
import { t } from "../i18n.js";
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
  const directorySearched = ref(false);
  const attachmentSearch = ref("");

  const form = reactive({
    title: "",
    subject: "",
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
  const attachmentChoices = computed(() => {
    const term = attachmentSearch.value.trim().toLowerCase();
    if (term.length < 2) return [];
    const picked = new Set(form.attachmentIds);
    return workspace.documents.value
      .filter((doc) => !picked.has(doc.id))
      .filter(
        (doc) =>
          doc.title.toLowerCase().includes(term) ||
          String(doc.version).includes(term),
      )
      .slice(0, 40);
  });
  const selectedAttachments = computed(() =>
    form.attachmentIds
      .map((id) => workspace.documents.value.find((d) => d.id === id))
      .filter(Boolean),
  );

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
    now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
    Object.assign(form, {
      title: "",
      subject: "",
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
      recipients: form.recipients.map(
        ({ userId, kind, printOnLetter, displayName, email }) => ({
          userId,
          kind,
          printOnLetter,
          displayName: displayName?.trim() || undefined,
          resolvedEmail: email?.trim() || undefined,
        }),
      ),
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
      recipients: d.recipients.map((x) => {
        const snap = full.snapshot?.recipients?.find((r) => r.userId === x.userId);
        const email = x.resolvedEmail ?? snap?.email ?? "";
        const fallback =
          x.displayName ||
          snap?.name ||
          (email ? email.split("@")[0] : "");
        return {
          ...x,
          email,
          displayName: fallback,
        };
      }),
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

  function defaultDisplayName(user) {
    const name = user.name?.trim();
    if (name && !name.includes("@")) return name;
    return user.email?.split("@")[0] || "";
  }

  function addRecipient(user) {
    if (!form.recipients.some((x) => x.userId === user.id))
      form.recipients.push({
        userId: user.id,
        email: user.email,
        kind: "to",
        printOnLetter: true,
        displayName: defaultDisplayName(user),
      });
  }

  function addAttachment(id) {
    if (form.attachmentIds.length >= 5 || form.attachmentIds.includes(id))
      return;
    form.attachmentIds.push(id);
    attachmentSearch.value = "";
  }

  function removeAttachment(id) {
    form.attachmentIds = form.attachmentIds.filter((x) => x !== id);
  }

  async function findUsers(more = false) {
    if (!more && !directorySearch.value.trim()) {
      workspace.error.value = st("searchUsersRequired");
      return;
    }
    const page = more ? directoryPage.value + 1 : 1;
    const result = await api(
      `/letters/directory?search=${encodeURIComponent(directorySearch.value.trim())}&page=${page}`,
    );
    directory.value = more ? [...directory.value, ...result.items] : result.items;
    directoryPage.value = page;
    directoryMore.value = result.hasMore;
    directorySearched.value = true;
  }

  async function addSelf() {
    const result = await api("/letters/directory");
    const self = result.items[0];
    if (self) addRecipient(self);
  }

  async function letterPreview(targetWindow) {
    const base = `/letters/${current.value.metadata.id}`;
    const body = { version: current.value.metadata.version };
    await previewPdf(base + "/preview", {
      method: "POST",
      body,
      targetWindow,
    });
  }

  function startPreview() {
    if (workspace.busy.value || !current.value) return;
    const targetWindow = window.open("about:blank", "_blank", "noopener,noreferrer");
    if (!targetWindow) {
      workspace.error.value = t("previewBlocked");
      return;
    }
    workspace.task(() => operation("preview", targetWindow));
  }

  async function operation(action, previewWindow = null) {
    try {
      if (!current.value) return;
      if (dirty.value) {
        if (action === "preview" || action === "validate" || action === "generate")
          await saveDraft();
        else {
          workspace.error.value = st("needsSave");
          return;
        }
      }
      const base = `/letters/${current.value.metadata.id}`;
      const body = { version: current.value.metadata.version };
      if (action === "validate") {
        const report = await api(base + "/validate", { method: "POST", body });
        issues.value = report.issues;
        validated.value = report.valid;
      } else if (action === "preview")
        await letterPreview(previewWindow);
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
    } catch (error) {
      previewWindow?.close();
      throw error;
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

  async function uploadAttachment(event) {
    if (form.attachmentIds.length >= 5) {
      workspace.error.value = st("attachmentLimit");
      return;
    }
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    const document = await api("/documents/upload", { method: "POST", body });
    await workspace.refresh();
    if (!form.attachmentIds.includes(document.id))
      form.attachmentIds.push(document.id);
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
    directorySearched,
    attachmentSearch,
    attachmentChoices,
    selectedAttachments,
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
    addSelf,
    addAttachment,
    removeAttachment,
    startPreview,
    operation,
    download,
    duplicate,
    removeLetter,
    uploadBodyDocument,
    uploadAttachment,
  };
}
