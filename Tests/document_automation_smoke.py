"""Real HTTP/native-engine coverage for document automation and private machine access."""
import base64
import concurrent.futures
import io
import json
import time
import urllib.error
import urllib.request
import zipfile

from studio_test_support import Client, docx_text, png, reference, server, template_model


def bearer(base, token, path, method='GET', value=None, expected=200, body=None, mime='application/json'):
    if value is not None:
        body = json.dumps(value, ensure_ascii=False).encode()
    headers = {'Authorization': 'Bearer ' + token}
    if body is not None:
        headers['Content-Type'] = mime
    try:
        response = urllib.request.urlopen(urllib.request.Request(base + '/api' + path, data=body, method=method, headers=headers), timeout=240)
    except urllib.error.HTTPError as error:
        response = error
    content = response.read()
    assert response.status == expected, (method, path, response.status, expected, content[:1200])
    return json.loads(content) if content and 'application/json' in response.headers.get('Content-Type', '') else content


def batch(client, document, operations, key=None, expected=200):
    return client.json(f'/documents/{document["id"]}/batch', 'POST', {'version': document['version'], 'operations': operations, 'idempotencyKey': key}, expected=expected)


def model(text):
    return {'body': [{'kind': 'para', 'text': text, 'runs': [{'len': len(text.encode()), 'props': {}}]}]}


def docx_fixture(body):
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        archive.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        archive.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>' + body + '<w:sectPr/></w:body></w:document>')
    return output.getvalue()


