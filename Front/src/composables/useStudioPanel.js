import { onMounted, ref, watch } from "vue";
import { api } from "../api.js";
import { locale, t } from "../i18n.js";
import { useUnsavedChanges } from "../unsavedChanges.js";
import { useStudioWorkspace } from "./useStudioWorkspace.js";
import { useStudioLetters } from "./useStudioLetters.js";
import { useStudioTemplates } from "./useStudioTemplates.js";
import { useStudioSenders } from "./useStudioSenders.js";
import { useStudioImages } from "./useStudioImages.js";

export function useStudioPanel(props, emit) {
  const tab = ref(props.initialTab);
  const st = (key) => t("studio." + key);
  const fields = ref([]);
  const required = ref([]);

  let letters;
  const workspace = useStudioWorkspace({
    onIssueError: (e) => {
      if (e.issues && letters) letters.issues.value = e.issues;
    },
  });

  function navigate(value) {
    tab.value = value;
    emit("tab-change", value);
    history.replaceState(
      null,
      "",
      value === "templates" ? "/#/templates" : "/#/letters",
    );
  }

  letters = useStudioLetters(workspace, emit, {
    st,
    navigate,
    fields,
    required,
  });

  const templates = useStudioTemplates(workspace, emit, {
    st,
    fields,
    required,
  });
  const senders = useStudioSenders(workspace, { st });
  const images = useStudioImages(workspace, { st });

  useUnsavedChanges(() => letters.dirty.value);

  watch(locale, () => {
    workspace.notice.value = "";
    workspace.error.value = "";
  });
  watch(
    () => props.initialTab,
    (value) => {
      tab.value = value;
    },
  );

  function confirmDelete() {
    return window.confirm(st("deleteConfirm"));
  }
  function confirmLeave() {
    return window.confirm(t("leave"));
  }

  function canLeave() {
    return !workspace.busy.value && (!letters.dirty.value || confirmLeave());
  }

  function resumeLetter(letter) {
    return workspace.task(async () => {
      await workspace.refresh();
      await letters.openLetter({ metadata: letter });
    });
  }

  function close() {
    if (workspace.busy.value) return;
    if (!letters.dirty.value || confirmLeave()) emit("close");
  }

  async function duplicate(item) {
    return letters.duplicate(item);
  }

  async function createDocument(item) {
    return templates.createDocument(item, letters.dirty.value, confirmLeave);
  }

  onMounted(() =>
    workspace.task(async () => {
      await workspace.refresh();
      const info = await api("/templates/fields");
      fields.value = info.fields;
      required.value = info.required;
      await letters.findUsers();
      const chosen = workspace.letters.value.find(
        (x) => x.metadata.id === props.initialLetterId,
      );
      if (chosen) await letters.openLetter(chosen);
      else letters.newLetter();
      navigate(props.initialTab);
    }),
  );

  return {
    st,
    tab,
    navigate,
    close,
    canLeave,
    resumeLetter,
    workspace,
    letters,
    templates,
    senders,
    images,
    fields,
    required,
    confirmDelete,
    duplicate,
    createDocument,
  };
}
