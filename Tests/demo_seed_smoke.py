"""Demo provisioning is opt-in, repeatable, private and preserves existing work."""
import importlib.util
import json
from studio_test_support import Client, ROOT, server

spec = importlib.util.spec_from_file_location('demo_seed', ROOT/'Scripts/seed_letter_demo.py')
demo = importlib.util.module_from_spec(spec); spec.loader.exec_module(demo)
with server(5092, 'demo-seed') as (base, data):
    demo.seed(base,data)
    manifest = json.loads((data/'demo/accounts.json').read_text(encoding='utf-8'))
    assert len(manifest['accounts']) == 8 and len({a['password'] for a in manifest['accounts']}) == 8
    account = manifest['accounts'][0]
    api = Client(base); api.json('/account/login','POST',{'email':account['email'],'password':account['password']})
    assert len(api.json('/letters/directory')['items']) == 8
    before = api.json('/letters/')['items']
    assert len(before) == 3 and len(api.json('/templates/')['items']) == 5
    assert len(api.json('/letters/senders/')['items']) == 2 and len(api.json('/letters/images/')['items']) == 3
    letter = next(x for x in before if x['metadata']['state'] == 'draft')
    snapshot = api.json(f'/letters/{letter["metadata"]["id"]}/editor?version={letter["metadata"]["version"]}')
    assert snapshot['metadata']['hasEditor']
    assert api.json(f'/letters/{letter["metadata"]["id"]}/validate','POST',{'version':letter['metadata']['version']})['valid']
    # Re-run without resetting passwords, rewriting letters, or duplicating resources.
    demo.seed(base,data)
    after = api.json('/letters/')['items']
    assert {x['metadata']['id']:x['metadata']['version'] for x in before} == {x['metadata']['id']:x['metadata']['version'] for x in after}
    assert len(api.json('/templates/')['items']) == 5
    assert json.loads((data/'demo/accounts.json').read_text(encoding='utf-8'))['accounts'][0]['password'] == account['password']
    print('PASS: eight private demo accounts, populated letter resources, usable editor draft/finalized examples, unique private credentials and idempotent provisioning.')
