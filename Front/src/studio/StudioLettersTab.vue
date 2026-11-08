<script setup>
import { t } from "../i18n";
const { studio } = defineProps({ studio: { type: Object, required: true } });
const w = studio.workspace;
const L = studio.letters;
const {
  busy,
  task,
  senders,
  images,
  documents,
  letters,
} = w;
const {
  form,
  current,
  artifacts,
  issues,
  validated,
  dirty,
  generated,
  letterTemplates,
  directory,
  directoryMore,
  directorySearch,
  aliases,
  newLetter,
  saveDraft,
  openLetter,
  writeLetter,
  addRecipient,
  findUsers,
  operation,
  download,
  removeLetter,
  uploadBodyDocument,
} = L;
const st = studio.st;
const fields = studio.fields;
const duplicate = (item) => studio.duplicate(item);
const remove = (item) => task(() => removeLetter(item));
</script>

<template>
  <div class="studio-layout">
    <aside class="studio-card studio-library">
      <div class="section-heading">
        <h2>{{ st("savedLetters") }}</h2>
        <button class="secondary" :disabled="busy" @click="newLetter()">
          ＋ {{ st("newLetter") }}
        </button>
      </div>
      <p v-if="!letters.length" class="muted">{{ st("empty") }}</p>
      <article
        v-for="item in letters"
        :key="item.metadata.id"
        class="studio-list-item"
      >
        <button
          class="text-button"
          :disabled="busy"
          @click="task(() => openLetter(item))"
        >
          {{ item.metadata.title }}</button
        ><small
          >{{ st(item.metadata.state) }} · v{{ item.metadata.version }}</small
        >
        <div class="studio-actions">
          <button :disabled="busy" @click="task(() => duplicate(item))">
            {{ st("duplicate") }}</button
          ><button :disabled="busy" @click="remove(item)">
            {{ st("delete") }}
          </button>
        </div>
      </article>
    </aside>
    <section class="studio-card">
      <h2>{{ current ? current.metadata.title : st("newLetter") }}</h2>
      <p class="muted">{{ st("intro") }}</p>
      <p class="studio-note">{{ st("privateNote") }}</p>
      <p v-if="generated" class="success">{{ st("generatedNote") }}</p>
      <section v-if="generated" class="studio-frozen">
        <h3>{{ current.snapshot?.draft.subject }}</h3>
        <p>
          {{ st("number") }}:
          <span dir="ltr">{{ current.snapshot?.number }}</span>
        </p>
        <p>
          {{ st("date") }}:
          <span dir="ltr">{{ current.snapshot?.draft.date }}</span>
        </p>
        <p>
          {{ st("sender") }}:
          {{ current.snapshot?.sender.profile.fullName }} —
          {{ current.snapshot?.sender.profile.title }}
        </p>
        <p>{{ current.snapshot?.sender.profile.organization }}</p>
        <p>
          {{ st("template") }}:
          {{ current.snapshot?.template.title || st("template") }} · v{{
            current.snapshot?.template.version
          }}
        </p>
        <h3>{{ st("recipients") }}</h3>
        <ul>
          <li
            v-for="person in current.snapshot?.recipients"
            :key="person.userId"
          >
            <span dir="ltr">{{ person.email }}</span> — {{ st(person.kind) }}
          </li>
        </ul>
        <button
          class="secondary"
          :disabled="busy"
          @click="task(() => operation('preview'))"
        >
          {{ st("preview") }}
        </button>
      </section>
      <form v-else @submit.prevent="task(saveDraft)">
        <fieldset :disabled="busy || generated" class="studio-fields">
          <label
            >{{ st("title")
            }}<input v-model="form.title" maxlength="160" required
          /></label>
          <label
            >{{ st("subject") }}<input v-model="form.subject" maxlength="200"
          /></label>
          <label
            >{{ st("date") }}<input v-model="form.date" type="date"
          /></label>
          <label
            >{{ st("template")
            }}<select
              v-model="form.templateId"
              :disabled="current?.metadata.hasEditor"
              :aria-label="st('template')"
            >
              <option value="">{{ st("choose") }}</option>
              <option
                v-for="item in letterTemplates"
                :key="item.metadata.id"
                :value="item.metadata.id"
              >
                {{ item.metadata.title }} · v{{ item.metadata.version }}
              </option>
            </select></label
          >
          <label
            >{{ st("sender")
            }}<select v-model="form.senderId" :aria-label="st('sender')">
              <option value="">{{ st("choose") }}</option>
              <option
                v-for="item in senders"
                :key="item.metadata.id"
                :value="item.metadata.id"
              >
                {{ item.data.fullName }} — {{ item.data.title }}
              </option>
            </select></label
          >
          <label
            >{{ st("signature")
            }}<select v-model="form.signatureId" :aria-label="st('signature')">
              <option value="">{{ st("choose") }}</option>
              <option
                v-for="item in images"
                :key="item.metadata.id"
                :value="item.metadata.id"
              >
                {{ item.metadata.title }}
              </option>
            </select></label
          >
          <label
            >{{ st("logo")
            }}<select v-model="form.logoId" :aria-label="st('logo')">
              <option value="">{{ st("none") }}</option>
              <option
                v-for="item in images"
                :key="item.metadata.id"
                :value="item.metadata.id"
              >
                {{ item.metadata.title }}
              </option>
            </select></label
          >
          <label
            >{{ st("number")
            }}<input
              v-model="form.number"
              maxlength="80"
              :placeholder="st('automatic')"
          /></label>
          <label
            >{{ st("priority")
            }}<select v-model="form.priority" :aria-label="st('priority')">
              <option
                v-for="value in ['normal', 'urgent', 'immediate']"
                :key="value"
                :value="value"
              >
                {{ st(value) }}
              </option>
            </select></label
          >
          <label
            >{{ st("classification")
            }}<select
              v-model="form.classification"
              :aria-label="st('classification')"
            >
              <option
                v-for="value in ['normal', 'confidential', 'strict']"
                :key="value"
                :value="value"
              >
                {{ st(value) }}
              </option>
            </select></label
          >
          <div class="studio-wide">
            <h3>{{ st("body") }}</h3>
            <p class="muted">
              {{
                st(
                  current?.metadata.hasEditor ? "editorBodySaved" : "writeHelp",
                )
              }}
            </p>
            <button
              type="button"
              class="primary"
              :disabled="busy || !form.templateId"
              @click="task(writeLetter)"
            >
              {{
                st(
                  current?.metadata.hasEditor
                    ? "continueWriting"
                    : "writeLetter",
                )
              }}
            </button>
            <label v-if="!current?.metadata.hasEditor"
              >{{ st("bodyDocument")
              }}<select v-model="form.bodyId" :aria-label="st('bodyDocument')">
                <option value="">{{ st("bodyText") }}</option>
                <option
                  v-for="item in documents"
                  :key="item.id"
                  :value="item.id"
                >
                  {{ item.title }} · v{{ item.version }}
                </option>
              </select></label
            >
            <label v-if="!current?.metadata.hasEditor && !form.bodyId"
              >{{ st("bodyText")
              }}<textarea v-model="form.bodyText" rows="9" dir="auto"></textarea>
            </label>
            <label v-if="!current?.metadata.hasEditor" class="studio-upload"
              >{{ st("uploadBody")
              }}<input
                type="file"
                accept=".docx,.odt,.rtf,.txt,.md,.json"
                @change="(event) => task(() => uploadBodyDocument(event))"
            /></label>
          </div>
          <div class="studio-wide">
            <h3>{{ st("recipients") }}</h3>
            <div class="studio-search">
              <input
                v-model="directorySearch"
                type="search"
                :placeholder="st('searchUsers')"
                :aria-label="st('searchUsers')"
                @keydown.enter.prevent="task(() => findUsers())"
              /><button
                type="button"
                class="secondary"
                @click="task(() => findUsers())"
              >
                {{ st("search") }}
              </button>
            </div>
            <div class="studio-directory">
              <button
                v-for="person in directory"
                :key="person.id"
                type="button"
                :disabled="form.recipients.some((x) => x.userId === person.id)"
                :aria-label="st('selectRecipient') + ': ' + person.email"
                @click="addRecipient(person)"
              >
                <span dir="ltr">{{ person.email }}</span> ＋</button
              ><button
                v-if="directoryMore"
                type="button"
                @click="task(() => findUsers(true))"
              >
                {{ st("more") }}
              </button>
            </div>
            <div
              v-for="person in form.recipients"
              :key="person.userId"
              class="studio-recipient"
            >
              <span dir="ltr">{{ person.label }}</span
              ><select v-model="person.kind" :aria-label="st('recipients')">
                <option value="to">{{ st("to") }}</option>
                <option value="cc">{{ st("cc") }}</option></select
              ><label v-if="person.kind === 'cc'" class="studio-check"
                ><input v-model="person.printOnLetter" type="checkbox" />{{
                  st("printCopy")
                }}</label
              ><button
                type="button"
                @click="
                  form.recipients = form.recipients.filter(
                    (x) => x.userId !== person.userId,
                  )
                "
              >
                {{ st("remove") }}
              </button>
            </div>
          </div>
          <div class="studio-wide">
            <h3>{{ st("attachments") }}</h3>
            <p class="muted">{{ st("attachmentHelp") }}</p>
            <div class="studio-directory">
              <label
                v-for="item in documents"
                :key="item.id"
                class="studio-check"
                ><input
                  v-model="form.attachmentIds"
                  type="checkbox"
                  :value="item.id"
                />{{ item.title }}</label
              >
            </div>
          </div>
          <label class="studio-check studio-wide"
            ><input v-model="form.includeQrCode" type="checkbox" />{{
              st("qr")
            }}</label
          ><label class="studio-check studio-wide"
            ><input v-model="form.copyVariants" type="checkbox" />{{
              st("variants")
            }}</label
          >
        </fieldset>
        <div class="studio-actions">
          <button
            v-if="!generated"
            class="primary"
            :disabled="busy"
            type="submit"
          >
            {{ st("saveDraft") }}</button
          ><button
            v-if="!generated"
            class="secondary"
            :disabled="busy || !current || dirty"
            type="button"
            @click="task(() => operation('validate'))"
          >
            {{ st("validate") }}</button
          ><button
            class="secondary"
            :disabled="busy || !current || dirty"
            type="button"
            @click="task(() => operation('preview'))"
          >
            {{ st("preview") }}</button
          ><button
            v-if="!generated"
            class="primary"
            :disabled="busy || !current || dirty"
            type="button"
            @click="task(() => operation('generate'))"
          >
            {{ st("generate") }}
          </button>
        </div>
      </form>
      <p v-if="dirty" class="muted">{{ st("needsSave") }}</p>
      <p v-if="validated && !generated" class="success" role="status">
        {{ st("valid") }}
      </p>
      <ul v-if="issues.length" class="studio-issues" role="alert">
        <li v-for="(issue, index) in issues" :key="index">
          <strong
            >{{
              st(
                issue.field in aliases || fields.includes(issue.field)
                  ? "field" + issue.field
                  : issue.field,
              )
            }}:</strong
          >
          {{ st(issue.code === "date" ? "dateIssue" : issue.code) }}
        </li>
      </ul>
      <section v-if="artifacts.length">
        <h3>{{ st("outputs") }}</h3>
        <div v-for="file in artifacts" :key="file.id" class="studio-list-item">
          <strong>{{ file.title }}</strong>
          <div class="studio-actions">
            <button
              v-for="format in ['docx', 'pdf', 'json']"
              :key="format"
              :disabled="busy"
              :aria-label="`${t('download')} ${format.toUpperCase()}: ${file.title}`"
              @click="task(() => download(file, format))"
            >
              {{ format.toUpperCase() }} ↓
            </button>
          </div>
        </div>
      </section>
    </section>
  </div>
</template>
