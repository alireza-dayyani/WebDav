# Local letter demonstration data

After building the backend, run from the repository root:

```powershell
python Scripts/seed_letter_demo.py
```

This is an explicit development operation. It starts a temporary loopback API, creates eight normal demo users, and stops that process when finished. It uses the existing private `Back/App_Data` database by default. `--data-directory` selects a different persistent data directory; `--api-url` can use an already-running loopback API.

Every demo user gets Persian/English letter templates, two sender titles, two synthetic signature images, a logo and a prepared editable letter with registered main/copy recipients. The first demo user also gets a distinct reply template, an ordinary meeting-notes template, attachment documents and a finalized letter with download variants. When the private bootstrap login is available, the same extended examples are added to that account's workspace.

New layouts are named `[Demo] نامه رسمی فارسی A5`, `[Demo] نامه رسمی فارسی A4` and `[Demo] English block letter`; the extended workspace has an A4 reply layout. `[Demo] نامه با قالب رسمی جدید` is a short prepared letter using the new A5 layout. Running the script on an older demo database adds these resources alongside existing ones, preserving edited drafts and frozen files. Font requirements and reference sources are documented in [letter layouts](../Application/docs/LETTER_TEMPLATES.md).

Random login passwords and resource IDs are saved only in `Back/App_Data/demo/accounts.json`. That ignored folder is restricted to the current Windows user, SYSTEM and local administrators. Passwords are never printed or embedded in source. Re-running uses the saved credentials and skips existing named examples; it does not reset accounts or overwrite letters that users have edited. Keep the private manifest with the demo database when backing it up.

All names and content are fictional. The PNGs are original geometric artwork and algorithmically drawn demonstration strokes, visibly marked DEMO; they are not real signatures. Their source is `demo_png` in this script. No legacy documents, external assets, external accounts or delivery services are used.
