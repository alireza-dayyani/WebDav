"""Real DOCX bookmark automation through the standalone API and native Rust engine."""
import concurrent.futures
import http.cookiejar
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:5095'
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'


class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path, method='GET', data=None, mime='application/json', expected=200, csrf=True):
        headers = {'Content-Type': mime} if data is not None else {}
        if method != 'GET' and csrf:
            headers['X-CSRF-TOKEN'] = json.loads(self.request('/api/csrf')[0])['token']
        try:
            response = self.opener.open(urllib.request.Request(BASE + path, data=data, method=method, headers=headers), timeout=120)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read()
        allowed = (expected,) if isinstance(expected, int) else expected
        assert response.status in allowed, (method, path, response.status, allowed, body[:700])
        return body, response.status

    def json(self, path, method='GET', value=None, expected=200, csrf=True):
        body, _ = self.request('/api' + path, method, json.dumps(value, ensure_ascii=False).encode() if value is not None else None, expected=expected, csrf=csrf)
        return json.loads(body) if body else None

    def upload(self, content):
        boundary = 'inkstone-bookmark-test'
        body = f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="template.docx"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode() + content + f'\r\n--{boundary}--\r\n'.encode()
        result, _ = self.request('/api/documents/upload', 'POST', body, 'multipart/form-data; boundary=' + boundary, 201)
        return json.loads(result)


def bookmark(identifier, name, text):
    return f'<w:bookmarkStart w:id="{identifier}" w:name="{name}"/><w:r><w:rPr><w:b/></w:rPr><w:t>{escape(text)}</w:t></w:r><w:bookmarkEnd w:id="{identifier}"/>'