with server(5101, 'document-automation') as (base, data_dir):
    alice, bob, guest = Client(base), Client(base), Client(base)
    alice.register('automation-alice'); bob.register('automation-bob')
    capability = alice.json('/document-capabilities')
    assert 'replaceText' in capability['operations'] and capability['offsetUnit'] == 'utf8Bytes'
    spec = json.loads(guest.raw('/openapi/v1.json')[0])
    for status in ['400', '413', '422']:
        response = spec['paths']['/api/documents/{id}/batch']['post']['responses'][status]
        assert 'application/problem+json' in response['content'], response
    for path in ['/api/documents/{id}/structure', '/api/documents/{id}/batch', '/api/documents/compare', '/api/documents/compose', '/api/document-jobs/', '/api/automation-tokens/']:
        assert path.rstrip('/') in {route.rstrip('/') for route in spec['paths']}, path

    source = alice.upload(model('Customer علی Customer {{Missing}}'), 'Automation source')
    path = f'/documents/{source["id"]}'
    for action in ['structure', 'tables', 'styles', 'images', 'controls', 'properties', 'comments', 'changes', 'versions']:
        alice.json(path + f'/{action}?version=1')
        bob.json(path + f'/{action}?version=1', expected=404)
        guest.json(path + f'/{action}?version=1', expected=401)
        alice.json(path + f'/{action}?version=99', expected=409)
    alice.json(path + '/search', 'POST', {'version': 1, 'text': 'Customer'}, csrf=False, expected=400)
    alice.json(path + '/styles/apply', 'POST', {'version': 1, 'operations': [None]}, expected=400)
    alice.raw('/api' + path + '/batch', 'POST', b'{"version":1,"operations":[{"type":"properties","values":{"subject":"a","subject":"b"}}]}', expected=400)
    found = alice.json(path + '/search', 'POST', {'version': 1, 'text': 'Customer'})['result']
    assert found['count'] == 2 and found['matches'][0]['context']
    assert alice.json(path + '/validate', 'POST', {'version': 1})['result']['placeholders']
    header_model = model('Jump')
    header_model['body'][0]['runs'][0]['props']['link'] = '#HeaderTarget'
    header_model['parts'] = {'1': {'kind': 'header', 'blocks': template_model(('HeaderTarget',))['body']}}
    header_source = alice.upload(header_model, 'Header reference')
    header_validation = alice.json('/documents/' + header_source['id'] + '/validate', 'POST', {'version': 1})['result']
    assert header_validation['brokenReferences'] == [], header_validation['brokenReferences']
    batch(bob, source, [{'type': 'replaceText', 'find': 'Customer', 'replacement': 'stolen'}], expected=404)
    batch(alice, source, [{'type': 'replaceText', 'find': 'Customer', 'replacement': 'X'}, {'type': 'replaceText', 'find': 'absent', 'replacement': 'Y', 'expectedMatches': 1}], expected=422)
    assert alice.json(path)['version'] == 1
    assert alice.raw('/api' + path + '/pages/1/preview?version=1')[0].startswith(b'\x89PNG')
    alice.raw('/api' + path + '/pages/1/preview?version=99', expected=409)
    batch(alice, source, [{'type': 'execute', 'command': 'file.open', 'params': {'path': str(data_dir)}}], expected=400)
    batch(alice, source, [{'type': 'replaceText', 'find': 'Customer', 'replacement': 'invalid guard', 'expectedMatches': -1}], expected=422)
    batch(alice, source, [{'type': 'replaceText', 'find': 'Customer', 'replacement': 'invalid flag', 'matchCase': 'true'}], expected=422)
    batch(alice, source, [{'type': 'replaceRange', 'target': {'ref': 'b.0', 'start': 10, 'end': 11}, 'text': 'invalid offset'}], expected=422)
    preview = alice.json(path + '/preview', 'POST', {'version': 1, 'operations': [{'type': 'replaceText', 'find': 'Customer', 'replacement': 'Preview only', 'expectedMatches': 2}]})
    assert alice.raw(preview['downloadUrl'])[0].startswith(b'%PDF')
    bob.raw(preview['downloadUrl'], expected=404)
    assert alice.json(path)['version'] == 1

    operations = [{'type': 'replaceText', 'find': 'Customer', 'replacement': 'Client', 'expectedMatches': 2}, {'type': 'format', 'target': {'ref': 'b.0'}, 'values': {'style': 'Heading1', 'rtl': True, 'bold': True}}]
    changed = batch(alice, source, operations, 'replace-customer')
    assert changed['metadata']['version'] == 2
    assert batch(alice, source, operations, 'replace-customer') == changed
    batch(alice, source, operations + [{'type': 'properties', 'values': {'subject': 'different'}}], 'replace-customer', expected=409)
    batch(alice, source, operations, expected=409)
    current = changed['metadata']
    versions = alice.json(path + '/versions?version=2')['items']
    assert [v['version'] for v in versions] == [2, 1]
    old = alice.raw('/api' + path + '/versions/1/download?version=2&format=docx')[0]
    assert 'Customer' in docx_text(old) and 'Client' not in docx_text(old)
    restored = alice.json(path + '/restore', 'POST', {'version': 2, 'sourceVersion': 1, 'idempotencyKey': 'restore-1'})
    assert restored['version'] == 3
    assert alice.json(path + '/restore', 'POST', {'version': 2, 'sourceVersion': 1, 'idempotencyKey': 'restore-1'}) == restored
    assert 'Customer' in json.dumps(alice.json(path + '/content'))
    current = restored

    result = alice.json(path + '/properties', 'PATCH', {'version': current['version'], 'values': {'subject': 'Invoice', 'language': 'fa-IR', 'custom': {'InvoiceNumber': 'INV-42'}}})
    current = result['metadata']
    properties = alice.json(path + f'/properties?version={current["version"]}')['result']
    assert properties['core']['subject'] == 'Invoice' and properties['language'] == 'fa-IR'
    result = alice.json(path + '/comments', 'POST', {'version': current['version'], 'action': 'add', 'target': {'ref': 'b.0', 'start': 0, 'end': 8}, 'text': 'Check this name'})
    current = result['metadata']
    comments = alice.json(path + f'/comments?version={current["version"]}')['result']
    assert 'Check this name' in json.dumps(comments)
    comment_id = next(iter(alice.json(path + '/content')['comments']))
    for action in ['reply', 'resolve', 'delete']:
        result = alice.json(path + '/comments', 'POST', {'version': current['version'], 'action': action, 'target': {}, 'id': int(comment_id), 'text': 'Reviewed', 'resolved': True})
        current = result['metadata']

    # A normal model save is retained too; history is not limited to automation calls.
    before_save = current['version']
    current = alice.json(path + '/content', 'PUT', {'version': before_save, 'document': model('علي ك 123 مي رود')})
    proposed = alice.json(path + '/normalize', 'POST', {'version': current['version'], 'rules': ['persianLetters', 'persianDigits', 'halfSpace']})
    assert proposed['result']['applied'] is False and alice.json(path)['version'] == current['version']
    result = alice.json(path + '/normalize', 'POST', {'version': current['version'], 'rules': ['persianLetters', 'persianDigits'], 'apply': True})
    current = result['metadata']
    assert 'علی ک ۱۲۳' in json.dumps(alice.json(path + '/content'), ensure_ascii=False)
    assert any(v['version'] == before_save for v in alice.json(path + f'/versions?version={current["version"]}')['items'])

    picture = base64.b64encode(png()).decode()
    result = alice.json(path + '/images', 'POST', {'version': current['version'], 'target': {'ref': 'b.0', 'start': 0, 'end': 0}, 'data': picture, 'width': 60, 'alt': 'Original logo'})
    current = result['metadata']
    images = alice.json(path + f'/images?version={current["version"]}')['result']['items']
    assert len(images) == 1 and images[0]['alt'] == 'Original logo'
    result = alice.json(path + '/images/' + images[0]['ref'], 'PUT', {'version': current['version'], 'target': {}, 'data': picture, 'width': 80, 'alt': 'Updated logo'})
    current = result['metadata']
    assert alice.json(path + f'/images?version={current["version"]}')['result']['items'][0]['alt'] == 'Updated logo'

    # Build a genuine Word document with a table, a formatted split text run and typed controls.
    body = '''<w:p><w:r><w:t>Hel</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>lo World</w:t></w:r></w:p>
    <w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
    <w:p><w:sdt><w:sdtPr><w:tag w:val="Name"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>Old Name</w:t></w:r></w:sdtContent></w:sdt></w:p>
    <w:p><w:sdt><w:sdtPr><w:tag w:val="Choice"/><w:dropDownList><w:listItem w:displayText="First" w:value="one"/><w:listItem w:displayText="Second" w:value="two"/></w:dropDownList></w:sdtPr><w:sdtContent><w:r><w:t>First</w:t></w:r></w:sdtContent></w:sdt></w:p>'''
    structured = alice.multipart('/documents/upload', 'structured.docx', docx_fixture(body))
    structured_path = '/documents/' + structured['id']
    assert alice.json(structured_path + '/search', 'POST', {'version': 1, 'text': 'Hello'})['result']['count'] == 1
    table_ref = alice.json(structured_path + '/tables?version=1')['result']['items'][0]['ref']
    result = alice.json(structured_path + '/tables/' + table_ref, 'PATCH', {'version': 1, 'cells': [{'row': 0, 'column': 1, 'text': 'قیمت'}], 'appendRows': [['Item', '100']]})
    structured = result['metadata']
    result = alice.json(structured_path + '/controls/fill', 'POST', {'version': structured['version'], 'values': {'Name': 'علی رضایی', 'Choice': 'two'}})
    structured = result['metadata']
    controls = alice.json(structured_path + f'/controls?version={structured["version"]}')['result']['items']
    assert any(c['tag'] == 'Name' and c['text'] == 'علی رضایی' for c in controls)
    assert any(c['tag'] == 'Choice' and c['value'] == 'two' for c in controls)
    alice.json(structured_path + '/controls/fill', 'POST', {'version': structured['version'], 'values': {'Choice': 'invalid'}}, expected=422)
    exported = alice.raw('/api' + structured_path + f'/download?version={structured["version"]}&format=docx')[0]
    assert 'قیمت' in docx_text(exported) and 'علی رضایی' in docx_text(exported)

    original = alice.upload(model('Original words'), 'Original')
    revised = alice.upload(model('Revised words'), 'Revised')
    compared = alice.json('/documents/compare', 'POST', {'original': reference(original), 'revised': reference(revised), 'title': 'Comparison', 'idempotencyKey': 'compare-1'})
    comparison_path = '/documents/' + compared['metadata']['id']
    assert alice.json(comparison_path + '/changes?version=1')['result']
    resolved = alice.json(comparison_path + '/changes/resolve', 'POST', {'version': 1, 'decision': 'accept', 'all': True, 'target': {}})
    assert not alice.json(comparison_path + '/changes?version=2')['result']
    assert 'Revised words' in json.dumps(alice.json(comparison_path + '/content'))
    assembled = alice.json('/documents/compose', 'POST', {'sources': [reference(original), reference(revised)], 'title': 'Assembled', 'separator': 'pageBreak'})
    assembled_text = json.dumps(alice.json('/documents/' + assembled['metadata']['id'] + '/content'))
    assert 'Original words' in assembled_text and 'Revised words' in assembled_text
    bob.json('/documents/compare', 'POST', {'original': reference(original), 'revised': reference(revised), 'title': 'stolen'}, expected=404)

    template_source = alice.upload(template_model(), 'Template')
    saved_template = alice.json('/templates/', 'POST', {'title': 'Bulk template', 'kind': 'document', 'language': 'en-US', 'source': reference(template_source)}, expected=201)
    template_path = '/templates/' + saved_template['metadata']['id']
    render_request = {'version': 1, 'idempotencyKey': 'bulk-1', 'records': [{'key': 'a', 'title': 'Alice letter', 'values': {'CustomerName': 'Alice'}}, {'key': 'b', 'title': 'Bad letter', 'values': {'Unknown': 'x'}}]}
    rendered = alice.json(template_path + '/render-batch', 'POST', render_request)
    assert rendered['records'][0]['success'] and not rendered['records'][1]['success']
    assert alice.json(template_path + '/render-batch', 'POST', render_request) == rendered

    bulk_job = alice.json('/document-jobs/', 'POST', {'kind': 'renderBatch', 'template': reference(saved_template), 'records': render_request['records'], 'format': 'docx'})
    for _ in range(120):
        bulk_state = alice.json('/document-jobs/' + bulk_job['id'] + '?version=1')
        if bulk_state['status'] in ['succeeded', 'failed']:
            break
        time.sleep(.25)
    assert bulk_state['status'] == 'succeeded', bulk_state
    with zipfile.ZipFile(io.BytesIO(alice.raw(bulk_state['downloadUrl'])[0])) as archive:
        manifest = json.loads(archive.read('manifest.json'))
        assert manifest[0]['success'] and not manifest[1]['success']
        assert 'Alice' in docx_text(archive.read(manifest[0]['file']))

    job = alice.json('/document-jobs/', 'POST', {'kind': 'export', 'source': reference(original), 'format': 'pdf', 'idempotencyKey': 'export-1'})
    job_path = '/document-jobs/' + job['id']
    bob.json(job_path + '?version=1', expected=404)
    for _ in range(120):
        state = alice.json(job_path + '?version=1')
        if state['status'] in ['succeeded', 'failed', 'cancelled']:
            break
        time.sleep(.25)
    assert state['status'] == 'succeeded', state
    assert alice.raw(state['downloadUrl'])[0].startswith(b'%PDF')
    alice.raw('/api' + job_path + '/download?version=1', expected=409)
    for kind, extra in [('batch', {'source': reference(original), 'operations': [{'type': 'replaceText', 'find': 'Original', 'replacement': 'Background'}]}), ('compose', {'sources': [reference(original), reference(revised)]})]:
        created = alice.json('/document-jobs/', 'POST', {'kind': kind, 'format': 'docx', **extra})
        for _ in range(120):
            status = alice.json('/document-jobs/' + created['id'] + '?version=1')
            if status['status'] in ['succeeded', 'failed']:
                break
            time.sleep(.25)
        assert status['status'] == 'succeeded', status
        assert alice.raw(status['downloadUrl'])[0].startswith(b'PK')
    cancel = alice.json('/document-jobs/', 'POST', {'kind': 'export', 'source': reference(original)})
    cancelled = alice.json('/document-jobs/' + cancel['id'] + '/cancel', 'POST', {'version': 1}, expected=(200, 409))
    if cancelled.get('status') == 'cancelled':
        assert alice.json('/document-jobs/' + cancel['id'] + '?version=1')['status'] == 'cancelled'

    read_token = alice.json('/automation-tokens/', 'POST', {'name': 'Read only', 'scopes': ['documents:read']}, expected=201)
    secret = read_token['token']
    assert bearer(base, secret, '/documents/' + original['id'])['id'] == original['id']
    bearer(base, secret, '/documents/' + original['id'] + '/batch', 'POST', {'version': 1, 'operations': [{'type': 'properties', 'values': {'subject': 'forbidden'}}]}, expected=403)
    bearer(base, secret, '/documents/' + original['id'] + '/search', 'POST', {'version': 1, 'text': 'Original'})
    bearer(base, secret, '/admin/settings', expected=403)
    bearer(base, secret, '/automation-tokens/', expected=403)
    bearer(base, secret, '/document-jobs/', 'POST', {'kind': 'export', 'source': reference(original)}, expected=403)
    bob.json('/automation-tokens/' + read_token['id'] + '?version=1', 'DELETE', expected=404)
    write_token = alice.json('/automation-tokens/', 'POST', {'name': 'Writer', 'scopes': ['documents:write', 'jobs:write']}, expected=201)
    bearer(base, write_token['token'], '/documents/' + original['id'] + '/batch', 'POST', {'version': 1, 'operations': [{'type': 'properties', 'values': {'subject': 'Machine edited'}}]})
    # Multipart upload with header credentials must not require a cookie or CSRF token.
    boundary = 'automation-token-upload'
    upload = f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="machine.txt"\r\nContent-Type: text/plain\r\n\r\nMachine document\r\n--{boundary}--\r\n'.encode()
    bearer(base, write_token['token'], '/documents/upload', 'POST', body=upload, mime='multipart/form-data; boundary=' + boundary, expected=201)
    listed = alice.json('/automation-tokens/')
    assert all('hash' not in token and 'token' not in token for token in listed)
    alice.json('/automation-tokens/' + read_token['id'] + '?version=1', 'DELETE', expected=204)
    bearer(base, secret, '/documents/', expected=401)
    # Expiry is checked live, just like revocation. Only the disposable fixture database is edited.
    import sqlite3
    with sqlite3.connect(data_dir / 'inkstone.db') as database:
        database.execute('UPDATE AutomationTokens SET ExpiresAt=0 WHERE Id=?', (write_token['id'].upper(),))
        assert database.total_changes == 1
    database.close()
    bearer(base, write_token['token'], '/documents/', expected=401)

    # Concurrent writes advance once and leave a complete revision history.
    race_doc = alice.upload(model('Race'), 'Race')
    def race(value):
        return alice.raw('/api/documents/' + race_doc['id'] + '/batch', 'POST', json.dumps({'version': 1, 'operations': [{'type': 'replaceText', 'find': 'Race', 'replacement': value, 'expectedMatches': 1}]}).encode(), expected=(200, 409))[1]
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        assert sorted(pool.map(race, ['A', 'B'])) == [200, 409]
    assert len(alice.json('/documents/' + race_doc['id'] + '/versions?version=2')['items']) == 2
    alice.json('/documents/' + race_doc['id'] + '?version=2', 'DELETE', expected=204)
    with sqlite3.connect(data_dir / 'inkstone.db') as database:
        assert database.execute('SELECT COUNT(*) FROM DocumentRevisions WHERE DocumentId=?', (race_doc['id'].upper(),)).fetchone()[0] == 0
    database.close()
    print('PASS: structure/search, transactional edits, retry keys, DOCX tables and controls, formatting, images, comments, revisions, normalization, comparison, assembly, template batches, private previews/jobs, scoped tokens, revocation, CSRF and owner/version isolation.')
