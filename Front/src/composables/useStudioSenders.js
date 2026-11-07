import { reactive } from "vue";
import { api } from "../api.js";

export function useStudioSenders(workspace, { st }) {
  const senderForm = reactive({
    id: "",
    version: 0,
    fullName: "",
    title: "",
    organization: "",
    address: "",
  });

  async function saveSender() {
    const { id, ...body } = senderForm;
    await api(id ? `/letters/senders/${id}` : "/letters/senders/", {
      method: id ? "PUT" : "POST",
      body,
    });
    Object.assign(senderForm, {
      id: "",
      version: 0,
      fullName: "",
      title: "",
      organization: "",
      address: "",
    });
    await workspace.refresh();
    workspace.notice.value = st("saved");
  }

  function editSender(item) {
    Object.assign(senderForm, item.data, {
      id: item.metadata.id,
      version: item.metadata.version,
    });
  }

  async function removeSender(item, confirmDelete) {
    if (!confirmDelete()) return;
    await workspace.removeItem(item, "/letters/senders");
  }

  return { senderForm, saveSender, editSender, removeSender };
}
