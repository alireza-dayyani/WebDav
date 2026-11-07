import { ref } from "vue";
import { api, allPages } from "../api";
import { libraryPath } from "../libraryApi";
import { updateFileLimits } from "../fileLimits";

export function useLibrary(documentTemplates) {
  const documents = ref([]);
  const libraryPage = ref(1);
  const libraryMore = ref(false);
  const libraryTotal = ref(0);
  const loadingMore = ref(false);
  let refreshTicket = 0;

  function cancelPendingRefresh() {
    refreshTicket++;
  }

  function clearDocuments() {
    cancelPendingRefresh();
    documents.value = [];
  }

  async function refresh(trashView, search) {
    const ticket = ++refreshTicket;
    const [items, formats, templates] = await Promise.all([
      api(libraryPath(1, trashView, search)),
      api("/documents/formats"),
      allPages("/templates/"),
    ]);
    if (ticket !== refreshTicket) return;
    documents.value = items.items;
    libraryPage.value = 1;
    libraryMore.value = items.hasMore;
    libraryTotal.value = items.total;
    documentTemplates.value = templates.filter((item) => item.data.kind === "document");
    updateFileLimits(formats);
  }

  async function loadMore(trashView, search) {
    if (loadingMore.value) return;
    loadingMore.value = true;
    const ticket = refreshTicket;
    try {
      const result = await api(libraryPath(libraryPage.value + 1, trashView, search));
      if (ticket !== refreshTicket) return;
      documents.value.push(...result.items);
      libraryPage.value++;
      libraryMore.value = result.hasMore;
    } finally {
      loadingMore.value = false;
    }
  }

  return {
    documents,
    libraryPage,
    libraryMore,
    libraryTotal,
    loadingMore,
    refresh,
    loadMore,
    cancelPendingRefresh,
    clearDocuments,
  };
}
