<script setup>
import { ref } from "vue";
import { useDialogFocus } from "./dialogFocus";
const props = defineProps({ busy: Boolean, labelledby: String });
const emit = defineEmits(["close"]);
const panel = ref(null);
function close() {
  if (!props.busy) emit("close");
}
useDialogFocus(panel, close);
</script>
<template>
  <div class="dialog-backdrop" @click.self="close">
    <section
      ref="panel"
      class="dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="labelledby"
      tabindex="-1"
    >
      <slot />
    </section>
  </div>
</template>
