"""Disposable HTTP test host and original fixtures for templates/letters. No live services."""
import contextlib
import http.cookiejar
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[1]


def png():
    def chunk(tag, data):
        return struct.pack('!I', len(data)) + tag + data + struct.pack('!I', zlib.crc32(tag + data) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', 2, 1, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\0\0\0\0\xff\xff\xff')) + chunk(b'IEND', b'')


def template_model(names=('CustomerName', 'Body')):
    return {'body': [{'kind': 'para', 'text': f'Before \ufffcold {name}\ufffc after',
                     'runs': [{'len': len(f'Before \ufffcold {name}\ufffc after'.encode()), 'props': {'bold': True}}],
                     'objects': [{'type': 'bookmarkStart', 'name': name}, {'type': 'bookmarkEnd', 'name': name}]}
                    for name in names]}


class Client:
    def __init__(self, base):
        self.base = base
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def raw(self, path, method='GET', body=None, mime='application/json', expected=200, csrf=True):
        headers = {'Content-Type': mime} if body is not None else {}
        if method not in ('GET', 'HEAD') and csrf:
            headers['X-CSRF-TOKEN'] = self.json('/csrf')['token']
        try:
            response = self.opener.open(urllib.request.Request(self.base + path, data=body, method=method, headers=headers), timeout=240)
        except urllib.error.HTTPError as e:
            response = e
        content = response.read()
        assert response.status in ((expected,) if isinstance(expected, int) else expected), (method, path, response.status, expected, content[:1200])
        return content, response.status, response.headers

    def json(self, path, method='GET', value=None, expected=200, csrf=True):
        body = json.dumps(value, ensure_ascii=False).encode() if value is not None else None
        content, _, _ = self.raw('/api' + path, method, body, expected=expected, csrf=csrf)
        return json.loads(content) if content else None

    def register(self, name):
        self.json('/account/register', 'POST', {'email': name + '@example.test', 'password': 'StudioTests123'})
        return next(u for u in self.json('/letters/directory?search=' + name + '@example.test')['items'] if u['email'] == name + '@example.test')

    def multipart(self, path, name, data, fields=None, expected=201, csrf=True):
        boundary = 'inkstone-studio-fixture'
        body = b''
        for key, value in (fields or {}).items():
            body += f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode()
        body += f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n'.encode() + data + f'\r\n--{boundary}--\r\n'.encode()
        result, _, _ = self.raw('/api' + path, 'POST', body, 'multipart/form-data; boundary=' + boundary, expected, csrf)
        return json.loads(result) if result else None

    def upload(self, model, title='Fixture'):
        return self.multipart('/documents/upload', title + '.json', json.dumps(model, ensure_ascii=False).encode())


@contextlib.contextmanager
def server(port, label):
    with tempfile.TemporaryDirectory(prefix='inkstone-' + label + '-') as data, open(ROOT / 'Tests' / (label + '.log'), 'w', encoding='utf-8') as log:
        base = f'http://127.0.0.1:{port}'
        environment = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__DataDirectory': data, 'BootstrapAdmin__File': str(Path(data) / 'no-bootstrap.json')}
        api_dll = os.environ.get('INKSTONE_TEST_API_DLL', str(ROOT / 'Back/bin/Debug/net10.0/Inkstone.Api.dll'))
        process = subprocess.Popen(['dotnet', api_dll, '--urls', base], cwd=ROOT / 'Back', env=environment, stdout=log, stderr=log)
        try:
            for _ in range(120):
                try:
                    Client(base).json('/health')
                    break
                except OSError:
                    if process.poll() is not None:
                        raise RuntimeError('Test host failed; see Tests/' + label + '.log')
                    time.sleep(.25)
            yield base, Path(data)
        finally:
            process.terminate()
            process.wait(timeout=20)


def reference(item):
    metadata = item.get('metadata', item)
    return {'id': metadata['id'], 'version': metadata['version']}


def docx_text(content):
    import xml.etree.ElementTree as ET
    ns = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        return '\n'.join(''.join(node.text or '' for node in ET.fromstring(archive.read(name)).iter(ns + 't'))
                         for name in archive.namelist() if name.startswith('word/') and name.endswith('.xml'))
