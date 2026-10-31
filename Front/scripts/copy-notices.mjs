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
for (const name of [
  "SourceSans3-Regular.ttf",
  "SourceSans3-Semibold.ttf",
  "SourceSans3-Bold.ttf",
  "SourceSerif4-Regular.ttf",
]) {
  await copyFile(
    join(source, "assets/fonts", name),
    join(workspace, "Front/public/fonts", name),
  );
}
await copyFile(
  join(workspace, "Front/assets/fonts/Vazirmatn.woff2"),
  join(workspace, "Front/public/fonts/Vazirmatn.woff2"),
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
notices.push(
  await readFile(
    join(workspace, "Front/assets/fonts/OFL-Vazirmatn.txt"),
    "utf8",
  ),
);
notices.push(
  await readFile(join(workspace, "Front/assets/ATTRIBUTION.md"), "utf8"),
);
await writeFile(
  join(workspace, "Front/public/licenses.txt"),
  notices.join("\n"),
  "utf8",
);
