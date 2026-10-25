"""Administration, existing-database upgrades, and near-limit uploads through the Vue proxy."""
import base64
import http.cookiejar
import io
import json
import os
from pathlib import Path
import secrets
import sqlite3
import string
import struct
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:5093'
MIB = 1024 * 1024


class Client:
    def __init__(self, base=BASE, opener=None):
        self.base = base
        self.opener = opener or urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path, method='GET', data=None, mime='application/json', expected=200, csrf=True):
        headers = {'Content-Type': mime} if data is not None else {}
        if method != 'GET' and csrf:
            headers['X-CSRF-TOKEN'] = self.json('/csrf')['token']
        request = urllib.request.Request(self.base + '/api' + path, data=data, method=method, headers=headers)
        try:
            response = self.opener.open(request, timeout=120)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read()
        assert response.status == expected, (method, path, response.status, expected, body[:500])
        return body

    def json(self, path, method='GET', value=None, expected=200, csrf=True):
        body = self.request(path, method, json.dumps(value, ensure_ascii=False).encode() if value is not None else None, expected=expected, csrf=csrf)
        return json.loads(body) if body else None

    def upload(self, name, content, expected=201, path='/documents/upload'):
        boundary = 'inkstone-admin-upload-regression'
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()
                + content + f'\r\n--{boundary}--\r\n'.encode())
        return json.loads(self.request(path, 'POST', body, 'multipart/form-data; boundary=' + boundary, expected=expected))


def start(data, log):
    environment = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__DataDirectory': str(data)}
    process = subprocess.Popen(['dotnet', str(ROOT / 'Back/bin/Debug/net10.0/Inkstone.Api.dll'), '--urls', BASE], cwd=ROOT / 'Back', env=environment, stdout=log, stderr=log)
    for _ in range(160):
        try:
            Client().json('/health')
            return process
        except OSError:
            if process.poll() is not None:
                raise RuntimeError('API startup failed; inspect Tests/admin-api.log')
            time.sleep(.25)
    process.terminate()
    raise RuntimeError('API startup timed out')


def stop(process):
    if process and process.poll() is None:
        process.terminate()
        process.wait(timeout=20)


def image_document():
    # Valid one-pixel PNG with trailing fixture bytes: media expands to base64 in the native model.
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\0\0\0\0')) + chunk(b'IEND', b'')
    media = png + secrets.token_bytes(int(9.5 * MIB) - len(png))
    document = '''<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body><w:p><w:r><w:t>Near-limit upload سلام</w:t></w:r><w:r><w:drawing><wp:inline><wp:extent cx="914400" cy="914400"/><wp:docPr id="1" name="Fixture"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="Fixture"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rIdImage"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p></w:body></w:document>'''
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_STORED) as archive:
        archive.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        archive.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        archive.writestr('word/document.xml', document)
        archive.writestr('word/_rels/document.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/fixture.png"/></Relationships>')
        archive.writestr('word/media/fixture.png', media)
    return output.getvalue()


