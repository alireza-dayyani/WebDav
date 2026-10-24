import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const workspace = fileURLToPath(new URL("../../", import.meta.url));
const args = ["build", "--cargo-profile", "web", "--no-sri"];
if (process.argv.includes("--optimize")) args.push("--release");
const result = spawnSync("trunk", args, {
  cwd: join(workspace, "Application/apps/inkstone-web"),
  stdio: "inherit",
  shell: process.platform === "win32",
  env: {
    ...process.env,
    CARGO_TARGET_DIR:
      process.env.CARGO_TARGET_DIR || join(workspace, "Application/target-web"),
  },
});
if (result.error)
  console.error(
    "Install the WebAssembly target and Trunk first. See README.md.",
    result.error.message,
  );
process.exit(result.status ?? 1);
