"""Keep a disposable seeded API available for manual/browser UI checks; Enter stops it."""
from studio_test_support import Client, ROOT, png, server

with server(5099, 'studio-browser') as (base, _):
    author = Client(base); author.register('browser-author')
    Client(base).register('browser-recipient'); Client(base).register('browser-copy')
    author.json('/templates/starter', 'POST', {'title': 'Reusable document', 'kind': 'document', 'language': 'en-US'}, expected=201)
    author.json('/templates/starter', 'POST', {'title': 'Letter template', 'kind': 'letter', 'language': 'en-US'}, expected=201)
    author.json('/letters/senders/', 'POST', {'version': 0, 'fullName': 'Sample Author', 'title': 'Project lead', 'organization': 'Sample workspace', 'address': 'Example address'}, expected=201)
    author.multipart('/letters/images/', 'signature.png', png(), {'title': 'Fixture signature'})
    folder = ROOT / 'Tests/generated'; folder.mkdir(exist_ok=True)
    (folder / 'fixture-signature.png').write_bytes(png())
    print('Disposable studio API ready at ' + base, flush=True)
    input('Press Enter to stop the disposable API.\n')
