# Letter layouts

These are original Inkstone layouts with reusable bookmarks, not copies of another organization's letterhead. `document.letterTemplate` creates them in the engine; the standalone API exposes the same operation through `POST /api/templates/starter`.

## Persian

The typography follows [Faragostar's administrative-letter guide](https://www.faragostar.net/سایز-و-نوع-فونت-رسمی-در-نامه-های-اداری/), whose size recommendations describe A5 letters:

| Element | Font | Size | Weight |
| --- | --- | --- | --- |
| Recipient, subject, sender name/title | B Titr | 11 pt | Bold |
| Body and greeting/closing | B Zar | 13 pt | Bold |
| Copies | B Zar | 9 pt | Bold |
| Company name and contact details | B Zar | 7 pt | Bold |

Persian starters default to A5. Choose A4 for longer correspondence. Both sizes use the same type hierarchy, RTL paragraphs, justified body text and explicit complex-script font properties. Margins, paragraph spacing and image sizes are Inkstone layout choices, rather than requirements stated by that article. Date/reference metadata is 9 pt; tracking and centered page numbers are 7 pt.

The B fonts are referenced by family name, **not bundled**: their redistribution rights have not been established. Install licensed B Zar and B Titr on machines rendering or editing these documents for matching glyphs. Without them the engine uses available fallbacks, including bundled OFL Noto Naskh Arabic; DOCX retains the requested B-family names and sizes. No B-font files or assets from the article are included.

## English

The English starter follows [Purdue OWL's business-letter guidance](https://owl.purdue.edu/owl/subject_specific_writing/professional_technical_writing/basic_business_letters/index.html): full-block alignment, Times New Roman 12 pt, single-spaced paragraphs with space between them, an address/date/recipient opening and a closing followed by the signature and typed identity. The default is US Letter with one-inch side margins. English dates use a full month name. Organization branding, reference/subject lines and a compact tracking footer support the application's letter workflow.

Times New Roman must also be installed for exact face matching; no proprietary font is redistributed. System and bundled fonts provide fallback rendering.

## Editing and composition

The templates retain all 17 canonical bookmarks, including fields in their header/footer stories. Signature, logo and QR images fit bounded boxes while preserving aspect ratio. Signer paragraphs stay together. Empty attachments produce no permanent label; populated attachments receive a localized label. Profile-based plain body input uses a new paragraph for each newline. Rich imported bodies retain their own formatting, and a user can edit a copied template freely; composition does not restyle arbitrary custom templates.

`paper` accepts `A5`, `A4` or `Letter` for letter starters. Omitting it selects the language default. Template content stores the actual geometry, so later language changes do not resize an existing draft. New profiles do not rewrite existing templates, working copies or frozen output. The demo provisioning script adds newly named layouts and a prepared Persian example without overwriting previous examples.

Run `python Tests/letter_template_layout_smoke.py` after building the API. It checks native styles, script properties, paper dimensions, bookmarks, actual DOCX/PDF generation and date formatting, and writes original samples under ignored `Tests/generated/`. PDF visual checks cover available fallback fonts; these checks do not certify Microsoft Word desktop rendering.
