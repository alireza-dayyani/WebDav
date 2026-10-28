# Original template and letter fixtures

`studio_test_support.py` builds small native models with original placeholder text and a two-pixel PNG generated in code. No legacy documents, signatures, accounts or external services are used.

- `template_api_smoke.py` creates ordinary document templates with `CustomerName` and `Body` bookmarks. It renders independent copies and checks privacy, versions, template/source isolation, failed batches and source deletion.
- `letter_api_smoke.py` creates a Persian letter starter and synthetic sender/signature assets. Its rich body contains Persian text, a table and an image with a deliberate style-ID collision. It checks registered recipients, copy visibility, previews, finalization, retries, races and frozen attachments.
- `bookmark_api_smoke.py` covers general DOCX bookmark replacement independently of letter rules.
- `letter_editor_api_smoke.py` writes a rich body directly into a private template copy, fills recipient/sender/title/signature bookmarks, and checks identity tampering, table preservation, versions, concurrency, duplication and review recovery. It writes `edited-letter.docx` and `edited-letter.pdf` examples.
- `demo_seed_smoke.py` verifies all eight sample accounts/resources and repeat provisioning against an isolated database; persistent local examples are created separately by `Scripts/seed_letter_demo.py`.
- `letter_template_layout_smoke.py` checks the sourced Persian A5/A4 and English Letter profiles, script font properties, bookmark safety, images, dates and DOCX/PDF output. It creates `persian-official-a5`, `persian-official-a4` and `english-business-letter` samples under ignored `Tests/generated/` for visual inspection.

Running the two studio tests writes two ordinary DOCX examples and letter `primary`, `without-copies`, individual `copy-*`, and attachment DOCX/PDF files to `Tests/generated/`. Those generated outputs are ignored by Git. Re-running tests may add another identified attachment fixture.
