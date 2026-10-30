"""Real HTTP integration checks, using isolated accounts and a disposable SQLite database.
Run: python Tests/api_smoke.py (build Back/Inkstone.Api.csproj first).
"""
import http.cookiejar
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:5089'

class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.token = None

    def request(self, path, method='GET', body=None, expected=200, csrf=True, account=None):
        if method != 'GET' and csrf and self.token is None:
            self.token = self.request('/csrf')['token']
        data = json.dumps(body).encode() if body is not None else None
        headers = {'Content-Type': 'application/json'}
        if account is not None: headers['X-Inkstone-Account'] = account
        if csrf and self.token:
            headers['X-CSRF-TOKEN'] = self.token
        req = urllib.request.Request(BASE + '/api' + path, data=data, headers=headers, method=method)
        try:
            response = self.opener.open(req, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        raw = response.read()
        allowed = expected if isinstance(expected, tuple) else (expected,)
        assert response.status in allowed, (method, path, response.status, expected, raw[:500])
        return json.loads(raw) if raw else None

    def account(self, email):
        self.request('/account/register', 'POST', {'email': email, 'password': 'SamplePass123'})
        self.token = None  # Anti-forgery tokens are bound to the authenticated identity.

def start(directory, log):
    env = {**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development', 'Storage__DataDirectory': directory}
    process = subprocess.Popen(['dotnet', str(ROOT / 'Back/bin/Debug/net10.0/Inkstone.Api.dll'), '--urls', BASE], cwd=ROOT / 'Back', env=env, stdout=log, stderr=log)
    for _ in range(120):
        try:
            Client().request('/health')
            return process
        except (OSError, AssertionError):
            if process.poll() is not None:
                raise RuntimeError('API exited during startup; inspect Tests/api-test.log')
            time.sleep(.5)
    process.terminate()
    raise RuntimeError('API startup timed out')

def stop(process):
    process.terminate()
    process.wait(timeout=20)

with tempfile.TemporaryDirectory(prefix='inkstone-tests-') as data, open(ROOT / 'Tests/api-test.log', 'w', encoding='utf-8') as log:
    process = start(data, log)
    try:
        guest, alice, bob = Client(), Client(), Client()
        guest.request('/documents/', expected=401)
        guest.request('/account/register', 'POST', {'email': 'csrf-bypass@example.test', 'password': 'ValidPass123'}, expected=400, csrf=False)
        guest.request('/account/register', 'POST', {'email': 'invalid', 'password': 'short'}, expected=400)
        alice.account('alice@example.test')
        bob.account('bob@example.test')
        alice.request('/documents/', 'POST', {'title': 'Wrong account'}, expected=403, account='bob@example.test')
        assert alice.request('/documents/', account='alice@example.test') == []
        guest.request('/documents/', expected=401, account='alice@example.test')
        alice.request('/documents/', 'POST', {'title': '   '}, expected=400)
        document = alice.request('/documents/', 'POST', {'title': 'First private draft'}, expected=201)
        identifier = document['id']
        alice.request(f'/documents/{identifier}/content', expected=204)
        assert bob.request('/documents/') == []
        for suffix in ['', '/content', '/snapshot']:
            bob.request(f'/documents/{identifier}{suffix}', expected=404)
        bob.request(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': {'body': []}}, expected=404)
        bob.request(f'/documents/{identifier}', 'PATCH', {'version': 1, 'title': 'Stolen'}, expected=404)
        bob.request(f'/documents/{identifier}?version=1', 'DELETE', expected=404)
        alice.request(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': {'bad': True}}, expected=400)
        alice.request(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': {'body': [{'kind': 'not-a-native-block', 'text': 'Invalid'}]}}, expected=422)
        assert alice.request(f'/documents/{identifier}')['version'] == 1
        alice.request(f'/documents/{identifier}/content', expected=204)
        payload = {'body': [], 'testText': 'A persistent draft — سلام'}
        saved = alice.request(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': payload})
        assert saved['version'] == 2
        assert alice.request(f'/documents/{identifier}/content') == payload
        assert alice.request(f'/documents/{identifier}/snapshot')['document'] == payload
        alice.request(f'/documents/{identifier}/content', 'PUT', {'version': 1, 'document': payload}, expected=409)
        alice.request(f'/documents/{identifier}', 'PATCH', {'version': 1, 'title': 'Stale rename'}, expected=409)
        renamed = alice.request(f'/documents/{identifier}', 'PATCH', {'version': 2, 'title': 'Renamed draft'})
        assert renamed['title'] == 'Renamed draft' and renamed['version'] == 3
        alice.request(f'/documents/{identifier}?version=2', 'DELETE', expected=409)
        with ThreadPoolExecutor(max_workers=2) as pool:
            attempts = list(pool.map(lambda text: alice.request(f'/documents/{identifier}/content', 'PUT', {'version': 3, 'document': {'body': [], 'testText': text}}, expected=(200, 409)), ['Concurrent A', 'Concurrent B']))
        assert sum('version' in result for result in attempts) == 1, 'exactly one simultaneous writer succeeds'
        payload = alice.request(f'/documents/{identifier}/content')
        assert alice.request(f'/documents/{identifier}')['version'] == 4
        alice.request('/account/logout', 'POST', expected=204)
        alice.request('/documents/', expected=401)
        alice.token = None
        alice.request('/account/login', 'POST', {'email': 'alice@example.test', 'password': 'WrongPass123'}, expected=401)
        alice.request('/account/login', 'POST', {'email': 'alice@example.test', 'password': 'SamplePass123'})
        alice.token = None
        assert len(alice.request('/documents/')) == 1
        guest.request('/nonexistent', expected=404)
        stop(process)
        process = start(data, log)
        # Persisted identity cookie remains decryptable because data-protection keys are persisted too.
        assert alice.request(f'/documents/{identifier}/content') == payload
        alice.request(f'/documents/{identifier}?version=4', 'DELETE', expected=204)
        alice.request(f'/documents/{identifier}', expected=404)
        assert alice.request('/documents/') == []
        print('PASS: account lifecycle, CSRF, tenant isolation on every route, validation, saving, rename, conflict protection, deletion, and persistence across API restart.')
    finally:
        if process.poll() is None:
            stop(process)
