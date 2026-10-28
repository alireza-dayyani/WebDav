"""Letter lifecycle, registered recipients, signatures, rich merge and immutable outputs."""
import base64
import concurrent.futures
import io
import json
import uuid
import zipfile
from studio_test_support import Client, ROOT, docx_text, png, reference, server


def draft(template=None, sender=None, signature=None, **changes):
    value = {'title': 'نامه آزمایشی', 'subject': 'موضوع نامه', 'language': 'fa-IR', 'date': '2026-10-26',
             'template': reference(template) if template else None, 'sender': reference(sender) if sender else None,
             'signature': reference(signature) if signature else None, 'logo': None, 'bodyDocument': None, 'bodyText': '',
             'recipients': [], 'attachments': [], 'number': '', 'priority': 'urgent', 'classification': 'confidential',
             'includeQrCode': True, 'copyVariants': True}
    return {**value, **changes}


with server(5098, 'letter-api') as (base, _):
    alice, bob, carol, guest = [Client(base) for _ in range(4)]
    alice_user = alice.register('letter-alice'); bob_user = bob.register('letter-bob'); carol_user = carol.register('letter-carol')
    directory = alice.json('/letters/directory')['items']
    assert all(set(user) == {'id', 'email', 'name'} for user in directory)
    assert {u['id'] for u in directory} == {alice_user['id'], bob_user['id'], carol_user['id']}
    guest.json('/letters/directory', expected=401)
    profile = {'version': 0, 'fullName': 'علی آزمایشی', 'title': 'مدیر', 'organization': 'سازمان نمونه', 'address': 'تهران'}
    sender = alice.json('/letters/senders/', 'POST', profile, expected=201)
    sender_id = sender['metadata']['id']
    alice.json('/letters/senders/', 'POST', {**profile, 'fullName': '\ufffc'}, expected=400)
    bob.json(f'/letters/senders/{sender_id}?version=1', expected=404)
    image = alice.multipart('/letters/images/', 'signature.png', png(), {'title': 'Original test signature'})
    image_id = image['metadata']['id']
    assert image['data']['width'] == 2
    bob.raw(f'/api/letters/images/{image_id}?version=1', expected=404)
    guest.raw(f'/api/letters/images/{image_id}?version=1', expected=401)
    alice.raw(f'/api/letters/images/{image_id}?version=99', expected=409)
    alice.multipart('/letters/images/', 'fake.png', b'not an image', {'title': 'bad'}, expected=415)
    alice.multipart('/letters/images/', 'bad.png', b'\x89PNG\r\n\x1a\ninvalid', {'title': 'bad'}, expected=422)
    alice.multipart('/letters/images/', 'large.png', b'x' * (512 * 1024 + 1), {'title': 'too large'}, expected=413)
    alice.multipart('/letters/images/', 'csrf.png', png(), {'title': 'csrf'}, expected=400, csrf=False)
    template = alice.json('/templates/starter', 'POST', {'title': 'قالب نامه', 'kind': 'letter', 'language': 'fa-IR'}, expected=201)
    template_id = template['metadata']['id']
    alice.json(f'/templates/{template_id}/render', 'POST', {'title': 'not ordinary', 'version': 1, 'values': {}}, expected=422)
    unfinished = alice.json('/letters/', 'POST', draft(), expected=201)
    unfinished_id = unfinished['metadata']['id']
    report = alice.json(f'/letters/{unfinished_id}/validate', 'POST', {'version': 1})
    assert report['valid'] is False and any(x['field'] == 'signature' for x in report['issues'])
    alice.json(f'/letters/{unfinished_id}/generate', 'POST', {'version': 1, 'idempotencyKey': str(uuid.uuid4())}, expected=422)
    assert alice.json(f'/letters/{unfinished_id}/artifacts?version=1') == []
    # Original rich-body fixture, including a table and an embedded image.
    body_text = 'متن فارسی با تصویر '
    source = alice.upload({'body': [
        {'kind': 'para', 'text': body_text + '\ufffc', 'runs': [{'len': len((body_text + '\ufffc').encode())}], 'props': {'style': 'Normal', 'bidi': True},
         'objects': [{'type': 'image', 'media': 'source.png', 'w': 80, 'h': 40}]},
        {'kind': 'table', 'grid': [120], 'rows': [{'cells': [{'blocks': [{'kind': 'para', 'text': 'جدول متن', 'runs': [{'len': len('جدول متن'.encode())}]}]}]}]}
        ], 'media': {'source.png': base64.b64encode(png()).decode()}}, 'Rich letter body')
    # Force a real Normal-style collision; the imported source must keep its bold formatting.
    model = alice.json(f'/documents/{source["id"]}/content')
    normal = next(s for s in model['styles']['styles'] if s['id'] == 'Normal')
    normal.setdefault('chr', {})['bold'] = True
    source = alice.json(f'/documents/{source["id"]}/content', 'PUT', {'version': source['version'], 'document': model})
    attachment = alice.multipart('/documents/upload', 'attachment.txt', b'Original attachment snapshot')
    recipients = [{'userId': bob_user['id'], 'kind': 'to', 'printOnLetter': True},
                  {'userId': carol_user['id'], 'kind': 'cc', 'printOnLetter': False},
                  {'userId': alice_user['id'], 'kind': 'cc', 'printOnLetter': True}]
    value = draft(template, sender, image, bodyDocument=reference(source), recipients=recipients, attachments=[reference(attachment)], logo=reference(image))
    alice.json('/letters/', 'POST', {**value, 'number': '\ufffc'}, expected=400)
    ancient = alice.json('/letters/', 'POST', {**value, 'date': '0001-01-01'}, expected=201)
    assert any(x['code'] == 'date' for x in alice.json(f'/letters/{ancient["metadata"]["id"]}/validate', 'POST', {'version': 1})['issues'])
    letter = alice.json('/letters/', 'POST', value, expected=201)
    letter_id = letter['metadata']['id']; path = f'/letters/{letter_id}'
    for client, status in [(bob, 404), (guest, 401)]:
        client.json(path + '?version=1', expected=status)
        client.json(path + '/validate', 'POST', {'version': 1}, expected=status)
        client.json(path + '/preview', 'POST', {'version': 1}, expected=status)
        client.json(path + '/generate', 'POST', {'version': 1, 'idempotencyKey': str(uuid.uuid4())}, expected=status)
        client.json(path, 'PUT', {'version': 1, 'draft': value}, expected=status)
        client.json(path + '?version=1', 'DELETE', expected=status)
    alice.json(path + '/validate', 'POST', {'version': 1}, expected=400, csrf=False)
    alice.json(path + '/validate', 'POST', {'version': 99}, expected=409)
    assert alice.json(path + '/validate', 'POST', {'version': 1})['valid'] is True
    # Referencing a different user's resources never makes them readable or usable.
    bob_image = bob.multipart('/letters/images/', 'bob.png', png(), {'title': 'Bob private signature'})
    forbidden = alice.json('/letters/', 'POST', {**value, 'signature': reference(bob_image)}, expected=201)
    forbidden_report = alice.json(f'/letters/{forbidden["metadata"]["id"]}/validate', 'POST', {'version': 1})
    assert any(x['code'] == 'resource_missing' and x['field'] == 'signature' for x in forbidden_report['issues'])
    stale = alice.json('/letters/', 'POST', {**value, 'sender': {'id': sender_id, 'version': 99}}, expected=201)
    stale_report = alice.json(f'/letters/{stale["metadata"]["id"]}/validate', 'POST', {'version': 1})
    assert any(x['code'] == 'resource_changed' for x in stale_report['issues'])
    alice.json('/letters/', 'POST', {**value, 'recipients': [recipients[0], recipients[0]]}, expected=400)
    alice.json('/letters/', 'POST', {**value, 'bodyText': 'x' * (480 * 1024 + 1)}, expected=400)
    # Actual revision marks are refused even with Track Changes disabled.
    reviewed_model = {'body': [{'kind': 'para', 'text': 'Pending', 'runs': [{'len': 7, 'props': {'ins': 0}}]}],
                      'revisions': [{'kind': 'insert', 'author': 'Fixture author', 'date': '2026-10-26T00:00:00Z'}]}
    reviewed = alice.upload(reviewed_model, 'Unresolved revision')
    reviewed_letter = alice.json('/letters/', 'POST', {**value, 'bodyDocument': reference(reviewed)}, expected=201)
    reviewed_path = f'/letters/{reviewed_letter["metadata"]["id"]}'
    reviewed_report = alice.json(reviewed_path + '/validate', 'POST', {'version': 1})
    assert any(x['code'] == 'review' for x in reviewed_report['issues']), reviewed_report
    alice.json(reviewed_path + '/generate', 'POST', {'version': 1, 'idempotencyKey': str(uuid.uuid4())}, expected=422)
    assert alice.json(reviewed_path + '/artifacts?version=1') == []
    alice.json(f'/documents/{reviewed["id"]}?version=1', 'DELETE', expected=204)
    alice.json(path + '/preview', 'POST', {'version': 99}, expected=409)
    alice.json(path + '/preview', 'POST', {'version': 1}, expected=400, csrf=False)
    preview, _, preview_headers = alice.raw('/api' + path + '/preview', 'POST', json.dumps({'version': 1}).encode())
    assert preview.startswith(b'%PDF-')
    assert preview_headers['Content-Type'] == 'application/pdf' and preview_headers['Content-Disposition'] == 'inline'
    assert alice.json(path + '/artifacts?version=1') == [], 'preview must not finalize'
    key = str(uuid.uuid4())
    result = alice.json(path + '/generate', 'POST', {'version': 1, 'idempotencyKey': key})
    assert result['metadata']['state'] == 'generated' and result['metadata']['version'] == 2
    assert len(result['artifacts']) == 5, result
    frozen_preview, _, frozen_headers = alice.raw('/api' + path + '/preview', 'POST', json.dumps({'version': 2}).encode())
    assert frozen_preview.startswith(b'%PDF-') and frozen_headers['Content-Disposition'] == 'inline'
    replay = alice.json(path + '/generate', 'POST', {'version': 1, 'idempotencyKey': key})
    assert {a['id'] for a in replay['artifacts']} == {a['id'] for a in result['artifacts']}
    alice.json(path, 'PUT', {'version': 2, 'draft': value}, expected=409)
    alice.json(path + '/generate', 'POST', {'version': 2, 'idempotencyKey': str(uuid.uuid4())}, expected=409)
    assert len(alice.json('/documents/')) == 2, 'letters must not become ordinary documents'
    racing = alice.json('/letters/', 'POST', {**value, 'title': 'Concurrent generation', 'bodyDocument': None, 'bodyText': 'Atomic generation test', 'copyVariants': False, 'attachments': [], 'logo': None}, expected=201)
    racing_id = racing['metadata']['id']
    def generate_race(_):
        payload = json.dumps({'version': 1, 'idempotencyKey': str(uuid.uuid4())}).encode()
        return alice.raw(f'/api/letters/{racing_id}/generate', 'POST', payload, expected=(200, 409))[1]
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        assert sorted(pool.map(generate_race, range(2))) == [200, 409]
    assert len(alice.json(f'/letters/{racing_id}/artifacts?version=2')) == 1
    output = ROOT / 'Tests/generated'; output.mkdir(exist_ok=True)
    for artifact in result['artifacts']:
        route = f'/api{path}/artifacts/{artifact["id"]}/download?version=2'
        content = alice.raw(route)[0]
        text = docx_text(content)
        (output / (artifact['key'] + '.docx')).write_bytes(content)
        pdf, _, download_headers = alice.raw(route + '&format=pdf')
        assert download_headers['Content-Disposition'].startswith('attachment;')
        assert pdf.startswith(b'%PDF-'); (output / (artifact['key'] + '.pdf')).write_bytes(pdf)
        frozen = json.loads(alice.raw(route + '&format=json')[0])
        if artifact['key'] == 'primary':
            assert body_text.strip() in text and 'جدول متن' in text and 'علی آزمایشی' in text and 'مدیر' in text
            assert bob_user['email'] in text and alice_user['email'] in text and carol_user['email'] not in text
            story_blocks = frozen['body'] + [block for part in frozen.get('parts', {}).values() for block in part.get('blocks', [])]
            image_refs = [obj['media'] for block in story_blocks for obj in block.get('objects', []) if obj['type'] == 'image']
            assert len(image_refs) >= 4 and all(key in frozen['media'] for key in image_refs), 'rich-body images must be adopted'
            assert any(s['id'].startswith('Imported_') and s.get('chr', {}).get('bold') for s in frozen['styles']['styles']), 'source style collision was not preserved'
            assert any(block.get('props', {}).get('style', '').startswith('Imported_') for block in frozen['body'] if body_text.strip() in block.get('text', '')), 'the first imported paragraph must retain its source style'
            with zipfile.ZipFile(io.BytesIO(content)) as archive:
                assert len([name for name in archive.namelist() if name.startswith('word/media/')]) >= 2
        elif artifact['key'] == 'without-copies':
            assert carol_user['email'] not in text and alice_user['email'] not in text
            assert 'رونوشت:' not in text, 'a no-copy variant must not retain an empty copies heading'
        elif artifact['key'] == 'copy-1':
            assert carol_user['email'] in text and alice_user['email'] not in text
        elif artifact['key'].startswith('attachment-'):
            assert 'Original attachment snapshot' in text
        for client, status in [(bob, 404), (carol, 404), (guest, 401)]:
            client.raw(route, expected=status)
        alice.raw(route.replace('version=2', 'version=1'), expected=409)
        alice.raw(route + '&format=exe', expected=415)
    # Deleting source resources cannot change the generated files or audit snapshot.
    primary = next(a for a in result['artifacts'] if a['key'] == 'primary')
    primary_route = f'/api{path}/artifacts/{primary["id"]}/download?version=2'
    original = alice.raw(primary_route)[0]
    alice.json(f'/letters/images/{image_id}?version=1', 'DELETE', expected=204)
    alice.json(f'/templates/{template_id}?version=1', 'DELETE', expected=204)
    alice.json(f'/documents/{source["id"]}?version={source["version"]}', 'DELETE', expected=204)
    assert alice.raw(primary_route)[0] == original
    snapshot = alice.json(path + '?version=2')['snapshot']
    assert len(snapshot['signature']['hash']) == 64 and len(snapshot['recipients']) == 3
    alice.json(f'/letters/senders/{sender_id}', 'PUT', {**profile, 'version': 1, 'fullName': 'Changed later'})
    assert alice.json(path + '?version=2')['snapshot']['sender']['profile']['fullName'] == profile['fullName']
    duplicate = alice.json(path + '/duplicate', 'POST', {'version': 2, 'title': 'Next draft'}, expected=201)
    assert duplicate['metadata']['state'] == 'draft'
    duplicate_report = alice.json(f'/letters/{duplicate["metadata"]["id"]}/validate', 'POST', {'version': 1})
    assert not duplicate_report['valid'] and any(x['code'] == 'resource_missing' for x in duplicate_report['issues'])
    alice.json(path + '?version=2', 'DELETE', expected=204)
    alice.raw(primary_route, expected=404)
    print('PASS: separate letter drafts, registered recipient directory, private sender/signature assets, preflight, rich media/styles/tables, previews, atomic finalization, retry idempotency, recipient PDF variants, frozen attachments, snapshot retention, ownership and version checks.')
