<script setup>
import { api } from "../api";
const { studio } = defineProps({ studio: { type: Object, required: true } });
const w = studio.workspace;
const T = studio.templates;
const L = studio.letters;
const { busy, task, templates, documents } = w;
const {
  templateForm,
  bookmarkNames,
  mappings,
  sourceChanged,
  editTemplate,
  clearTemplate,
  saveTemplate,
  uploadTemplateSource,
  removeTemplate,
  newLetterFromTemplate,
} = T;
const st = studio.st;
const fields = studio.fields;
const required = studio.required;
const createDocument = (item) => studio.createDocument(item);
const remove = (item) => task(() => removeTemplate(item, studio.confirmDelete));
</script>

<template>
  <section class="studio-card">
    <p class="muted">{{ st("templateHelp") }}</p>
    <form @submit.prevent="task(() => saveTemplate(false))">
      <fieldset :disabled="busy" class="studio-fields">
        <label
          >{{ st("templateName")
          }}<input v-model="templateForm.title" maxlength="160" required /></label
        ><label
          >{{ st("kind")
          }}<select
            v-model="templateForm.kind"
            :aria-label="st('kind')"
            :disabled="!!templateForm.id"
          >
            <option value="document">{{ st("document") }}</option>
            <option value="letter">{{ st("letter") }}</option>
          </select></label
        >
        <label
          v-if="
            templateForm.kind === 'letter' &&
            !templateForm.id &&
            !templateForm.sourceId
          "
        >
          {{ st("paper")
          }}<select v-model="templateForm.paper" :aria-label="st('paper')">
            <option value="">{{ st("paperAuto") }}</option>
            <option value="A5">A5</option>
            <option value="A4">A4</option>
            <option value="Letter">US Letter</option></select
          ><small>{{ st("paperHelp") }}</small>
        </label>
        <label v-if="!templateForm.id"
          >{{ st("source")
          }}<select
            v-model="templateForm.sourceId"
            :aria-label="st('source')"
            @change="task(sourceChanged)"
          >
            <option value="">{{ st("choose") }}</option>
            <option v-for="item in documents" :key="item.id" :value="item.id">
              {{ item.title }} · v{{ item.version }}
            </option>
          </select></label
        >
        <label v-if="!templateForm.id" class="studio-wide studio-upload"
          >{{ st("uploadTemplate")
          }}<input
            type="file"
            accept=".docx,.odt,.rtf,.txt,.md,.json"
            @change="(event) => task(() => uploadTemplateSource(event))"
        /></label>
        <div
          v-if="
            templateForm.kind === 'letter' &&
            (templateForm.sourceId || templateForm.id)
          "
          class="studio-wide"
        >
          <h3>{{ st("mappings") }}</h3>
          <p class="muted">{{ st("mappingHelp") }}</p>
          <div class="studio-fields">
            <label v-for="field in fields" :key="field"
              >{{ st("field" + field) }}
              {{ required.includes(field) ? "*" : ""
              }}<select
                v-model="mappings[field]"
                :aria-label="st('field' + field)"
                :required="required.includes(field)"
              >
                <option value="">{{ st("none") }}</option>
                <option
                  v-for="name in bookmarkNames"
                  :key="name"
                  :value="name"
                >
                  {{ name }}
                </option>
              </select></label
            >
          </div>
        </div>
      </fieldset>
      <div class="studio-actions">
        <button
          class="primary"
          :disabled="busy || (!templateForm.id && !templateForm.sourceId)"
          type="submit"
        >
          {{ st(templateForm.id ? "saveMappings" : "saveTemplate") }}</button
        ><button
          v-if="!templateForm.id"
          class="secondary"
          :disabled="busy || !templateForm.title.trim()"
          type="button"
          @click="task(() => saveTemplate(true))"
        >
          {{ st("starter") }}</button
        ><button
          v-if="templateForm.id"
          type="button"
          :disabled="busy"
          @click="clearTemplate"
        >
          {{ st("cancel") }}
        </button>
      </div>
    </form>
    <div class="studio-template-grid">
      <article
        v-for="item in templates"
        :key="item.metadata.id"
        class="studio-list-item"
      >
        <h3>{{ item.metadata.title }}</h3>
        <p class="muted">
          {{ st(item.data.kind) }} · v{{ item.metadata.version }}
        </p>
        <div class="studio-actions">
          <button
            v-if="item.data.kind === 'document'"
            :disabled="busy"
            @click="task(() => createDocument(item))"
          >
            {{ st("createDocument") }}</button
          ><button
            v-if="item.data.kind === 'letter'"
            :disabled="busy"
            @click="
              task(() => newLetterFromTemplate(item.metadata.id, L.newLetter))
            "
          >
            {{ st("newLetter") }}</button
          ><button :disabled="busy" @click="task(() => editTemplate(item))">
            {{ st("edit") }}</button
          ><button
            @click="
              task(() =>
                api(
                  `/templates/${item.metadata.id}/download?version=${item.metadata.version}`,
                  { download: true },
                ),
              )
            "
          >
            DOCX ↓</button
          ><button @click="remove(item)">
            {{ st("delete") }}
          </button>
        </div>
      </article>
    </div>
    <p v-if="!templates.length" class="muted">{{ st("empty") }}</p>
  </section>
</template>
