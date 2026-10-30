"""Real cookie/CSRF, SQLite and Rust coverage for search, retention, collaboration and jobs."""
from contextlib import closing
import io
import json
import sqlite3
import time
import zipfile
from studio_test_support import Client, reference, server, template_model


def wait(client, job):
    for _ in range(240):
        job = client.json(f'/document-jobs/{job["id"]}?version={job["version"]}')
        if job['status'] in ('succeeded', 'failed', 'cancelled'):
            assert job['status'] == 'succeeded', job
            return job
        time.sleep(.25)
    raise AssertionError('Job timed out')


with server(5096, 'workspace-sequence') as (base, data):
    alice, bob, stranger = Client(base), Client(base), Client(base)
    alice.register('sequence-alice'); bob_user = bob.register('sequence-bob'); stranger.register('sequence-stranger')
    model = {'body': [{'kind': 'para', 'text': 'كتاب يكم — private body needle', 'runs': []}]}
    doc = alice.upload(model, 'Private source')
    search = '/library?q=' + __import__('urllib.parse', fromlist=['quote']).quote('کتاب یکم')
    assert alice.json(search)['total'] == 1
    assert bob.json(search)['total'] == 0
    for i in range(51): alice.json('/documents/', 'POST', {'title': f'Pagination {i:02d}'}, expected=201)
    assert len(alice.json('/library?q=Pagination')['items']) == 50
    assert len(alice.json('/library?q=Pagination&page=2')['items']) == 1
    grant = alice.json(f'/documents/{doc["id"]}/shares', 'PUT', {'version': doc['version'], 'email': 'sequence-bob@example.test', 'permission': 'read'})
    assert bob.json(search)['total'] == 1
    bob.json(f'/documents/{doc["id"]}/snapshot')
    for suffix in ['/versions?version=1', '/shares?version=1', '/audit?version=1']:
        bob.json(f'/documents/{doc["id"]}' + suffix, expected=404)
    bob.json(f'/documents/{doc["id"]}/content', 'PUT', {'version': 1, 'document': model}, expected=404)
    bob.json(f'/documents/{doc["id"]}', 'PATCH', {'version': 1, 'title': 'Not allowed'}, expected=404)
    bob.json(f'/documents/{doc["id"]}?version=1', 'DELETE', expected=404)
    bob.raw(f'/api/documents/{doc["id"]}/download?version=99&format=pdf', expected=409)
    pdf, _, _ = bob.raw(f'/api/documents/{doc["id"]}/download?version=1&format=pdf'); assert pdf.startswith(b'%PDF')
    alice.json(f'/documents/{doc["id"]}/shares', 'PUT', {'version': 1, 'email': 'sequence-bob@example.test', 'permission': 'edit', 'grantVersion': grant['version']})
    doc = bob.json(f'/documents/{doc["id"]}/content', 'PUT', {'version': 1, 'document': model})
    native = bob.json(f'/documents/{doc["id"]}/batch', 'POST', {'version': doc['version'], 'operations': [{'type': 'trackChanges', 'enabled': True}]})
    doc = native['metadata']
    review = alice.json(f'/documents/{doc["id"]}/reviews', 'POST', {'version': doc['version'], 'reviewerId': bob_user['id']})
    assert bob.json('/reviews/')['items'][0]['documentVersion'] == doc['version']
    stranger.json(f'/reviews/{review["id"]}/decision', 'POST', {'version': 1, 'documentVersion': doc['version'], 'decision': 'approved'}, expected=404)
    bob.json(f'/reviews/{review["id"]}/decision', 'POST', {'version': 1, 'documentVersion': doc['version'], 'decision': 'approved', 'note': 'Reviewed fixture'})
    doc = alice.json(f'/documents/{doc["id"]}', 'PATCH', {'version': doc['version'], 'title': 'Updated after review'})
    listed = bob.json('/reviews/')['items'][0]; assert listed['currentVersion'] != listed['documentVersion']
    old_pdf, _, _ = bob.raw(f'/api/reviews/{review["id"]}/download?version=2'); assert old_pdf.startswith(b'%PDF')
    job = alice.json('/document-jobs/', 'POST', {'kind': 'export', 'source': reference(doc), 'format': 'pdf', 'idempotencyKey': 'export-fixture'})
    repeated = alice.json('/document-jobs/', 'POST', {'kind': 'export', 'source': reference(doc), 'format': 'pdf', 'idempotencyKey': 'export-fixture'}); assert repeated['id'] == job['id']
    done = wait(alice, job); output, _, _ = alice.raw(done['downloadUrl']); assert output.startswith(b'%PDF')
    bob.json(f'/document-jobs/{done["id"]}?version={done["version"]}', expected=404)
    source = alice.upload(template_model(), 'Bulk template source')
    template = alice.json('/templates/', 'POST', {'title': 'Bulk fixture', 'kind': 'document', 'language': 'en-US', 'source': reference(source)}, expected=201)
    job = alice.json('/document-jobs/', 'POST', {'kind': 'renderBatch', 'template': reference(template), 'format': 'pdf', 'records': [{'key': 'a', 'title': 'One', 'values': {'CustomerName': 'Ada'}}, {'key': 'b', 'title': 'Two', 'values': {'CustomerName': 'علی'}}]})
    done = wait(alice, job); output, _, _ = alice.raw(done['downloadUrl'])
    with zipfile.ZipFile(io.BytesIO(output)) as archive:
        manifest = json.loads(archive.read('manifest.json')); assert len(manifest) == 2 and all(row['success'] for row in manifest)
        assert archive.read('document-1.pdf').startswith(b'%PDF')
    # Retention operates only on synthetic history deliberately aged in this disposable DB.
    history = alice.json('/documents/', 'POST', {'title': 'Retention fixture'}, expected=201)
    for _ in range(11): history = alice.json(f'/documents/{history["id"]}/content', 'PUT', {'version': history['version'], 'document': model})
    with closing(sqlite3.connect(data / 'inkstone.db')) as db:
        db.execute('UPDATE DocumentRevisions SET CreatedAt=0 WHERE UPPER(DocumentId)=?', (history['id'].upper(),)); db.commit()
    policy = alice.json('/retention/', 'PUT', {'version': 0, 'enabled': False, 'keepLatest': 5, 'days': 7})
    assert alice.json('/retention/preview', 'POST', {'version': policy['version']})['count'] == 6
    alice.json('/retention/apply', 'POST', {'version': policy['version']}, expected=409)
    policy = alice.json('/retention/', 'PUT', {'version': policy['version'], 'enabled': True, 'keepLatest': 5, 'days': 7})
    assert alice.json('/retention/apply', 'POST', {'version': policy['version']})['removed'] == 6
    assert len(alice.json(f'/documents/{history["id"]}/versions?version={history["version"]}')['items']) == 6
    alice.json(f'/documents/{doc["id"]}/shares/{bob_user["id"]}?version={doc["version"]}&grantVersion=2', 'DELETE', expected=204)
    bob.json(f'/documents/{doc["id"]}/snapshot', expected=404)
    bob.raw(f'/api/reviews/{review["id"]}/download?version=2', expected=404)
    assert bob.json(search)['total'] == 0 and not bob.json('/reviews/')['items']
    bob.json(f'/documents/{doc["id"]}/access', expected=404)
    # Regranting uses a fresh version and cannot revive a stale editor grant.
    grant = alice.json(f'/documents/{doc["id"]}/shares', 'PUT', {'version': doc['version'], 'email': 'sequence-bob@example.test', 'permission': 'read'})
    assert grant['version'] > 2
    assert bob.json(f'/documents/{doc["id"]}/access')['permission'] == 'read'
    alice.json(f'/documents/{doc["id"]}/shares/{bob_user["id"]}?version={doc["version"]}&grantVersion=2', 'DELETE', expected=409)
    bob.json(f'/documents/{doc["id"]}/content', 'PUT', {'version': doc['version'], 'document': model}, expected=404)
    alice.json(f'/documents/{doc["id"]}/shares/{bob_user["id"]}?version={doc["version"]}&grantVersion={grant["version"]}', 'DELETE', expected=204)
    assert bob.json(search)['total'] == 0
print('PASS: full-text search/pagination/privacy, reader/editor permissions, native shared editing, exact-version reviews, private export/bulk jobs, retry receipts and opt-in retention')
