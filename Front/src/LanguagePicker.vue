<script setup>
import { locale, t } from "./i18n";
import { languages } from "./locales";
import { theme } from "./theme";
import AppIcon from "./AppIcon.vue";
</script>
<template>
  <div class="appearance-controls">
    <button
      type="button"
      class="theme-toggle"
      :aria-label="t(theme === 'dark' ? 'lightMode' : 'darkMode')"
      :title="t(theme === 'dark' ? 'lightMode' : 'darkMode')"
      @click="theme = theme === 'dark' ? 'light' : 'dark'"
    >
      <AppIcon :name="theme === 'dark' ? 'sun' : 'moon'" />
    </button>
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
  </div>
</template>
