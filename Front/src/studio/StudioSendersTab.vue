<script setup>
const { studio } = defineProps({ studio: { type: Object, required: true } });
const w = studio.workspace;
const S = studio.senders;
const { busy, task, senders } = w;
const { senderForm, saveSender, editSender, removeSender } = S;
const st = studio.st;
const remove = (item) => task(() => removeSender(item, studio.confirmDelete));
</script>

<template>
  <section class="studio-card">
    <form @submit.prevent="task(saveSender)">
      <fieldset :disabled="busy" class="studio-fields">
        <label
          >{{ st("fullName")
          }}<input v-model="senderForm.fullName" maxlength="160" required /></label
        ><label
          >{{ st("senderTitle")
          }}<input v-model="senderForm.title" maxlength="160" required /></label
        ><label
          >{{ st("organization")
          }}<input v-model="senderForm.organization" maxlength="500" /></label
        ><label
          >{{ st("address")
          }}<textarea
            v-model="senderForm.address"
            maxlength="500"
            rows="3"
          ></textarea>
        </label>
      </fieldset>
      <button class="primary" :disabled="busy">{{ st("saveSender") }}</button>
    </form>
    <div class="studio-template-grid">
      <article
        v-for="item in senders"
        :key="item.metadata.id"
        class="studio-list-item"
      >
        <h3>{{ item.data.fullName }}</h3>
        <p>{{ item.data.title }}</p>
        <p class="muted">{{ item.data.organization }}</p>
        <div class="studio-actions">
          <button :disabled="busy" @click="editSender(item)">
            {{ st("edit") }}</button
          ><button @click="remove(item)">
            {{ st("delete") }}
          </button>
        </div>
      </article>
    </div>
  </section>
</template>
