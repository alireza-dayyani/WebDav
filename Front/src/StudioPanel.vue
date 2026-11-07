<script setup>
import LanguagePicker from "./LanguagePicker.vue";
import StudioLettersTab from "./studio/StudioLettersTab.vue";
import StudioTemplatesTab from "./studio/StudioTemplatesTab.vue";
import StudioSendersTab from "./studio/StudioSendersTab.vue";
import StudioImagesTab from "./studio/StudioImagesTab.vue";
import { useStudioPanel } from "./composables/useStudioPanel.js";

const props = defineProps({
  embedded: Boolean,
  initialTab: { type: String, default: "letters" },
  initialLetterId: { type: String, default: "" },
});
const emit = defineEmits([
  "close",
  "open-document",
  "open-letter-editor",
  "tab-change",
]);

const studio = useStudioPanel(props, emit);
const { st, tab, navigate, close, canLeave, resumeLetter } = studio;
const { busy, error, notice, task } = studio.workspace;

defineExpose({ canLeave, resumeLetter });
</script>

<template>
  <section class="studio-page" :class="{ embedded }">
    <header class="studio-header">
      <div>
        <button
          v-if="!embedded"
          class="text-button"
          :disabled="busy"
          @click="close"
        >
          ← {{ st("back") }}
        </button>
        <h1>{{ st(tab) }}</h1>
      </div>
      <div class="header-tools">
        <button
          class="secondary"
          :disabled="busy"
          @click="task(() => studio.workspace.refresh())"
        >
          {{ st("refresh") }}</button
        ><LanguagePicker v-if="!embedded" />
      </div>
    </header>
    <nav class="studio-tabs" :aria-label="st('letters')">
      <button
        v-for="name in ['letters', 'templates', 'senders', 'images']"
        :key="name"
        :class="{ selected: tab === name }"
        :disabled="busy"
        @click="navigate(name)"
      >
        {{ st(name) }}
      </button>
    </nav>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <p v-if="notice" class="success" role="status">{{ notice }}</p>
    <p v-if="busy" role="status">{{ st("busy") }}</p>

    <StudioLettersTab v-if="tab === 'letters'" :studio="studio" />
    <StudioTemplatesTab v-else-if="tab === 'templates'" :studio="studio" />
    <StudioSendersTab v-else-if="tab === 'senders'" :studio="studio" />
    <StudioImagesTab v-else :studio="studio" />
  </section>
</template>
