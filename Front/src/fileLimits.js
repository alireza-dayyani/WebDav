import { ref } from "vue";

export const maxUploadBytes = ref(10 * 1024 * 1024);
export const maxDocumentBytes = ref(256 * 1024 * 1024);
export function updateFileLimits(value) {
  if (Number.isFinite(value.maxUploadBytes))
    maxUploadBytes.value = value.maxUploadBytes;
  if (Number.isFinite(value.maxDocumentBytes))
    maxDocumentBytes.value = value.maxDocumentBytes;
}
