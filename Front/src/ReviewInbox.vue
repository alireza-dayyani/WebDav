<script setup>
import { onMounted, ref } from "vue";
import { api, previewPdf } from "./api";
import { t, errorMessage } from "./i18n";
import { useDialogFocus } from "./dialogFocus";
const emit = defineEmits(["close"]),
  panel = ref(null),
  items = ref([]),
  busy = ref(false),
  error = ref(""),
  page = ref(0),
  more = ref(true),
  notes = ref({});
useDialogFocus(panel, () => {
  if (!busy.value) emit("close");
});
async function load(reset = false) {
  if (reset) {
    items.value = [];
    page.value = 0;
  }
  const result = await api(`/reviews/?page=${page.value + 1}`);
  items.value.push(...result.items);
  page.value++;
  more.value = result.hasMore;
}
async function task(action) {
  if (busy.value) return;
  busy.value = true;
  error.value = "";
  try {
    await action();
  } catch (e) {
    error.value = errorMessage(e);
  } finally {
    busy.value = false;
  }
}
async function decide(item, decision) {
  await api(`/reviews/${item.id}/decision`, {
    method: "POST",
    body: {
      version: item.version,
      documentVersion: item.currentVersion,
      decision,
      note: notes.value[item.id] ?? "",
    },
  });
  delete notes.value[item.id];
  await load(true);
}
onMounted(() => task(() => load(true)));
</script>
<template>
  <div class="dialog-backdrop">
    <section
      ref="panel"
      tabindex="-1"
      class="dialog history-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="reviews-title"
    >
      <h2 id="reviews-title">{{ t("ws.reviews") }}</h2>
      <p>{{ t("ws.reviewHelp") }}</p>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <article v-for="item in items" :key="item.id" class="workspace-record">
        <h3 dir="auto">
          {{ item.title }} · {{ t("version") }} {{ item.documentVersion }}
        </h3>
        <p>
          {{ t("ws.status_" + item.status) }}
          <strong v-if="item.currentVersion !== item.documentVersion"
            >· {{ t("ws.stale") }}</strong
          >
        </p>
        <p v-if="item.note" dir="auto">{{ item.note }}</p>
        <button
          :disabled="busy"
          @click="
            task(() =>
              previewPdf(
                `/reviews/${item.id}/download?version=${item.version}`,
              ),
            )
          "
        >
          {{ t("ws.reviewEvidence") }}
        </button>
        <template
          v-if="
            item.status === 'pending' &&
            item.canDecide &&
            item.currentVersion === item.documentVersion
          "
          ><label
            >{{ t("ws.reviewNote")
            }}<textarea
              v-model="notes[item.id]"
              maxlength="2000"
            ></textarea></label
          ><button
            :disabled="busy"
            @click="task(() => decide(item, 'approved'))"
          >
            {{ t("ws.approve") }}</button
          ><button
            :disabled="busy"
            @click="task(() => decide(item, 'returned'))"
          >
            {{ t("ws.return") }}
          </button></template
        >
        <button
          v-if="item.status === 'pending' && item.canCancel"
          :disabled="busy"
          @click="task(() => decide(item, 'cancelled'))"
        >
          {{ t("ws.cancelReview") }}
        </button>
      </article>
      <p v-if="!items.length">{{ t("ws.noReviews") }}</p>
      <button v-if="more" :disabled="busy" @click="task(() => load())">
        {{ t("loadMore") }}
      </button>
      <button :disabled="busy" @click="task(() => load(true))">
        {{ t("ws.refresh") }}
      </button>
      <button :disabled="busy" @click="emit('close')">{{ t("close") }}</button>
    </section>
  </div>
</template>
