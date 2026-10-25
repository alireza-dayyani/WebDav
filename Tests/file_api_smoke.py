"""Exercise real file conversion, RTL defaults, private downloads and OpenAPI without Vue."""
import http.cookiejar
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:5091'


class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path, method='GET', data=None, mime='application/json', expected=200, csrf=True):
        headers = {}
        if data is not None:
            headers['Content-Type'] = mime
        if method != 'GET' and csrf:
            headers['X-CSRF-TOKEN'] = json.loads(self.request('/api/csrf')[0])['token']
        request = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
        try:
            response = self.opener.open(request, timeout=90)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read()
        assert response.status == expected, (method, path, response.status, expected, body[:600])
        return body, response.headers

    def json(self, path, method='GET', value=None, expected=200):
        body, _ = self.request('/api' + path, method, json.dumps(value, ensure_ascii=False).encode() if value is not None else None, expected=expected)
        return json.loads(body) if body else None

    def upload(self, name, content, expected=201, csrf=True):
        boundary = 'inkstone-file-test-boundary'
        data = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode()
                + content + f'\r\n--{boundary}--\r\n'.encode())
        body, _ = self.request('/api/documents/upload', 'POST', data, f'multipart/form-data; boundary={boundary}', expected, csrf)
        return json.loads(body) if body else None


