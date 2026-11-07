<script setup>
const { studio } = defineProps({ studio: { type: Object, required: true } });
const w = studio.workspace;
const I = studio.images;
const { busy, task, images } = w;
const { imageTitle, imageFile, imageInput, uploadImage, removeImage } = I;
const st = studio.st;
const remove = (item) => task(() => removeImage(item, studio.confirmDelete));
</script>

<template>
  <section class="studio-card">
    <p class="muted">{{ st("imageHelp") }}</p>
    <form @submit.prevent="task(uploadImage)">
      <fieldset :disabled="busy" class="studio-fields">
        <label
          >{{ st("imageTitle")
          }}<input v-model="imageTitle" maxlength="160" required /></label
        ><label
          >{{ st("imageFile")
          }}<input
            ref="imageInput"
            type="file"
            accept="image/png,image/jpeg"
            required
            @change="imageFile = $event.target.files?.[0] || null"
        /></label>
      </fieldset>
      <button class="primary" :disabled="busy || !imageFile">
        {{ st("uploadImage") }}
      </button>
    </form>
    <div class="studio-template-grid">
      <article
        v-for="item in images"
        :key="item.metadata.id"
        class="studio-list-item"
      >
        <img
          class="studio-image"
          :src="`/api/letters/images/${item.metadata.id}?version=${item.metadata.version}`"
          :alt="item.metadata.title"
        />
        <h3>{{ item.metadata.title }}</h3>
        <button @click="remove(item)">
          {{ st("delete") }}
        </button>
      </article>
    </div>
  </section>
</template>