def template(body=None):
    if body is None:
        body = ''.join(f'<w:p>{bookmark(i, name, text)}</w:p>' for i, name, text in [
            (1, 'CustomerName', 'old customer'), (2, 'Amount', 'old amount'),
            (3, 'Empty', ''), (4, 'Notes', 'old notes'), (5, 'Untouched', 'KEEP THIS'),
        ])
        body += '<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2000"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p>' + bookmark(6, 'Cell', 'old cell') + '</w:p></w:tc></w:tr></w:tbl>'
        body += '<w:p><w:bookmarkStart w:id="7" w:name="Paragraphs"/><w:r><w:t>first old paragraph</w:t></w:r></w:p><w:p><w:r><w:t>second old paragraph</w:t></w:r><w:bookmarkEnd w:id="7"/></w:p>'
    content = io.BytesIO()
    with zipfile.ZipFile(content, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>')
        archive.writestr('_rels/.rels', f'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{R}/officeDocument" Target="word/document.xml"/></Relationships>')
        archive.writestr('word/_rels/document.xml.rels', f'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="header" Type="{R}/header" Target="header1.xml"/><Relationship Id="footer" Type="{R}/footer" Target="footer1.xml"/></Relationships>')
        archive.writestr('word/document.xml', f'<w:document xmlns:w="{W}" xmlns:r="{R}"><w:body>{body}<w:sectPr><w:headerReference w:type="default" r:id="header"/><w:footerReference w:type="default" r:id="footer"/></w:sectPr></w:body></w:document>')
        archive.writestr('word/header1.xml', f'<w:hdr xmlns:w="{W}"><w:p>{bookmark(8, "Header", "old header")}</w:p></w:hdr>')
        archive.writestr('word/footer1.xml', f'<w:ftr xmlns:w="{W}"><w:p>{bookmark(9, "Footer", "old footer")}</w:p></w:ftr>')
    return content.getvalue()


with tempfile.TemporaryDirectory(prefix='inkstone-bookmarks-') as data, open(ROOT / 'Tests/bookmark-api.log', 'w', encoding='utf-8') as log:
    environment = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__DataDirectory': data}
    process = subprocess.Popen(['dotnet', str(ROOT / 'Back/bin/Debug/net10.0/Inkstone.Api.dll'), '--urls', BASE], cwd=ROOT / 'Back', env=environment, stdout=log, stderr=log)
    try:
        guest = Client()
        for _ in range(120):
            try:
                guest.json('/health')
                break
            except OSError:
                if process.poll() is not None:
                    raise RuntimeError('API failed to start; see Tests/bookmark-api.log')
                time.sleep(.25)
        alice, bob = Client(), Client()
        for client, name in [(alice, 'bookmark-alice'), (bob, 'bookmark-bob')]:
            client.json('/account/register', 'POST', {'email': name + '@example.test', 'password': 'BookmarkTests123'})
        spec = json.loads(guest.request('/openapi/v1.json')[0])
        assert '/api/documents/{id}/bookmarks' in spec['paths']
        assert '/api/documents/{id}/bookmarks/replace' in spec['paths']
        assert 'values' in spec['components']['schemas']['ReplaceBookmarks']['properties']
        value_schema = spec['components']['schemas']['ReplaceBookmarks']['properties']['values']
        assert value_schema['type'] == 'object' and value_schema['additionalProperties']['type'] == 'string'
        assert spec['components']['schemas']['ReplaceBookmarks']['examples'][0]['values']['CustomerName'] == 'علی رضایی'
        doc = alice.upload(template())
        path = f'/documents/{doc["id"]}'
        listing = alice.json(path + '/bookmarks?version=1')
        names = {b['name'] for b in listing['bookmarks']}
        assert names == {'CustomerName', 'Amount', 'Empty', 'Notes', 'Untouched', 'Cell', 'Paragraphs', 'Header', 'Footer'}, listing
        assert all(b['replaceable'] for b in listing['bookmarks']), listing
        alice.json(path + '/bookmarks', expected=400)
        alice.json(path + '/bookmarks?version=99', expected=409)
        guest.json(path + '/bookmarks?version=1', expected=401)
        bob.json(path + '/bookmarks?version=1', expected=404)
        sample = {'version': 1, 'values': {'CustomerName': 'new'}}
        guest.json(path + '/bookmarks/replace', 'POST', sample, expected=401)
        bob.json(path + '/bookmarks/replace', 'POST', sample, expected=404)
        alice.json(path + '/bookmarks/replace', 'POST', sample, expected=400, csrf=False)
        for values in [{}, [], {'Amount': 123}, {'Amount': None}, {'Amount': {}}, {'Amount': '\u0000'}, {'Amount': '\ufffc'}]:
            alice.json(path + '/bookmarks/replace', 'POST', {'version': 1, 'values': values}, expected=400)
        for values in [{'CustomerName': 'new', 'Missing': 'error'}, {'customername': 'wrong case'}]:
            error = alice.json(path + '/bookmarks/replace', 'POST', {'version': 1, 'values': values}, expected=422)
            assert error['invalidKeys']
        alice.request('/api' + path + '/bookmarks/replace', 'POST', b'{"version":1,"values":{"Amount":"one","Amount":"two"}}', expected=400)
        alice.json(path + '/bookmarks/replace', 'POST', {'version': 1, 'values': {'Amount': 'x' * (512 * 1024 + 1)}}, expected=413)
        alice.json(path + '/bookmarks/replace', 'POST', {'version': 1, 'values': {f'K{i}': '' for i in range(257)}}, expected=413)
        alice.request('/api' + path + '/bookmarks/replace', 'POST', b' ' * (4 * 1024 * 1024 + 1), expected=413)
        assert alice.json(path)['version'] == 1

        values = {'CustomerName': 'علی رضایی <&> "مشتری"', 'Amount': '1,234.50', 'Empty': '"',
                  'Notes': 'line one\r\nline two\ttab', 'Cell': 'سلول جدید', 'Paragraphs': 'replaced paragraphs',
                  'Header': 'سربرگ', 'Footer': ''}
        result = alice.json(path + '/bookmarks/replace', 'POST', {'version': 1, 'values': values})
        assert result['metadata']['version'] == 2 and set(result['replaced']) == set(values), result
        alice.json(path + '/bookmarks/replace', 'POST', sample, expected=409)
        snapshot = alice.json(path + '/snapshot')
        serialized = json.dumps(snapshot['document'], ensure_ascii=False)
        assert 'علی رضایی <&>' in serialized and 'KEEP THIS' in serialized and 'old customer' not in serialized
        assert 'old cell' not in serialized and 'first old paragraph' not in serialized and 'second old paragraph' not in serialized
        assert 'old header' not in serialized and 'old footer' not in serialized
        assert {b['name'] for b in alice.json(path + '/bookmarks?version=2')['bookmarks']} == names
        output, _ = alice.request('/api' + path + '/download?format=docx&version=2')
        with zipfile.ZipFile(io.BytesIO(output)) as archive:
            xml_parts = [ET.fromstring(archive.read(n)) for n in archive.namelist() if n.startswith('word/') and n.endswith('.xml')]
            exported_names = {e.attrib[f'{{{W}}}name'] for part in xml_parts for e in part.iter(f'{{{W}}}bookmarkStart')}
            assert names <= exported_names
            text = ''.join(e.text or '' for part in xml_parts for e in part.iter(f'{{{W}}}t'))
            assert values['CustomerName'] in text and 'سربرگ' in text and 'سلول جدید' in text
            replacement_runs = [run for part in xml_parts for run in part.iter(f'{{{W}}}r')
                                if values['CustomerName'] in ''.join(e.text or '' for e in run.iter(f'{{{W}}}t'))]
            assert replacement_runs and any(run.find(f'{{{W}}}rPr/{{{W}}}b') is not None for run in replacement_runs), 'replacement formatting was lost'
        roundtrip = alice.upload(output)
        assert {b['name'] for b in alice.json(f'/documents/{roundtrip["id"]}/bookmarks?version=1')['bookmarks']} == names
        alice.json(path + '/bookmarks/replace', 'POST', {'version': 2, 'values': {'Empty': '', 'CustomerName': 'second pass'}})
        assert 'second pass' in json.dumps(alice.json(path + '/content'))

        # A write can race a second conversion. Only one may commit the expected revision.
        def race(text):
            return alice.request('/api' + path + '/bookmarks/replace', 'POST', json.dumps({'version': 3, 'values': {'Amount': text}}).encode(), expected=(200, 409))[1]
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            assert sorted(pool.map(race, ['A', 'B'])) == [200, 409]
        assert alice.json(path)['version'] == 4
        for setting, value in [('trackChanges', True), ('protection', 'readOnly')]:
            model = alice.json(path + '/content')
            model['settings'][setting] = value
            saved = alice.json(path + '/content', 'PUT', {'version': alice.json(path)['version'], 'document': model})
            alice.json(path + '/bookmarks/replace', 'POST', {'version': saved['version'], 'values': {'Amount': 'refused'}}, expected=422)
            assert alice.json(path)['version'] == saved['version']
            model['settings'][setting] = False if setting == 'trackChanges' else None
            alice.json(path + '/content', 'PUT', {'version': saved['version'], 'document': model})

        unsafe = [
            '<w:p><w:bookmarkStart w:id="10" w:name="Outer"/>' + bookmark(11, 'Inner', 'nested') + '<w:bookmarkEnd w:id="10"/></w:p>',
            '<w:p>' + bookmark(12, 'Duplicate', 'one') + bookmark(13, 'Duplicate', 'two') + '</w:p>',
            '<w:p><w:bookmarkStart w:id="14" w:name="Unpaired"/><w:r><w:t>old</w:t></w:r></w:p>',
            '<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p><w:bookmarkStart w:id="15" w:name="AcrossCells"/><w:r><w:t>one</w:t></w:r></w:p></w:tc><w:tc><w:tcPr/><w:p><w:r><w:t>two</w:t></w:r><w:bookmarkEnd w:id="15"/></w:p></w:tc></w:tr></w:tbl>',
        ]
        for body, name in zip(unsafe, ['Outer', 'Duplicate', 'Unpaired', 'AcrossCells']):
            bad = alice.upload(template(body))
            bad_path = f'/documents/{bad["id"]}'
            info = alice.json(bad_path + '/bookmarks?version=1')
            field = next(b for b in info['bookmarks'] if b['name'] == name)
            assert field['replaceable'] is False and field['issue'], field
            alice.json(bad_path + '/bookmarks/replace', 'POST', {'version': 1, 'values': {name: 'refused', 'Header': 'also not saved'}}, expected=422)
            assert alice.json(bad_path)['version'] == 1
        blank = alice.json('/documents/', 'POST', {'title': 'Blank'}, expected=201)
        assert alice.json(f'/documents/{blank["id"]}/bookmarks?version=1')['bookmarks'] == []
        alice.json(f'/documents/{blank["id"]}/bookmarks/replace', 'POST', {'version': 1, 'values': {'Name': 'x'}}, expected=422)
        print('PASS: DOCX body/table/header/footer bookmarks, Persian/literal text, empty and repeated replacement, multiline ranges, formatting, marker roundtrip, validation, atomic batches, version races, CSRF, owner isolation, OpenAPI and protection checks.')
    finally:
        process.terminate()
        process.wait(timeout=20)
