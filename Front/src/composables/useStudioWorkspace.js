import { ref } from "vue";
import { api, allPages } from "../api.js";
import { errorMessage } from "../i18n.js";

export function useStudioWorkspace({ onIssueError } = {}) {
  const busy = ref(false);
  const error = ref("");
  const notice = ref("");
  const templates = ref([]);
  const senders = ref([]);
  const images = ref([]);
  const letters = ref([]);
  const documents = ref([]);

  async function task(action) {
    if (busy.value) return;
    busy.value = true;
    error.value = "";
    notice.value = "";
    try {
      await action();
    } catch (e) {
      error.value = errorMessage(e);
      onIssueError?.(e);
    } finally {
      busy.value = false;
    }
  }

  async function all(path) {
    return allPages(path);
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

  const reference = (items, id) => {
    const item = items.find((x) => (x.metadata?.id || x.id) === id);
    return item ? { id, version: item.metadata?.version || item.version } : null;
  };

  async function removeItem(item, path, { currentId, onCurrentRemoved } = {}) {
    await api(`${path}/${item.metadata.id}?version=${item.metadata.version}`, {
      method: "DELETE",
    });
    if (currentId && currentId === item.metadata.id) onCurrentRemoved?.();
    await refresh();
  }

  return {
    busy,
    error,
    notice,
    templates,
    senders,
    images,
    letters,
    documents,
    task,
    all,
    refresh,
    reference,
    removeItem,
  };
}
