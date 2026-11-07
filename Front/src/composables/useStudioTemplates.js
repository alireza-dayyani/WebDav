import { reactive, ref } from "vue";
import { api } from "../api.js";
import { language } from "../i18n.js";
import { STUDIO_BOOKMARK_ALIASES as aliases } from "./studioConstants.js";

export function useStudioTemplates(workspace, emit, { st, fields, required }) {
  const bookmarkNames = ref([]);
  const mappings = reactive({});

  const templateForm = reactive({
    id: "",
    version: 0,
    title: "",
    kind: "document",
    language: language.value.tag,
    sourceId: "",
    paper: "",
  });

  async function sourceChanged() {
    const source = workspace.documents.value.find(
      (x) => x.id === templateForm.sourceId,
    );
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
    for (const key of fields.value)
      mappings[key] = item.data.fields[key] || "";
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
        body: {
          ...config,
          source: workspace.reference(workspace.documents.value, config.sourceId),
        },
      });
    clearTemplate();
    await workspace.refresh();
    workspace.notice.value = st("saved");
  }

  async function createDocument(item, dirty, confirmLeave) {
    if (dirty && !confirmLeave()) return;
    const title = window.prompt(st("documentName"), item.metadata.title);
    if (!title) return;
    const document = await api(`/templates/${item.metadata.id}/render`, {
      method: "POST",
      body: { title, version: item.metadata.version, values: {} },
    });
    emit("open-document", document);
  }

  async function uploadTemplateSource(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    const document = await api("/documents/upload", { method: "POST", body });
    await workspace.refresh();
    templateForm.sourceId = document.id;
    await sourceChanged();
  }

  async function removeTemplate(item, confirmDelete) {
    if (!confirmDelete()) return;
    await workspace.removeItem(item, "/templates");
  }

  function newLetterFromTemplate(templateId, newLetter) {
    newLetter(templateId);
  }

  return {
    templateForm,
    bookmarkNames,
    mappings,
    sourceChanged,
    editTemplate,
    clearTemplate,
    saveTemplate,
    createDocument,
    uploadTemplateSource,
    removeTemplate,
    newLetterFromTemplate,
  };
}
