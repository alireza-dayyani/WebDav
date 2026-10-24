import { readFile, readdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const workspace = fileURLToPath(new URL("../../", import.meta.url));
const source = join(workspace, "Application");
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
