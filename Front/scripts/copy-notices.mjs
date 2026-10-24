import {
  readFile,
  readdir,
  writeFile,
  mkdir,
  copyFile,
} from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const workspace = fileURLToPath(new URL("../../", import.meta.url));
const source = join(workspace, "Application");
await mkdir(join(workspace, "Front/public/fonts"), { recursive: true });
await copyFile(
  join(source, "assets/fonts/NotoNaskhArabic.ttf"),
  join(workspace, "Front/public/fonts/NotoNaskhArabic.ttf"),
);
const files = ["LICENSE-MIT", "LICENSE-APACHE", "NOTICE", "ATTRIBUTION.md"];
for (const name of await readdir(join(source, "assets/fonts"))) {
  if (name.startsWith("OFL-") && name.endsWith(".txt"))
    files.push("assets/fonts/" + name);
}
const notices = await Promise.all(
  files.map(
    async (name) =>
      `===== ${name} =====\n\n${await readFile(join(source, name), "utf8")}\n`,
  ),
);
await writeFile(
  join(workspace, "Front/public/licenses.txt"),
  notices.join("\n"),
  "utf8",
);
