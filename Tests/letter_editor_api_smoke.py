"""Editable private letter template copies and authoritative UI/API field filling."""
import copy
import base64
import json
import uuid
import concurrent.futures
from studio_test_support import Client, ROOT, docx_text, png, reference, server


def paragraph(text, objects=None):
    return {'kind': 'para', 'text': text, 'runs': [{'len': len(text.encode()), 'props': {'bold': True}}],
            'props': {'bidi': True}, 'objects': objects or []}


with server(5094, 'letter-editor-api') as (base, _):
    alice, bob, guest = Client(base), Client(base), Client(base)
    alice.register('editor-alice'); recipient = bob.register('editor-bob')
    template = alice.json('/templates/starter', 'POST', {'title': 'Editable Persian letter', 'kind': 'letter', 'language': 'fa-IR'}, expected=201)
    template_id = template['metadata']['id']
    template_before = alice.raw(f'/api/templates/{template_id}/download?version=1&format=json')[0]
    draft = {'title': 'Letter written in template', 'subject': '', 'language': 'fa-IR', 'date': '2026-10-26',
             'template': reference(template), 'sender': None, 'signature': None, 'logo': None, 'bodyDocument': None,
             'bodyText': '', 'recipients': [], 'attachments': [], 'includeQrCode': False}
    letter = alice.json('/letters/', 'POST', draft, expected=201)
    path = f'/letters/{letter["metadata"]["id"]}'
    opened = alice.json(path + '/editor', 'POST', {'version': 1})
    assert opened['metadata']['hasEditor'] and opened['metadata']['version'] == 2
    assert alice.json(path + '/editor', 'POST', {'version': 2})['metadata']['version'] == 2, 'reopening must not overwrite edits'
    snapshot = alice.json(path + '/editor?version=2')
    assert snapshot['bodyBookmark'] == 'Body'
    assert alice.json('/letters/')['items'][0]['metadata']['hasEditor'] is True
    for client, code in [(bob, 404), (guest, 401)]:
        client.json(path + '/editor?version=2', expected=code)
        client.json(path + '/editor', 'POST', {'version': 2}, expected=code)
        client.json(path + '/editor', 'PUT', {'version': 2, 'document': snapshot['document']}, expected=code)
        client.json(path + '/editor/fields', 'POST', {'version': 2}, expected=code)
        client.raw('/api' + path + '/editor/download?version=2', expected=code)
    alice.json(path + '/editor', 'PUT', {'version': 2, 'document': snapshot['document']}, expected=400, csrf=False)
    alice.json(path + '/editor', 'PUT', {'version': 99, 'document': snapshot['document']}, expected=409)
    alice.json(path + '/editor', 'PUT', {'version': 2}, expected=400)
    alice.json(path + '/editor', 'PUT', {'version': 2, 'document': None}, expected=400)
    model = copy.deepcopy(snapshot['document'])
    body_index = next(i for i, block in enumerate(model['body']) if any(o.get('type') == 'bookmarkStart' and o.get('name') == 'Body' for o in block.get('objects', [])))
    model['body'][body_index:body_index+1] = [
        paragraph('\ufffcمتن نوشته‌شده در قالب', [{'type': 'bookmarkStart', 'name': 'Body'}]),
        {'kind': 'table', 'grid': [160], 'rows': [{'cells': [{'blocks': [paragraph('Rich table content')]}]}]},
        paragraph('\ufffc', [{'type': 'bookmarkEnd', 'name': 'Body'}])]
    sender_block = next(b for b in model['body'] if any(o.get('name') == 'SenderName' for o in b.get('objects', [])))
    sender_block['text'] = '\ufffcForged sender\ufffc'; sender_block['runs'] = [{'len': len(sender_block['text'].encode())}]
    saved = alice.json(path + '/editor', 'PUT', {'version': 2, 'document': model})
    assert saved['version'] == 3
    assert alice.raw(f'/api/templates/{template_id}/download?version=1&format=json')[0] == template_before
    assert alice.json('/documents/') == [], 'working letters never enter the ordinary library'
    sender = alice.json('/letters/senders/', 'POST', {'version': 0, 'fullName': 'Verified sender', 'title': 'Project director', 'organization': 'Example team', 'address': ''}, expected=201)
    image = alice.multipart('/letters/images/', 'signature.png', png(), {'title': 'Synthetic signature'})
    draft.update(subject='Written letter', sender=reference(sender), signature=reference(image), recipients=[{'userId': recipient['id'], 'kind': 'to', 'printOnLetter': True}])
    alice.json(path, 'PUT', {'version': 3, 'draft': draft})
    alice.json(path, 'PUT', {'version': 4, 'draft': {**draft, 'bodyText': 'silent overwrite'}}, expected=409)
    filled = alice.json(path + '/editor/fields', 'POST', {'version': 4})
    assert filled['metadata']['version'] == 5
    working = alice.json(path + '/editor?version=5')['document']
    assert 'متن نوشته‌شده در قالب' in json.dumps(working, ensure_ascii=False)
    assert 'Verified sender' in json.dumps(working) and 'Forged sender' not in json.dumps(working)
    signature_block = next(b for b in working['body'] if any(o.get('name') == 'Signature' for o in b.get('objects', [])))
    signature_image = next(o for o in signature_block['objects'] if o.get('type') == 'image')
    assert base64.b64decode(working['media'][signature_image['media']]) == png(), 'the selected private signature must fill its bookmark'
    assert alice.json(path + '/validate', 'POST', {'version': 5})['valid'] is True
    preview = alice.raw('/api' + path + '/preview', 'POST', json.dumps({'version': 5}).encode())[0]
    assert preview.startswith(b'%PDF-')
    # Even direct native-model edits cannot override identity fields in finalized output.
    changed = copy.deepcopy(working)
    name = next(b for b in changed['body'] if any(o.get('name') == 'SenderName' for o in b.get('objects', [])))
    name['text'] = '\ufffcForged again\ufffc'; name['runs'] = [{'len': len(name['text'].encode())}]
    alice.json(path + '/editor', 'PUT', {'version': 5, 'document': changed})
    result = alice.json(path + '/generate', 'POST', {'version': 6, 'idempotencyKey': str(uuid.uuid4())})
    artifact = next(a for a in result['artifacts'] if a['key'] == 'primary')
    route = f'/api{path}/artifacts/{artifact["id"]}/download?version=7'
    docx = alice.raw(route)[0]; text = docx_text(docx)
    assert 'Verified sender' in text and 'Project director' in text and recipient['email'] in text
    assert 'Forged again' not in text and 'Rich table content' in text and 'متن نوشته‌شده در قالب' in text
    assert alice.json(path + '?version=7')['snapshot']['editor']['version'] == 6
    for method, suffix, value in [('PUT', '/editor', {'version': 7, 'document': working}), ('POST', '/editor', {'version': 7}), ('POST', '/editor/fields', {'version': 7})]:
        alice.json(path + suffix, method, value, expected=409)
    duplicate = alice.json(path + '/duplicate', 'POST', {'version': 7, 'title': 'Next edited letter'}, expected=201)
    duplicate_path = f'/letters/{duplicate["metadata"]["id"]}'
    assert duplicate['metadata']['hasEditor']
    duplicate_model = alice.json(duplicate_path + '/editor?version=1')['document']
    assert 'Rich table content' in json.dumps(duplicate_model)
    def racing_save(_):
        return alice.raw('/api' + duplicate_path + '/editor', 'PUT', json.dumps({'version': 1, 'document': duplicate_model}).encode(), expected=(200,409))[1]
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        assert sorted(pool.map(racing_save, range(2))) == [200,409]
    # Review flags block finalization/filling, but must never trap a user outside their editor.
    duplicate_model.setdefault('settings', {})['trackChanges'] = True
    alice.json(duplicate_path + '/editor', 'PUT', {'version': 2, 'document': duplicate_model})
    assert alice.json(duplicate_path + '/editor', 'POST', {'version': 3})['metadata']['version'] == 3
    assert any(i['code'] == 'review' for i in alice.json(duplicate_path + '/validate', 'POST', {'version': 3})['issues'])
    out = ROOT / 'Tests/generated'; out.mkdir(exist_ok=True)
    (out / 'edited-letter.docx').write_bytes(docx)
    (out / 'edited-letter.pdf').write_bytes(alice.raw(route + '&format=pdf')[0])
    print('PASS: editable template copies, partial setup, rich bookmarked body/table preservation, authoritative recipient/sender/title/signature filling, private versioned saves, concurrency, immutable finalization, duplication and review recovery.')
