import { test, expect } from "@playwright/test";
import { translate } from "../src/locales.js";
import { mkdir } from "node:fs/promises";
const output = "../Tests/generated/redesign";
const documents = [
  "Project proposal",
  "یادداشت‌های پروژه",
  "Quarterly review",
].map((title, i) => ({
  id: `00000000-0000-0000-0000-00000000000${i}`,
  title,
  version: 1,
  size: 4096,
  permission: "owner",
  createdAt: Date.parse("2026-10-20T12:00:00Z"),
  updatedAt: Date.parse("2026-10-30T12:00:00Z"),
}));
async function mock(page, authenticated = true) {
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname.replace("/api", "");
    let data = { items: [], total: 0, hasMore: false, page: 1 };
    if (path === "/account/me") {
      if (!authenticated) return route.fulfill({ status: 401, json: {} });
      data = { email: "alex@example.test", isAdmin: true };
    } else if (path === "/library")
      data = { items: documents, total: 3, hasMore: false };
    else if (path === "/documents/") data = documents;
    else if (path === "/documents/formats" || path === "/admin/settings")
      data = {
        maxUploadBytes: 10485760,
        maxDocumentBytes: 268435456,
        maxUploadMiB: 10,
        version: 1,
      };
    else if (path === "/templates/fields") data = { fields: [], required: [] };
    else if (path === "/retention/")
      data = { version: 1, enabled: false, days: 90, keepLatest: 10 };
    else if (path === "/storage")
      data = {
        totalBytes: 12288,
        documentBytes: 12288,
        historyBytes: 0,
        resourceBytes: 0,
        artifactBytes: 0,
        resultBytes: 0,
        trashBytes: 0,
      };
    else if (path.endsWith("/access")) data = documents[0];
    else if (path.endsWith("/shares") || path === "/automation-tokens/")
      data = [];
    return route.fulfill({ json: data });
  });
}
async function shot(page, name) {
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: `${output}/${name}.png`, fullPage: true });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth + 1,
    ),
  ).toBeTruthy();
}
for (const locale of ["en", "fa"])
  for (const width of [1440, 390]) {
    test(`${locale} ${width}: navigation, themes, search, and create dialog`, async ({
      page,
    }) => {
      await mkdir(output, { recursive: true });
      await page.setViewportSize({ width, height: 1000 });
      await page.addInitScript(
        (locale) => localStorage.setItem("inkstone.locale", locale),
        locale,
      );
      const errors = [];
      page.on("pageerror", (error) => errors.push(error.message));
      await mock(page);
      await page.goto("/");
      await expect(page.locator(".document-card")).toHaveCount(3);
      const icon = await page.locator(".search svg").boundingBox();
      const input = await page.locator(".search input").boundingBox();
      expect(
        locale === "fa"
          ? input.x + input.width <= icon.x
          : icon.x + icon.width <= input.x,
      ).toBeTruthy();
      await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
      await shot(page, `${locale}-${width}-workspace-dark`);
      await page
        .getByRole("button", {
          name: translate(locale, "lightMode"),
          exact: true,
        })
        .click();
      await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
      await page.reload();
      await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
      await shot(page, `${locale}-${width}-workspace-light`);
      await page
        .getByRole("button", {
          name: translate(locale, "darkMode"),
          exact: true,
        })
        .click();
      await page
        .getByRole("button", { name: translate(locale, "new"), exact: false })
        .first()
        .click();
      await expect(page.locator("#title")).toBeVisible();
      await expect(page.locator("#document-language")).toHaveCount(0);
      await shot(page, `${locale}-${width}-create`);
      await page
        .getByRole("button", { name: translate(locale, "cancel"), exact: true })
        .click();
      for (const [key, name] of [
        ["ws.automation", "automation"],
        ["ws.reviews", "reviews"],
        ["ws.storage", "storage"],
      ]) {
        await page
          .locator(".workspace-nav")
          .getByRole("button", { name: translate(locale, key), exact: true })
          .click();
        await expect(page.locator(".dialog")).toBeVisible();
        await shot(page, `${locale}-${width}-${name}`);
        if (name === "automation")
          for (const sub of ["bulk", "tokens"]) {
            await page
              .locator(".workspace-tabs")
              .getByRole("button", {
                name: translate(locale, "ws." + sub),
                exact: true,
              })
              .click();
            await shot(page, `${locale}-${width}-${sub}`);
          }
        await page.keyboard.press("Escape");
      }
      for (const [key, name] of [
        ["ws.sharing", "sharing"],
        ["history", "history"],
        ["rename", "rename"],
      ]) {
        await page
          .locator(".document-card")
          .first()
          .getByRole("button", { name: translate(locale, key), exact: true })
          .click();
        await expect(page.locator(".dialog")).toBeVisible();
        await shot(page, `${locale}-${width}-${name}`);
        await page.keyboard.press("Escape");
      }
      for (const [hash, name] of [
        ["templates", "templates"],
        ["letters", "letters"],
        ["admin", "admin"],
      ]) {
        await page.goto(`/#/${hash}`);
        await page.reload();
        await expect(
          page.locator(name === "admin" ? ".admin-page" : ".studio-page"),
        ).toBeVisible();
        await shot(page, `${locale}-${width}-${name}`);
        if (name === "templates")
          for (const sub of ["senders", "images"]) {
            await page
              .locator(".studio-tabs")
              .getByRole("button", {
                name: translate(locale, "studio." + sub),
                exact: true,
              })
              .click();
            await shot(page, `${locale}-${width}-${sub}`);
          }
      }
      expect(errors).toEqual([]);
    });
    test(`${locale} ${width}: account screen`, async ({ page }) => {
      await page.setViewportSize({ width, height: 1000 });
      await page.addInitScript(
        (locale) => localStorage.setItem("inkstone.locale", locale),
        locale,
      );
      await mock(page, false);
      await page.goto("/");
      await expect(page.locator(".auth-form")).toBeVisible();
      await shot(page, `${locale}-${width}-login`);
    });
  }
