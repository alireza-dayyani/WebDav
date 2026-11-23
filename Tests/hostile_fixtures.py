"""Synthetic hostile archives for converter boundary tests. Nothing is stored in Git."""
import io
import struct
import zipfile

DOCX_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
CT_NS = "http://schemas.openxmlformats.org/package/2006/content-types"
REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"


def minimal_docx(text="Hostile fixture"):
    body = (
        f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        f'<w:document xmlns:w="{DOCX_NS}"><w:body>'
        f'<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>'
    )
    content_types = (
        f'<?xml version="1.0" encoding="UTF-8"?>'
        f'<Types xmlns="{CT_NS}">'
        f'<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        f'<Default Extension="xml" ContentType="application/xml"/>'
        f'<Override PartName="/word/document.xml" '
        f'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        f"</Types>"
    )
    rels = (
        f'<?xml version="1.0" encoding="UTF-8"?>'
        f'<Relationships xmlns="{REL_NS}">'
        f'<Relationship Id="rId1" Type="'
        f"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
        f'" Target="word/document.xml"/>'
        f"</Relationships>"
    )
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("[Content_Types].xml", content_types)
        archive.writestr("_rels/.rels", rels)
        archive.writestr("word/document.xml", body)
    return buffer.getvalue()


def zip_entry_count(count):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        for index in range(count):
            archive.writestr(f"part-{index}.xml", b"<t/>")
    return buffer.getvalue()


def zip_traversal_docx():
    """DOCX-shaped archive with a path-traversal member name."""
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        archive.writestr("[Content_Types].xml", b"<Types/>")
        archive.writestr("word/../../../escape.xml", b"<escape/>")
        archive.writestr("word/document.xml", b"<document/>")
    return buffer.getvalue()


def zip_inflated_metadata():
    """
    Small compressed payload; patched central directory claims ~200 MiB uncompressed per entry.
  .NET ValidateArchive uses ZipArchiveEntry.Length (uncompressed size from the directory).
    """
    payload = b"0" * 64
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        archive.writestr("word/document.xml", payload)
    data = bytearray(buffer.getvalue())
    inflated = struct.pack("<I", 210 * 1024 * 1024)
    marker = b"PK\x01\x02"
    offset = 0
    patched = 0
    while True:
        index = data.find(marker, offset)
        if index < 0:
            break
        # uncompressed size field in central directory header (offset 24 from PK\x01\x02)
        size_offset = index + 24
        if size_offset + 4 <= len(data):
            data[size_offset : size_offset + 4] = inflated
            patched += 1
        offset = index + 4
    if patched == 0:
        raise RuntimeError("Could not patch zip central directory for inflated-metadata fixture")
    return bytes(data)


def native_blank_model():
    return b'{"body":[{"kind":"para","text":"","runs":[]}]}'