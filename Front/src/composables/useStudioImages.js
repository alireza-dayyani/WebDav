import { ref } from "vue";
import { api } from "../api.js";

export function useStudioImages(workspace, { st }) {
  const imageTitle = ref("");
  const imageFile = ref(null);
  const imageInput = ref(null);

  async function uploadImage() {
    const body = new FormData();
    body.append("title", imageTitle.value);
    body.append("file", imageFile.value);
    await api("/letters/images/", { method: "POST", body });
    imageTitle.value = "";
    imageFile.value = null;
    if (imageInput.value) imageInput.value.value = "";
    await workspace.refresh();
    workspace.notice.value = st("saved");
  }

  async function removeImage(item, confirmDelete) {
    if (!confirmDelete()) return;
    await workspace.removeItem(item, "/letters/images");
  }

  return { imageTitle, imageFile, imageInput, uploadImage, removeImage };
}
