<script setup>
import { locale, t } from "./i18n";
import { languages } from "./locales";
</script>
<template>
  <select
    v-model="locale"
    class="language-picker"
    :aria-label="t('language')"
    dir="auto"
  >
    <option
      v-for="language in languages"
      :key="language.code"
      :value="language.code"
      :lang="language.code"
    >
      {{ language.name }}
    </option>
  </select>
</template>