for (const locale of ["en", "fa"])
  for (const theme of ["dark", "light"])
    for (const width of [1440, 740]) {
      test(`persistent workspace: ${locale} ${theme} ${width}`, async ({
        page,
      }) => {
        await page.setViewportSize({ width, height: 900 });
        await page.addInitScript(
          ({ locale, theme }) => {
            localStorage.setItem("inkstone.locale", locale);
            localStorage.setItem("inkstone.theme", theme);
          },
          { locale, theme },
        );
        await mock(page);
        await page.goto("/");
        const nav = page.locator(".workspace-nav");
        await nav
          .getByRole("button", {
            name: translate(locale, "studio.templates"),
            exact: true,
          })
          .click();
        await expect(page.locator(".studio-page")).toBeVisible();
        await expect(nav).toBeVisible();
        await expect(
          nav.getByRole("button", {
            name: translate(locale, "studio.templates"),
            exact: true,
          }),
        ).toHaveClass(/active/);
        await page
          .locator(".studio-page")
          .getByLabel(translate(locale, "studio.templateName"), { exact: true })
          .fill("Reusable draft");
        await shot(page, `${locale}-${width}-${theme}-persistent-templates`);
        await nav
          .getByRole("button", {
            name: translate(locale, "studio.letters"),
            exact: true,
          })
          .click();
        const letterTitle = page
          .locator(".studio-page")
          .getByLabel(translate(locale, "studio.title"), { exact: true });
        await letterTitle.fill("Unsaved letter draft");
        await shot(page, `${locale}-${width}-${theme}-persistent-letters`);
        await nav.locator("button").first().click();
        await expect(page.locator(".content-body")).toBeVisible();
        await nav
          .getByRole("button", {
            name: translate(locale, "studio.templates"),
            exact: true,
          })
          .click();
        await expect(
          page
            .locator(".studio-page")
            .getByLabel(translate(locale, "studio.templateName"), {
              exact: true,
            }),
        ).toHaveValue("Reusable draft");
        await nav
          .getByRole("button", {
            name: translate(locale, "studio.letters"),
            exact: true,
          })
          .click();
        await expect(letterTitle).toHaveValue("Unsaved letter draft");
        await nav
          .getByRole("button", {
            name: translate(locale, "ws.storage"),
            exact: true,
          })
          .click();
        await expect(page.locator(".dialog")).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(page.locator(".dialog")).toHaveCount(0);
        await expect(letterTitle).toHaveValue("Unsaved letter draft");
      });
    }
