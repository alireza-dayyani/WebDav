"""Ordinary template creation/rendering remains independent of the Letters workflow."""
import json
from studio_test_support import Client, ROOT, docx_text, reference, server, template_model

with server(5097, 'template-api') as (base, _):
    alice, bob, guest = Client(base), Client(base), Client(base)
    alice.register('template-alice'); bob.register('template-bob')
    spec = json.loads(guest.raw('/openapi/v1.json')[0])
    paths = {route.rstrip('/') for route in spec['paths']}
    for route in ['/api/templates/{id}/render', '/api/letters/{id}/generate', '/api/letters/directory', '/api/letters/senders/', '/api/letters/images/']:
        assert route.rstrip('/') in paths, route
    assert 'LetterDraft' in spec['components']['schemas'] and 'GenerateLetter' in spec['components']['schemas']
    alice.raw('/api/templates/starter', 'POST', b'{"title":"' + b'x' * (4 * 1024 * 1024) + b'"}', expected=413)
    source = alice.upload(template_model(), 'Template source')
    payload = {'title': 'Reusable document', 'kind': 'document', 'language': 'fa-IR', 'source': reference(source)}
    template = alice.json('/templates/', 'POST', payload, expected=201)
    identifier = template['metadata']['id']
    for client, status in [(bob, 404), (guest, 401)]:
        client.json(f'/templates/{identifier}?version=1', expected=status)
        client.json(f'/templates/{identifier}/bookmarks?version=1', expected=status)
        client.json(f'/templates/{identifier}/download?version=1', expected=status)
        client.json(f'/templates/{identifier}/render', 'POST', {'title': 'stolen', 'version': 1, 'values': {}}, expected=status)
    bob.json('/templates/', 'POST', payload, expected=404)
    alice.json(f'/templates/{identifier}/render', 'POST', {'title': 'blocked', 'version': 1, 'values': {}}, expected=400, csrf=False)
    alice.json(f'/templates/{identifier}/render', 'POST', {'title': 'stale', 'version': 99, 'values': {}}, expected=409)
    assert len(alice.json(f'/templates/{identifier}/bookmarks?version=1')['bookmarks']) == 2
    first = alice.json(f'/templates/{identifier}/render', 'POST', {'title': 'Letter-free first copy', 'version': 1, 'values': {'CustomerName': 'مشتری اول'}}, expected=201)
    second = alice.json(f'/templates/{identifier}/render', 'POST', {'title': 'Second copy', 'version': 1, 'values': {'CustomerName': 'Second customer'}}, expected=201)
    assert first['id'] != second['id'] != source['id']
    assert 'مشتری اول' in json.dumps(alice.json(f'/documents/{first["id"]}/content'), ensure_ascii=False)
    assert 'Second customer' in json.dumps(alice.json(f'/documents/{second["id"]}/content'))
    template_bytes = alice.raw(f'/api/templates/{identifier}/download?version=1&format=json')[0]
    assert 'old CustomerName' in template_bytes.decode() and 'مشتری اول' not in template_bytes.decode()
    # Source edits/deletion cannot change the retained template snapshot.
    alice.json(f'/documents/{source["id"]}/content', 'PUT', {'version': 1, 'document': {'body': []}})
    alice.json(f'/documents/{source["id"]}?version=2', 'DELETE', expected=204)
    assert alice.raw(f'/api/templates/{identifier}/download?version=1&format=json')[0] == template_bytes
    for generated in [first, second]:
        content = alice.raw(f'/api/documents/{generated["id"]}/download?version=1&format=docx')[0]
        assert content.startswith(b'PK')
        out = ROOT / 'Tests/generated'; out.mkdir(exist_ok=True)
        (out / (generated['title'] + '.docx')).write_bytes(content)
    assert alice.json('/letters/')['items'] == []
    before = len(alice.json('/documents/'))
    alice.json(f'/templates/{identifier}/render', 'POST', {'title': 'bad batch', 'version': 1, 'values': {'CustomerName': 'new', 'Unknown': 'refused'}}, expected=422)
    assert len(alice.json('/documents/')) == before
    updated = alice.json(f'/templates/{identifier}', 'PUT', {'version': 1, 'title': 'Renamed template', 'fields': {}, 'requiredFields': []})
    assert updated['metadata']['version'] == 2
    alice.json(f'/templates/{identifier}/download?version=1', expected=409)
    bob.json(f'/templates/{identifier}?version=2', 'DELETE', expected=404)
    alice.json(f'/templates/{identifier}?version=1', 'DELETE', expected=409)
    alice.json(f'/templates/{identifier}?version=2', 'DELETE', expected=204)
    assert alice.json(f'/documents/{first["id"]}')['version'] == 1
    starter = alice.json('/templates/starter', 'POST', {'title': 'Blank reusable template', 'kind': 'document', 'language': 'en-US'}, expected=201)
    alice.json(f'/templates/{starter["metadata"]["id"]}/render', 'POST', {'title': 'Blank copy', 'version': 1, 'values': {}}, expected=201)
    print('PASS: private template snapshots, independent copies, ordinary document editing/download, source deletion, atomic failure, CSRF, tenant isolation and revision checks.')
