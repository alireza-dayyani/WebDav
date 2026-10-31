<script setup>
defineProps({ name: String });
const paths = {
  documents:
    "M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z M14 3v6h6 M8 13h8 M8 17h5",
  template: "M4 4h16v16H4z M4 9h16 M9 9v11",
  mail: "M3 5h18v14H3z M3 5l9 8 9-8",
  bolt: "M13 2L4 14h7l-1 8 10-13h-7z",
  review: "M9 5H5v16h14V5h-4 M9 3h6v4H9z M8 14l3 3 5-6",
  storage: "M4 4h16v6H4z M4 14h16v6H4z M7 7h.01 M7 17h.01",
  trash: "M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7",
  settings: "M4 7h16 M4 17h16 M8 4v6 M16 14v6",
  search: "M21 21l-5-5 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
  sun: "M12 2v2 M12 20v2 M2 12h2 M20 12h2 M5 5l1 1 M18 18l1 1 M5 19l1-1 M18 6l1-1 M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
  moon: "M20 15A9 9 0 0 1 9 4a9 9 0 1 0 11 11",
};
</script>
<template>
  <svg
    class="app-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.6"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path :d="paths[name] || paths.documents" />
  </svg>
</template>