with tempfile.TemporaryDirectory(prefix='inkstone-admin-tests-') as directory, open(ROOT / 'Tests/admin-api.log', 'w', encoding='utf-8') as log:
    data = Path(directory)
    bootstrap = data / 'bootstrap/superadmin.json'
    bootstrap.parent.mkdir()
    root_password = ''.join(secrets.choice(string.ascii_lowercase) for _ in range(12))
    bootstrap.write_text(json.dumps({'Email': 'root@example.test', 'Password': root_password}))
    process = start(data, log)
    proxy = None
    try:
        guest, root, alice, bob = Client(), Client(), Client(), Client()
        root_user = root.json('/account/login', 'POST', {'email': 'root@example.test', 'password': root_password})
        assert root_user['isAdmin'] and root_user['isSuperAdmin']
        for _ in range(25):
            assert root.json('/account/me')['isSuperAdmin'], 'reading roles must not consume the sign-in rate limit'
        password = 'TestAa1' + secrets.token_hex(10)
        for client, email in [(alice, 'admin-alice@example.test'), (bob, 'admin-bob@example.test')]:
            user = client.json('/account/register', 'POST', {'email': email, 'password': password, 'isAdmin': True})
            assert not user['isAdmin']
        guest.json('/admin/users', expected=401)
        alice.json('/admin/users', expected=403)
        alice.json('/admin/settings', 'PUT', {'maxUploadMiB': 20, 'version': 1}, expected=403)
        private = alice.upload('private.txt', 'Private file سلام'.encode())
        identifier = private['id']
        alice_id = root.json('/admin/users?search=admin-alice')['items'][0]['id']
        listed = root.json(f'/admin/users/{alice_id}/documents')
        assert listed['items'][0]['id'] == identifier
        root.json(f'/documents/{identifier}', expected=404)
        bob.json(f'/documents/{identifier}', expected=404)
        alice.json(f'/admin/documents/{identifier}/preview?version=1', expected=403)
        preview = root.json(f'/admin/documents/{identifier}/preview?version=1')
        assert 'Private file سلام' in preview['text']
        assert json.loads(root.request(f'/admin/documents/{identifier}/download?format=json&version=1'))['body']
        root.json(f'/admin/documents/{identifier}/preview?version=99', expected=409)
        root.request(f'/admin/documents/{identifier}/download?version=99', expected=409)
        root.json(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': {'body': []}}, expected=404)
        alice.upload('trailing-slash.txt', b'Route matching also applies upload limits.', path='/documents/upload/')

        # Simulate the previous database schema; existing accounts/files and cookies must survive upgrade.
        stop(process)
        with sqlite3.connect(data / 'inkstone.db') as database:
            database.execute('DROP TABLE UploadPolicies')
        database.close()
        process = start(data, log)
        assert alice.json(f'/documents/{identifier}')['title'] == 'private'
        assert root.json('/admin/settings')['maxUploadMiB'] == 10

        def find(email):
            return root.json('/admin/users?search=' + urllib.parse.quote(email))['items'][0]
        alice_record = find('admin-alice@example.test')
        root.json(f'/admin/users/{alice_id}/admin', 'PUT', {'isAdmin': True, 'version': alice_record['version']}, expected=400, csrf=False)
        root.json(f'/admin/users/{alice_id}/admin', 'PUT', {'isAdmin': True, 'version': alice_record['version']})
        assert alice.json('/account/me')['isAdmin']  # Cookie predates promotion.
        alice.json('/admin/users')
        root.json(f'/admin/users/{alice_id}/admin', 'PUT', {'isAdmin': False, 'version': alice_record['version']}, expected=409)
        bob_record = find('admin-bob@example.test')
        alice.json(f'/admin/users/{bob_record["id"]}/admin', 'PUT', {'isAdmin': True, 'version': bob_record['version'], 'role': 'SuperAdmin'})
        assert not bob.json('/account/me')['isSuperAdmin']
        bob.json('/admin/settings')
        bob_record = find('admin-bob@example.test')
        root.json(f'/admin/users/{bob_record["id"]}/admin', 'PUT', {'isAdmin': False, 'version': bob_record['version']})
        bob.json('/admin/settings', expected=403)  # Old administrator cookie must no longer authorize.
        protected = find('root@example.test')
        alice.json(f'/admin/users/{protected["id"]}/admin', 'PUT', {'isAdmin': False, 'version': protected['version']}, expected=400)

        proxy_environment = {**os.environ, 'INKSTONE_API_URL': BASE}
        proxy = subprocess.Popen(['node', str(ROOT / 'Front/node_modules/vite/bin/vite.js'), '--host', '127.0.0.1', '--port', '5181', '--strictPort'], cwd=ROOT / 'Front', env=proxy_environment, stdout=log, stderr=log)
        through_proxy = Client('http://127.0.0.1:5181', alice.opener)
        for _ in range(120):
            try:
                through_proxy.json('/health')
                break
            except OSError:
                time.sleep(.25)
        large_docx = image_document()
        assert 9.5 * MIB <= len(large_docx) < 10 * MIB
        large = through_proxy.upload('near-limit.docx', large_docx)
        assert large['size'] > 10 * MIB, 'converted media must exceed the old model cap'
        assert alice.json(f'/documents/{large["id"]}/snapshot')['document']['body']
        policy = root.json('/admin/settings')
        root.json('/admin/settings', 'PUT', {'maxUploadMiB': 0, 'version': policy['version']}, expected=400)
        root.json('/admin/settings', 'PUT', {'maxUploadMiB': 129, 'version': policy['version']}, expected=400)
        updated = root.json('/admin/settings', 'PUT', {'maxUploadMiB': 12, 'version': policy['version']})
        alice.json('/admin/settings', 'PUT', {'maxUploadMiB': 20, 'version': policy['version']}, expected=409)
        assert through_proxy.json('/documents/formats')['maxUploadBytes'] == 12 * MIB
        above_default = b'{"body":[],"padding":"' + b'x' * int(10.5 * MIB) + b'"}'
        through_proxy.upload('above-default.json', above_default)
        root.json('/admin/settings', 'PUT', {'maxUploadMiB': 1, 'version': updated['version']})
        denied = through_proxy.upload('above-current.json', b'{"body":[],"padding":"' + b'x' * (2 * MIB) + b'"}', expected=413)
        assert denied['maxBytes'] == MIB
        # Lowering the upload limit must not prevent existing documents from being saved or downloaded.
        saved = alice.json(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': {'body': [], 'testText': 'x' * (2 * MIB)}})
        assert saved['version'] == 2
        assert root.request(f'/admin/documents/{large["id"]}/download?format=json&version=1')
        stop(process)
        bootstrap.write_text(json.dumps({'Email': 'root@example.test', 'Password': 'another-initial-password'}))
        process = start(data, log)
        assert root.json('/admin/settings')['maxUploadMiB'] == 1
        root.json('/account/login', 'POST', {'email': 'root@example.test', 'password': root_password})
        assert alice.json('/account/me')['isAdmin']
        stop(process)
        bootstrap.write_text(json.dumps({'Email': 'admin-bob@example.test', 'Password': root_password}))
        collision_environment = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__DataDirectory': str(data)}
        process = subprocess.Popen(['dotnet', str(ROOT / 'Back/bin/Debug/net10.0/Inkstone.Api.dll'), '--urls', BASE], cwd=ROOT / 'Back', env=collision_environment, stdout=log, stderr=log)
        assert process.wait(timeout=20) != 0, 'bootstrap must refuse to take over an existing ordinary account'
        with sqlite3.connect(data / 'inkstone.db') as connection:
            elevated = connection.execute("SELECT COUNT(*) FROM AspNetUserRoles ur JOIN AspNetUsers u ON u.Id=ur.UserId JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE u.Email=? AND r.Name='SuperAdmin'", ('admin-bob@example.test',)).fetchone()[0]
        connection.close()
        assert elevated == 0
        print('PASS: protected bootstrap, legacy schema upgrade, live role grants/revocations, owner isolation, admin previews/downloads, version checks, persistent upload policy, 9.5 MiB DOCX through Vue proxy, expanded models, raised/lowered limits and existing-file saves.')
    finally:
        stop(proxy)
        stop(process)