with tempfile.TemporaryDirectory(prefix='inkstone-files-test-') as data, open(ROOT / 'Tests/file-api.log', 'w', encoding='utf-8') as log:
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
                    raise RuntimeError('API failed to start; see Tests/file-api.log')
                time.sleep(.25)
        alice, bob = Client(), Client()
        for client, name in [(alice, 'file-alice'), (bob, 'file-bob')]:
            client.json('/account/register', 'POST', {'email': name + '@example.test', 'password': 'FileTests123'})
        spec = json.loads(guest.request('/openapi/v1.json')[0])
        assert '/api/documents/upload' in spec['paths']
        assert '/api/documents/{id}/download' in spec['paths']
        assert 'multipart/form-data' in spec['paths']['/api/documents/upload']['post']['requestBody']['content']
        assert 'language' in spec['components']['schemas']['DocumentTitle']['properties']
        swagger, _ = guest.request('/swagger/index.html')
        assert b'Inkstone API' in swagger
        swagger_script, _ = guest.request('/swagger/index.js')
        assert b'X-CSRF-TOKEN' in swagger_script
        # Execute the package's actual initializer to verify that it can parse our interceptor.
        javascript = """
          const vm = require('node:vm'); const fs = require('node:fs');
          const assert = require('node:assert/strict'); let config;
          const bundle = value => { config = value; return { initOAuth() {} }; };
          bundle.presets = { apis: {} };
          const context = { URL, window: { location: { href: 'http://localhost/swagger/index.html' } },
            SwaggerUIBundle: bundle, SwaggerUIStandalonePreset: {},
            fetch: async (url, options) => { assert.equal(url, '/api/csrf'); assert.equal(options.credentials, 'same-origin'); return { ok: true, json: async () => ({ token: 'test-csrf' }) }; } };
          vm.runInNewContext(fs.readFileSync(0, 'utf8'), context); context.window.onload();
          (async () => {
            const request = await config.requestInterceptor({ method: 'POST', headers: {} });
            assert.equal(request.headers['X-CSRF-TOKEN'], 'test-csrf');
            assert.equal(request.credentials, 'same-origin');
            const get = await config.requestInterceptor({ method: 'GET', headers: {} });
            assert.equal(get.headers['X-CSRF-TOKEN'], undefined);
          })().catch(error => { console.error(error); process.exitCode = 1; });
        """
        check = subprocess.run(['node', '-e', javascript], input=swagger_script, capture_output=True, timeout=15)
        assert check.returncode == 0, check.stderr.decode()
        assert "'unsafe-eval'" in guest.request('/swagger/index.js')[1]['Content-Security-Policy']
        assert "'unsafe-eval'" not in guest.request('/api/health')[1]['Content-Security-Policy']
        alice.upload('blocked.txt', b'csrf required', expected=400, csrf=False)
        guest.upload('private.txt', b'authentication required', expected=401)
        alice.upload('bad.exe', b'unsupported', expected=415)
        alice.upload('empty.txt', b'', expected=413)
        alice.upload('invalid.docx', b'not a zip archive', expected=422)
        alice.upload('broken.json', b'{"notADocument":true}', expected=422)
        archive_buffer = io.BytesIO()
        with zipfile.ZipFile(archive_buffer, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('oversized.xml', b'0' * (129 * 1024 * 1024))
        alice.upload('oversized.docx', archive_buffer.getvalue(), expected=413)
        formats = alice.json('/documents/formats')
        assert 'fa-IR' in formats['languages'] and 'ur-PK' in formats['languages']
        # A 4 MiB Persian model must not exceed the limit because of Unicode re-escaping.
        large = alice.json('/documents/', 'POST', {'title': 'Large Persian draft'}, expected=201)
        large_model = {'body': [], 'testText': 'ی' * 2_000_000}
        saved = alice.json(f'/documents/{large["id"]}/content', 'PUT', {'version': 1, 'document': large_model})
        assert saved['size'] < 5 * 1024 * 1024
        assert alice.json(f'/documents/{large["id"]}/content') == large_model
        alice.json(f'/documents/{large["id"]}?version=2', 'DELETE', expected=204)
        for tag in ['fa-IR', 'ar-SA', 'ur-PK']:
            document = alice.json('/documents/', 'POST', {'title': 'سند ' + tag, 'language': tag}, expected=201)
            snapshot = alice.json(f'/documents/{document["id"]}/snapshot')
            model = snapshot['document']
            assert tag in json.dumps(model), (tag, 'language was not persisted')
            assert model['body'][0]['props']['bidi'] is True, model['body'][0]
        alice.json('/documents/', 'POST', {'title': 'Invalid language', 'language': 'invalid'}, expected=400)
        text = 'سلام دنیا — Persian پچژگ and English 123\nمرحبا بالعالم\nاردو زبان'
        uploaded = alice.upload('Persian mixed text.txt', text.encode())
        named = alice.upload('سند فارسی.txt', 'سلام'.encode())
        _, named_headers = alice.request(f'/api/documents/{named["id"]}/download?format=txt')
        assert 'filename*=UTF-8' in named_headers['Content-Disposition']
        assert urllib.parse.quote('سند فارسی.txt') in named_headers['Content-Disposition']
        identifier = uploaded['id']
        assert uploaded['title'] == 'Persian mixed text'
        assert text.splitlines()[0] in json.dumps(alice.json(f'/documents/{identifier}/content'), ensure_ascii=False)
        for format in ['json', 'txt', 'md', 'docx', 'pdf']:
            body, headers = alice.request(f'/api/documents/{identifier}/download?format={format}&version=1')
            assert body and 'attachment' in headers['Content-Disposition']
            assert f'.{format}' in headers['Content-Disposition']
            if format == 'docx':
                with zipfile.ZipFile(io.BytesIO(body)) as archive:
                    assert 'word/document.xml' in archive.namelist()
                roundtrip = alice.upload('roundtrip.docx', body)
                restored = alice.json(f'/documents/{roundtrip["id"]}/content')
                assert 'پچژگ' in json.dumps(restored, ensure_ascii=False)
            if format == 'pdf':
                assert body.startswith(b'%PDF-')
            if format == 'txt':
                assert 'پچژگ' in body.decode('utf-8-sig')
            bob.request(f'/api/documents/{identifier}/download?format={format}', expected=404)
            guest.request(f'/api/documents/{identifier}/download?format={format}', expected=401)
        alice.request(f'/api/documents/{identifier}/download?format=exe', expected=415)
        alice.request(f'/api/documents/{identifier}/download?version=999', expected=409)
        alice.json(f'/documents/{identifier}?version=1', 'DELETE', expected=204)
        alice.request(f'/api/documents/{identifier}/download', expected=404)
        print('PASS: standalone OpenAPI/Swagger, multipart CSRF, format validation, archive limits, Persian/Arabic/Urdu defaults, Unicode upload/download, DOCX roundtrip, PDF export and owner isolation.')
    finally:
        process.terminate()
        process.wait(timeout=20)

    environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    process = subprocess.Popen(['dotnet', str(ROOT / 'Back/bin/Debug/net10.0/Inkstone.Api.dll'), '--urls', BASE], cwd=ROOT / 'Back', env=environment, stdout=log, stderr=log)
    try:
        for _ in range(120):
            try:
                Client().json('/health')
                break
            except OSError:
                if process.poll() is not None:
                    raise RuntimeError('Production API failed to start')
                time.sleep(.25)
        guest = Client()
        guest.request('/swagger/index.html', expected=404)
        guest.request('/openapi/v1.json', expected=404)
        print('PASS: Swagger and OpenAPI are unavailable in Production.')
    finally:
        process.terminate()
        process.wait(timeout=20)
